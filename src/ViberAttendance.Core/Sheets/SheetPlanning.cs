using System.Globalization;
using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Models;

namespace ViberAttendance.Core.Sheets;

public sealed record SheetCell(int Row, int Column, string Value);

public sealed record SheetColor(int Row, int Column, string Hex);

public sealed record DetailSheetPerson(
    string RecordId,
    string Name,
    string OnlineText,
    string OfflineText,
    string Status,
    string DurationText);

public sealed record TimeSheetPerson(
    string Name,
    DateTimeOffset FirstOnlineLocal,
    TimeSpan TotalDuration,
    string Note);

public sealed record DetailSheetPlan(IReadOnlyList<SheetCell> Cells);

public sealed record TimeSheetPlan(
    IReadOnlyList<SheetCell> Cells,
    IReadOnlyList<SheetColor> Colors,
    IReadOnlyList<string> OverflowNames);

public static class AttendanceSheetRows
{
    public static IReadOnlyList<DetailSheetPerson> BuildDetail(
        Guid callId,
        IReadOnlyList<AttendanceIntervalRecord> intervals) =>
        Group(intervals).Select(group =>
        {
            var online = group.Min(item => item.OnlineAtUtc ?? item.FirstSeenAtUtc);
            var offline = group
                .Select(item => item.OfflineAtUtc)
                .Where(item => item is not null)
                .Select(item => item!.Value)
                .DefaultIfEmpty(online)
                .Max();
            var status = StatusOf(group);
            return new DetailSheetPerson(
                $"{callId:N}:{group.Key:N}",
                group.First().DisplayName,
                FormatClock(online),
                status == OnlineStatus ? string.Empty : FormatClock(offline),
                status,
                FormatMinutes(TimeSpan.FromTicks(group.Sum(item => Math.Max(0, item.Duration.Ticks)))));
        }).ToArray();

    public static IReadOnlyList<TimeSheetPerson> BuildTime(IReadOnlyList<AttendanceIntervalRecord> intervals) =>
        Group(intervals).Select(group =>
        {
            var ordered = group.OrderBy(item => item.OnlineAtUtc ?? item.FirstSeenAtUtc).ToArray();
            var first = ordered[0].OnlineAtUtc ?? ordered[0].FirstSeenAtUtc;
            return new TimeSheetPerson(
                ordered[^1].DisplayName,
                first.ToLocalTime(),
                TimeSpan.FromTicks(ordered.Sum(item => Math.Max(0, item.Duration.Ticks))),
                FormatNote(ordered));
        }).ToArray();

    public static string FormatTimeCell(TimeSheetPerson person, TimeMarkScheme scheme)
    {
        var (symbol, _) = scheme.Resolve(person.TotalDuration);
        var minutes = (int)Math.Floor(Math.Max(0, person.TotalDuration.TotalMinutes));
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{person.FirstOnlineLocal:M/d} {symbol} {minutes}分钟");
        if (!string.IsNullOrWhiteSpace(scheme.Suffix))
        {
            text = $"{text} {scheme.Suffix.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(scheme.PrefixText) && minutes >= scheme.PrefixMinimumMinutes)
        {
            text = $"{scheme.PrefixText.Trim()} {text}";
        }

        return string.IsNullOrWhiteSpace(person.Note) ? text : $"{text} {person.Note}";
    }

    private static string FormatNote(IReadOnlyList<AttendanceIntervalRecord> segments) =>
        string.Join("；", segments.Select(segment =>
        {
            var online = (segment.OnlineAtUtc ?? segment.FirstSeenAtUtc).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
            var interrupted = segment.Status == UserFacingStatuses.InterruptedStatus;
            var offline = segment.OfflineAtUtc is DateTimeOffset value
                ? value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)
                : "未下线";
            return interrupted
                ? $"上线 {online} 下线 {offline} 程序中断"
                : $"上线 {online} 下线 {offline}";
        }));

    private const string OnlineStatus = "entrar en línea";
    private const string OfflineStatus = "desconectado";

    private static string StatusOf(IGrouping<Guid, AttendanceIntervalRecord> group)
    {
        if (group.Any(item => item.Status is "Online" or "CandidateOffline"))
        {
            return OnlineStatus;
        }

        return group.Any(item => item.Status == UserFacingStatuses.InterruptedStatus)
            ? "程序中断"
            : OfflineStatus;
    }

