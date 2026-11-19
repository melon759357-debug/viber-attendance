using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Contracts;
using ViberAttendance.Core.Models;
using ViberAttendance.Core.Sheets;

namespace ViberAttendance.App.Services;

public sealed class SheetAttendanceSync(
    AppSettings settings,
    IAttendanceRepository attendanceRepository,
    ISheetGridClient sheetClient,
    IDiagnosticLogger logger,
    IClock clock) : ISheetAttendanceSync
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _watchCts;
    private Guid? _watchedCallId;

    public void Watch(Guid callId)
    {
        _watchedCallId = callId;
        if (_watchCts is not null)
        {
            return;
        }

        _watchCts = new CancellationTokenSource();
        _ = WatchLoopAsync(_watchCts.Token);
    }

    public void Unwatch()
    {
        _watchedCallId = null;
        _watchCts?.Cancel();
        _watchCts?.Dispose();
        _watchCts = null;
    }

    public async Task PushInterruptedAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSync())
        {
            return;
        }

        var cutoff = clock.UtcNow.AddHours(-36);
        var calls = await attendanceRepository.GetCallsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var call in calls.Where(call =>
                     call.Status == UserFacingStatuses.InterruptedStatus &&
                     call.EndedAtUtc is DateTimeOffset ended &&
                     ended >= cutoff))
        {
            await PushCallAsync(call.CallId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<string> PushHistoryAsync(CancellationToken cancellationToken = default)
    {
        var sheet = settings.SheetSync;
        if (!CanSync() || !sheet.DetailEnabled || string.IsNullOrWhiteSpace(sheet.DetailSpreadsheetUrl))
        {
            return "请先在实时页面打开「写入实时表格」并保存。";
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var calls = await attendanceRepository.GetCallsAsync(cancellationToken).ConfigureAwait(false);
            var people = new List<DetailSheetPerson>();
            var callCount = 0;
            foreach (var call in calls)
            {
                var intervals = await attendanceRepository.GetCallIntervalsAsync(call.CallId, cancellationToken)
                    .ConfigureAwait(false);
                if (intervals.Count == 0)
                {
                    continue;
                }

                callCount++;
                people.AddRange(AttendanceSheetRows.BuildDetail(call.CallId, intervals));
            }

            if (people.Count == 0)
            {
                return "没有可写入的历史记录。";
            }

            await WriteDetailPeopleAsync(people, cancellationToken).ConfigureAwait(false);
            await logger.WriteAsync(
                DiagnosticLogLevel.Information,
                "sheet.history.completed",
                $"历史通话={callCount}，明细行={people.Count}。",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return $"已写入实时表格：{callCount} 场，{people.Count} 人。";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await logger.WriteAsync(
                DiagnosticLogLevel.Warning,
                "sheet.history.failed",
                $"历史写入失败：{exception.GetType().Name}",
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return UserFailure(exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PushCallAsync(Guid callId, CancellationToken cancellationToken = default)
    {
        if (!CanSync())
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var intervals = await attendanceRepository.GetCallIntervalsAsync(callId, cancellationToken)
                .ConfigureAwait(false);
            if (intervals.Count == 0)
            {
                return;
            }

            var detailCount = 0;
            var sheet = settings.SheetSync;
            if (sheet.DetailEnabled && !string.IsNullOrWhiteSpace(sheet.DetailSpreadsheetUrl))
            {
                detailCount = await WriteDetailAsync(callId, intervals, cancellationToken).ConfigureAwait(false);
            }

            await logger.WriteAsync(
                DiagnosticLogLevel.Information,
                "sheet.sync.completed",
                $"明细行={detailCount}。",
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await logger.WriteAsync(
                DiagnosticLogLevel.Warning,
                "sheet.sync.failed",
                $"谷歌表格写入失败：{exception.GetType().Name}",
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<int> WriteDetailAsync(
        Guid callId,
        IReadOnlyList<AttendanceIntervalRecord> intervals,
        CancellationToken cancellationToken)
    {
        var people = AttendanceSheetRows.BuildDetail(callId, intervals);
        await WriteDetailPeopleAsync(people, cancellationToken).ConfigureAwait(false);
        return people.Count;
    }

    private async Task WriteDetailPeopleAsync(
        IReadOnlyList<DetailSheetPerson> people,
        CancellationToken cancellationToken)
    {
        var sheet = settings.SheetSync;
        var grid = await ReadGridAsync(sheet.DetailSpreadsheetUrl, sheet.DetailSheetName, cancellationToken)
            .ConfigureAwait(false);
        var columns = DetailSheetColumns.From(sheet);
        var codes = CustomNumberDirectory.Index(sheet.CustomNumbers);
        var plan = DetailSheetPlanner.Plan(grid, people, columns, codes);
        await sheetClient.WriteAsync(
            sheet.DetailSpreadsheetUrl,
            sheet.DetailSheetName,
            plan.Cells,
            [],
            cancellationToken).ConfigureAwait(false);

        var verify = await ReadGridAsync(sheet.DetailSpreadsheetUrl, sheet.DetailSheetName, cancellationToken)
            .ConfigureAwait(false);
        var missing = AttendanceSheetRows.CollapseByName(people)
            .Where(person => !ContainsName(verify, person.Name, columns.Name))
            .ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        var retry = DetailSheetPlanner.Plan(verify, missing, columns, codes);
        await sheetClient.WriteAsync(
            sheet.DetailSpreadsheetUrl,
            sheet.DetailSheetName,
            retry.Cells,
            [],
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> RegisterTimeAsync(CancellationToken cancellationToken = default)
    {
        var sheet = settings.SheetSync;
        if (!sheet.TimeEnabled || string.IsNullOrWhiteSpace(sheet.TimeSpreadsheetUrl))
        {
            return "请先打开「写入最终表格」并填写链接。";
        }

        var callId = _watchedCallId;
        if (callId is null)
        {
            var calls = await attendanceRepository.GetCallsAsync(cancellationToken).ConfigureAwait(false);
            callId = calls.Count == 0 ? null : calls[0].CallId;
        }

        if (callId is not Guid id)
        {
            return "没有可登记的通话。";
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var intervals = await attendanceRepository.GetCallIntervalsAsync(id, cancellationToken).ConfigureAwait(false);
            if (intervals.Count == 0)
            {
                return "这场还没有可登记的记录。";
            }

            var (written, overflow) = await WriteTimeAsync(intervals, cancellationToken).ConfigureAwait(false);
            await logger.WriteAsync(
                DiagnosticLogLevel.Information,
                "sheet.time.registered",
                $"时间行={written}，行区已满未写入={overflow}。",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return overflow == 0
                ? $"已登记 {written} 人。"
                : $"已登记 {written} 人。有 {overflow} 人超出结束行，没有写入。";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await logger.WriteAsync(
                DiagnosticLogLevel.Warning,
                "sheet.time.failed",
                $"最终表格登记失败：{exception.GetType().Name}",
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return UserFailure(exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> RefreshCustomNumbersAsync(CancellationToken cancellationToken = default)
    {
        var sheet = settings.SheetSync;
        var live = sheet.DetailEnabled && sheet.DetailCodeEnabled && !string.IsNullOrWhiteSpace(sheet.DetailSpreadsheetUrl)
            ? await PushHistoryAsync(cancellationToken).ConfigureAwait(false)
            : "实时表格这次没有写号码。";

        if (!sheet.TimeEnabled || !sheet.TimeCodeEnabled || string.IsNullOrWhiteSpace(sheet.TimeSpreadsheetUrl))
        {
            return live + "最终表格这次没有写号码。";
        }

        if (!SheetColumns.TryParse(sheet.NameColumn, out var nameColumn) ||
            !SheetColumns.TryParse(sheet.TimeCodeColumn, out var codeColumn) ||
            codeColumn == nameColumn ||
            (SheetColumns.TryParse(sheet.TimeColumn, out var timeColumn) && codeColumn == timeColumn))
        {
            return live + "最终表格的号码列和姓名或时间列撞在一起，没有写。";
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var grid = await ReadGridAsync(sheet.TimeSpreadsheetUrl, sheet.TimeSheetName, cancellationToken)
                .ConfigureAwait(false);
            var codes = CustomNumberDirectory.Index(sheet.CustomNumbers);
            var cells = new List<SheetCell>();
            for (var row = sheet.StartRow; row <= sheet.EndRow; row++)
            {
                var line = row - 1;
                var name = line >= 0 && line < grid.Count && nameColumn < grid[line].Count
                    ? grid[line][nameColumn]
                    : string.Empty;
                if (CustomNumberDirectory.Key(name).Length == 0)
                {
                    continue;
                }

                cells.Add(new SheetCell(row, codeColumn, CustomNumberDirectory.Find(codes, name)));
            }

            if (cells.Count == 0)
            {
                return live + "最终表格里没有已有的人，号码没有写。";
            }

            await sheetClient.WriteAsync(sheet.TimeSpreadsheetUrl, sheet.TimeSheetName, cells, [], cancellationToken)
                .ConfigureAwait(false);
            return live + "最终表格里已有的人已补上号码，时间格子没有改。";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await logger.WriteAsync(
                DiagnosticLogLevel.Warning,
                "sheet.numbers.failed",
                $"自定义号码没有写进最终表格：{exception.GetType().Name}",
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return live + "最终表格这次没有写上。";
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(int Written, int Overflow)> WriteTimeAsync(
        IReadOnlyList<AttendanceIntervalRecord> intervals,
        CancellationToken cancellationToken)
    {
        var people = AttendanceSheetRows.BuildTime(intervals);
        var sheet = settings.SheetSync;
        var grid = await ReadGridAsync(sheet.TimeSpreadsheetUrl, sheet.TimeSheetName, cancellationToken)
            .ConfigureAwait(false);
        var plan = TimeSheetPlanner.Plan(grid, sheet, people);
        await sheetClient.WriteAsync(
            sheet.TimeSpreadsheetUrl,
            sheet.TimeSheetName,
            plan.Cells,
            plan.Colors,
            cancellationToken).ConfigureAwait(false);
        if (plan.OverflowNames.Count == 0)
        {
            var verify = await ReadGridAsync(sheet.TimeSpreadsheetUrl, sheet.TimeSheetName, cancellationToken)
                .ConfigureAwait(false);
            var names = NamesInRange(verify, sheet);
            if (people.Any(person =>
                {
                    var key = CustomNumberDirectory.Key(person.Name);
                    return key.Length > 0 && !names.Contains(key);
                }))
            {
                var retry = TimeSheetPlanner.Plan(verify, sheet, people);
                await sheetClient.WriteAsync(
                    sheet.TimeSpreadsheetUrl,
                    sheet.TimeSheetName,
                    retry.Cells,
                    retry.Colors,
                    cancellationToken).ConfigureAwait(false);
                return (people.Count - retry.OverflowNames.Count, retry.OverflowNames.Count);
            }
        }

        return (people.Count - plan.OverflowNames.Count, plan.OverflowNames.Count);
    }

    private async Task WatchLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.SheetSync.IntervalSeconds), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (_watchedCallId is not Guid callId)
            {
                continue;
            }

            try
            {
                await PushHistoryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static string UserFailure(Exception exception)
    {
        var message = exception.Message;
        return exception is InvalidOperationException &&
               message.Length <= 80 &&
               !message.Contains("http", StringComparison.OrdinalIgnoreCase)
            ? message
            : "表格写入没有成功。";
    }

    private bool CanSync() =>
        settings.SheetSync.Enabled &&
        (settings.SheetSync.DetailEnabled || settings.SheetSync.TimeEnabled);

    private Task<IReadOnlyList<IReadOnlyList<string>>> ReadGridAsync(
        string spreadsheetUrl,
        string sheetName,
        CancellationToken cancellationToken) =>
        sheetClient.ReadAsync(
            spreadsheetUrl,
            sheetName,
            SheetColumns.ReadColumnCount(settings.SheetSync),
            cancellationToken);

    private static bool ContainsName(IReadOnlyList<IReadOnlyList<string>> rows, string name, int column)
    {
        var key = CustomNumberDirectory.Key(name);
        return key.Length > 0 && rows.Any(row =>
            column < row.Count &&
            string.Equals(CustomNumberDirectory.Key(row[column]), key, StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<string> NamesInRange(IReadOnlyList<IReadOnlyList<string>> rows, SheetSyncSettings sheet)
    {
        SheetColumns.TryParse(sheet.NameColumn, out var column);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var row = sheet.StartRow; row <= sheet.EndRow && row - 1 < rows.Count; row++)
        {
            var line = rows[row - 1];
            if (column >= line.Count)
            {
                continue;
            }

            var key = CustomNumberDirectory.Key(line[column]);
            if (key.Length > 0)
            {
                names.Add(key);
            }
        }

        return names;
    }
}