    private static IEnumerable<IGrouping<Guid, AttendanceIntervalRecord>> Group(
        IReadOnlyList<AttendanceIntervalRecord> intervals) =>
        intervals.GroupBy(item => item.MemberId);

    private static string FormatClock(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public static IReadOnlyList<DetailSheetPerson> CollapseByName(IReadOnlyList<DetailSheetPerson> people)
    {
        var merged = new List<DetailSheetPerson>();
        var indexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var person in people)
        {
            var key = person.Name.Trim();
            if (!indexByName.TryGetValue(key, out var index))
            {
                indexByName[key] = merged.Count;
                merged.Add(person);
                continue;
            }

            var current = merged[index];
            var minutes = ReadMinutes(current.DurationText) + ReadMinutes(person.DurationText);
            merged[index] = current with
            {
                DurationText = minutes.ToString(CultureInfo.InvariantCulture) + "分钟"
            };
        }

        return merged;
    }

    private static string FormatMinutes(TimeSpan value)
    {
        var minutes = (int)Math.Floor(Math.Max(0, value.TotalMinutes));
        return minutes.ToString(CultureInfo.InvariantCulture) + "分钟";
    }

    private static int ReadMinutes(string text)
    {
        var digits = text.Trim();
        var end = 0;
        while (end < digits.Length && digits[end] is >= '0' and <= '9')
        {
            end++;
        }

        return end > 0 && int.TryParse(digits[..end], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            ? minutes
            : 0;
    }
}

public readonly record struct DetailSheetColumns(
    int Name,
    int Online,
    int Offline,
    int Status,
    int Duration,
    int? Code = null)
{
    public static DetailSheetColumns Default { get; } = new(0, 1, 2, 3, 4);

    public static DetailSheetColumns From(SheetSyncSettings settings)
    {
        SheetColumns.TryParse(settings.DetailNameColumn, out var name);
        SheetColumns.TryParse(settings.DetailOnlineColumn, out var online);
        SheetColumns.TryParse(settings.DetailOfflineColumn, out var offline);
        SheetColumns.TryParse(settings.DetailStatusColumn, out var status);
        SheetColumns.TryParse(settings.DetailDurationColumn, out var duration);
        int? code = null;
        if (settings.DetailCodeEnabled &&
            SheetColumns.TryParse(settings.DetailCodeColumn, out var parsed) &&
            parsed != name &&
            parsed != online &&
            parsed != offline &&
            parsed != status &&
            parsed != duration)
        {
            code = parsed;
        }

        return new(name, online, offline, status, duration, code);
    }
}

public static class DetailSheetPlanner
{
    public static DetailSheetPlan Plan(
        IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyList<DetailSheetPerson> people) =>
        Plan(rows, people, DetailSheetColumns.Default);

    public static DetailSheetPlan Plan(
        IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyList<DetailSheetPerson> people,
        DetailSheetColumns columns,
        IReadOnlyDictionary<string, string>? codes = null)
    {
        var headers = new List<(int Column, string Text)>
        {
            (columns.Name, "姓名"),
            (columns.Online, "上线时间"),
            (columns.Offline, "下线时间"),
            (columns.Status, "当前状态"),
            (columns.Duration, "累计时长")
        };
        if (columns.Code is int headerColumn)
        {
            headers.Add((headerColumn, "自定义号码"));
        }
        var cells = new List<SheetCell>();
        var hasHeader = rows.Count > 0 && rows[0].Any(cell => cell.Trim() == "姓名");
        var writeHeader = !hasHeader && (rows.Count == 0 || IsBlank(rows[0]));
        if (writeHeader)
        {
            var written = new HashSet<int>();
            foreach (var (column, text) in headers)
            {
                if (written.Add(column))
                {
                    cells.Add(new SheetCell(1, column, text));
                }
            }
        }
        else if (hasHeader && columns.Code is int codeColumn && string.IsNullOrWhiteSpace(Cell(rows, 0, codeColumn)))
        {
            cells.Add(new SheetCell(1, codeColumn, "自定义号码"));
        }

        var occupied = new HashSet<int>();
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (writeHeader || hasHeader)
        {
            occupied.Add(1);
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var rowNumber = index + 1;
            if (occupied.Contains(rowNumber))
            {
                continue;
            }

            if (!IsBlank(rows[index]))
            {
                occupied.Add(rowNumber);
            }

            var name = Cell(rows, index, columns.Name).Trim();
            if (name.Length > 0)
            {
                byName.TryAdd(name, rowNumber);
            }
        }

        var cursor = 1;
        foreach (var person in AttendanceSheetRows.CollapseByName(people))
        {
            var key = person.Name.Trim();
            var row = byName.TryGetValue(key, out var existing)
                ? existing
                : NextEmpty(occupied, ref cursor);
            if (!byName.ContainsKey(key))
            {
                occupied.Add(row);
                byName[key] = row;
            }

            cells.Add(new SheetCell(row, columns.Name, person.Name));
            cells.Add(new SheetCell(row, columns.Online, person.OnlineText));
            cells.Add(new SheetCell(row, columns.Offline, person.OfflineText));
            cells.Add(new SheetCell(row, columns.Status, person.Status));
            cells.Add(new SheetCell(row, columns.Duration, person.DurationText));
            if (columns.Code is int codeColumn)
            {
                cells.Add(new SheetCell(row, codeColumn, CustomNumberDirectory.Find(codes, person.Name)));
            }
        }

        return new DetailSheetPlan(cells);
    }

    private static int NextEmpty(HashSet<int> occupied, ref int cursor)
    {
        while (occupied.Contains(cursor))
        {
            cursor++;
        }

        var row = cursor;
        cursor++;
        return row;
    }

    private static bool IsBlank(IReadOnlyList<string> row) =>
        row.All(string.IsNullOrWhiteSpace);

    private static string Cell(IReadOnlyList<IReadOnlyList<string>> rows, int row, int column) =>
        row < rows.Count && column < rows[row].Count ? rows[row][column] : string.Empty;
}

public static class TimeSheetPlanner
{
    public static TimeSheetPlan Plan(
        IReadOnlyList<IReadOnlyList<string>> rows,
        SheetSyncSettings settings,
        IReadOnlyList<TimeSheetPerson> people)
    {
        var scheme = settings.ActiveScheme();
        SheetColumns.TryParse(settings.NameColumn, out var nameColumn);
        SheetColumns.TryParse(settings.TimeColumn, out var timeColumn);
        int? codeColumn = settings.TimeCodeEnabled &&
            SheetColumns.TryParse(settings.TimeCodeColumn, out var parsedCode) &&
            parsedCode != nameColumn &&
            parsedCode != timeColumn
            ? parsedCode
            : null;
        var codes = CustomNumberDirectory.Index(settings.CustomNumbers);
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var empty = new List<int>();
        for (var row = settings.StartRow; row <= settings.EndRow; row++)
        {
            var name = Cell(rows, row - 1, nameColumn).Trim();
            if (name.Length == 0)
            {
                empty.Add(row);
            }
            else if (!used.ContainsKey(name))
            {
                used[name] = row;
            }
        }

        var cells = new List<SheetCell>();
        var colors = new List<SheetColor>();
        var overflow = new List<string>();
        var emptyIndex = 0;
        foreach (var person in people)
        {
            var key = person.Name.Trim();
            int row;
            var isNew = false;
            if (used.TryGetValue(key, out var existing))
            {
                row = existing;
            }
            else if (emptyIndex < empty.Count)
            {
                row = empty[emptyIndex++];
                used[key] = row;
                isNew = true;
            }
            else
            {
                overflow.Add(person.Name);
                continue;
            }

            if (isNew)
            {
                cells.Add(new SheetCell(row, nameColumn, person.Name));
            }

            var color = scheme.Resolve(person.TotalDuration).Color;
            cells.Add(new SheetCell(row, timeColumn, AttendanceSheetRows.FormatTimeCell(person, scheme)));
            colors.Add(new SheetColor(row, timeColumn, color));
            if (codeColumn is int column)
            {
                cells.Add(new SheetCell(row, column, CustomNumberDirectory.Find(codes, person.Name)));
            }
        }

        return new TimeSheetPlan(cells, colors, overflow);
    }

    private static string Cell(IReadOnlyList<IReadOnlyList<string>> rows, int row, int column) =>
        row >= 0 && row < rows.Count && column < rows[row].Count ? rows[row][column] : string.Empty;
}
