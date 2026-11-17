using System.Globalization;
using System.IO;
using System.Text;
using ViberAttendance.App.ViewModels;
using ViberAttendance.Core.Attendance;
using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Contracts;
using ViberAttendance.Core.Models;

namespace ViberAttendance.App.Services;

public sealed record MonitoringUiSnapshot(
    ConnectionState ConnectionState,
    string ConnectionStatus,
    string MonitoringStatus,
    DateTimeOffset? CurrentCallStartTime,
    IReadOnlyList<ParticipantRowViewModel> Participants,
    bool IsDetecting);

public sealed class MonitoringCoordinator(
    AppSettings settings,
    IViberWindowLocator windowLocator,
    IUiAutomationParticipantDetector participantDetector,
    IAttendanceRepository attendanceRepository,
    IClock clock,
    IDiagnosticLogger logger,
    ISheetAttendanceSync? sheetSync = null) : IAsyncDisposable
{
    private static readonly TimeSpan DetectionTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private CancellationTokenSource? _monitoringCts;
    private Task? _monitoringTask;
    private ParticipantPresenceTracker? _tracker;
    private Guid? _currentCallId;
    private Guid? _lastCompletedCallId;
    private DateTimeOffset? _currentCallStartTime;
    private DateTimeOffset? _lastViberSeenAt;
    private DateTimeOffset? _lastParticipantListSeenAt;
    private IReadOnlyList<ParticipantPresenceSnapshot> _participants = [];
    private ConnectionState _connectionState = ConnectionState.ViberNotRunning;
    private string _connectionStatus = UserFacingStatuses.ViberNotRunning;
    private string _monitoringStatus = "未监控";
    private bool _isDetecting;
    private bool _loopRunning;

    public event EventHandler<MonitoringUiSnapshot>? SnapshotChanged;

    public Guid? CurrentCallId => _currentCallId;
    public ConnectionState ConnectionState => _connectionState;
    public bool IsDetecting => _isDetecting;
    public bool IsLoopRunning => _loopRunning;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var alreadyRunning = false;
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_monitoringTask is { IsCompleted: false })
            {
                alreadyRunning = true;
            }
            else
            {
                _monitoringCts?.Dispose();
                _monitoringCts = new CancellationTokenSource();
                _connectionState = ConnectionState.ViberNotRunning;
                _connectionStatus = UserFacingStatuses.ViberNotRunning;
                _monitoringStatus = "正在寻找 Viber";
                _loopRunning = true;
                PublishSnapshot(clock.UtcNow);
                _monitoringTask = RunLoopAsync(_monitoringCts.Token);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }

        if (alreadyRunning)
        {
            await DetectOnceAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RetryDetectionAsync(CancellationToken cancellationToken = default)
    {
        if (_monitoringTask is null or { IsCompleted: true })
        {
            await StartAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await DetectOnceAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(string reason = "ManualStop", CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _monitoringCts?.Cancel();
            if (_monitoringTask is not null)
            {
                try
                {
                    await _monitoringTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected when the user stops monitoring.
                }
            }

            await CompleteCurrentCallAsync(reason, cancellationToken).ConfigureAwait(false);
            _monitoringStatus = "已暂停";
            _loopRunning = false;
            if (_connectionState is not ConnectionState.ViberNotRunning
                and not ConnectionState.DetectionTemporarilyFailed)
            {
                _connectionStatus = "本场记录已保存";
            }

            PublishSnapshot(clock.UtcNow);
        }
        finally
        {
            _monitoringTask = null;
            _monitoringCts?.Dispose();
            _monitoringCts = null;
            _lifecycleGate.Release();
        }
    }

    public async Task<string?> ExportCurrentAsync(CancellationToken cancellationToken = default)
    {
        var callId = _currentCallId ?? _lastCompletedCallId;
        if (callId is null)
        {
            return null;
        }

        return await ExportCallAsync(callId.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ExportCallAsync(Guid callId, CancellationToken cancellationToken = default)
    {
        var rows = await attendanceRepository.GetCallIntervalsAsync(callId, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(settings.ExportFolder);
        var path = Path.Combine(settings.ExportFolder, $"Viber在线记录-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.csv");
        var builder = new StringBuilder("姓名,内部编号,上线时间,下线时间,本段在线时长,状态\r\n");
        foreach (var row in rows)
        {
            builder.Append(EscapeCsv(row.DisplayName)).Append(',')
                .Append(EscapeCsv(row.StableUiIdentity ?? string.Empty)).Append(',')
                .Append(FormatLocalTime(row.OnlineAtUtc)).Append(',')
                .Append(FormatLocalTime(row.OfflineAtUtc)).Append(',')
                .Append(FormatDuration(row.Duration)).Append(',')
                .Append(EscapeCsv(UserFacingStatuses.DescribeIntervalStatus(row.Status, row.OfflineAtUtc is not null)))
                .Append("\r\n");
        }

        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(true), cancellationToken)
            .ConfigureAwait(false);
        return path;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await DetectOnceAsync(cancellationToken).ConfigureAwait(false);
                var delay = GetAdaptiveDelay(_connectionState);
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal stop.
        }
        catch (Exception exception)
        {
            await logger.WriteAsync(
                DiagnosticLogLevel.Error,
                "monitoring.loop.failed",
                "监控循环发生技术错误，将在下一轮重试。",
                exception,
                CancellationToken.None).ConfigureAwait(false);
            SetState(ConnectionState.DetectionTemporarilyFailed, "已暂停");
            PublishSnapshot(clock.UtcNow);
        }
        finally
        {
            _loopRunning = false;
            _isDetecting = false;
            PublishSnapshot(clock.UtcNow);
        }
    }

    private async Task DetectOnceAsync(CancellationToken cancellationToken)
    {
        if (!await _scanGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        _isDetecting = true;
        PublishSnapshot(clock.UtcNow);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DetectionTimeout);
            await ScanOnceAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            await logger.WriteAsync(
                DiagnosticLogLevel.Warning,
                "monitoring.detect.timeout",
                "单次检测超时，将在下一轮重试。",
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            ApplyTemporaryFailure();
            PublishSnapshot(clock.UtcNow);
        }
        catch (Exception exception)
        {
            await logger.WriteAsync(
                DiagnosticLogLevel.Error,
                "monitoring.detect.failed",
                "单次检测失败，将在下一轮重试。",
                exception,
                CancellationToken.None).ConfigureAwait(false);
            ApplyTemporaryFailure();
            PublishSnapshot(clock.UtcNow);
        }
        finally
        {
            _isDetecting = false;
            PublishSnapshot(clock.UtcNow);
            _scanGate.Release();
        }
    }

    private async Task ScanOnceAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var previous = _connectionState;
        var discovery = await windowLocator.FindAsync(cancellationToken).ConfigureAwait(false);

        if (discovery.ProcessCount == 0)
        {
            // 仅在确实没有进程时才未运行；短暂丢失不立即清掉已连接。
            if (_lastViberSeenAt is DateTimeOffset lastSeen &&
                now - lastSeen < TimeSpan.FromSeconds(settings.OfflineWaitSeconds) &&
                UserFacingStatuses.ImpliesViberRunning(previous))
            {
                SetState(ConnectionState.DetectionTemporarilyFailed, "正在重试");
            }
            else
            {
                SetState(ConnectionState.ViberNotRunning, "正在寻找 Viber");
            }

            PublishSnapshot(now);

            if (_currentCallId is not null &&
                _lastViberSeenAt is DateTimeOffset seen &&
                now - seen >= TimeSpan.FromSeconds(settings.OfflineWaitSeconds))
            {
                await CompleteCurrentCallAsync("ViberClosed", cancellationToken).ConfigureAwait(false);
                SetState(ConnectionState.ViberNotRunning, "正在寻找 Viber");
                PublishSnapshot(now);
            }

            return;
        }

        _lastViberSeenAt = now;

        if (!discovery.HasDiscoveredWindow)
        {
            SetState(ConnectionState.ViberProcessFound, "正在寻找窗口");
            PublishSnapshot(now);
            return;
        }

        var window = discovery.CallWindow ?? discovery.MainWindow ??
            (discovery.Candidates.Count > 0 ? discovery.Candidates[0] : null) ??
            discovery.AllWindows.FirstOrDefault(candidate =>
                candidate.IsWindow &&
                !candidate.IsChildWindow &&
                candidate.Width > 0 &&
                candidate.Height > 0 &&
                (candidate.IsVisible || candidate.IsMinimized));

        if (window is null)
        {
            // 有进程且 AllWindows 表明存在窗口痕迹时，至少保持窗口已找到。
            SetState(
                discovery.AllWindows.Count > 0
                    ? ConnectionState.ViberWindowFound
                    : ConnectionState.ViberProcessFound,
                "正在寻找窗口");
            PublishSnapshot(now);
            return;
        }

        var hasCallSignal = discovery.CallWindow is not null || window.CallSignalCount > 0;
        if (!hasCallSignal)
        {
            SetState(ConnectionState.CallNotDetected, "已连接");
            PublishSnapshot(now);
            return;
        }

        SetState(ConnectionState.CallDetected, "正在寻找参与者");
        PublishSnapshot(now);

        ParticipantDetectionResult detection;
        try
        {
            detection = await participantDetector.DetectAsync(window, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            windowLocator.RequestFullScan();
            await logger.WriteAsync(
                DiagnosticLogLevel.Warning,
                "monitoring.participant.read.failed",
                "参与者读取失败，保持已连接/已检测到通话。",
                exception,
                CancellationToken.None).ConfigureAwait(false);
            SetState(ConnectionState.ParticipantReadingFailed, "已连接");
            PublishSnapshot(now);
            return;
        }

        if (detection.IsUiAutomationAccessDenied || !detection.IsParticipantListFound || detection.Participants.Count == 0)
        {
            windowLocator.RequestFullScan();
        }

        if (detection.IsUiAutomationAccessDenied)
        {
            await logger.WriteAsync(
                DiagnosticLogLevel.Warning,
                "monitoring.uia.access.denied",
                "找到窗口但 UIA 因权限不足无法读取；保持已连接。",
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            SetState(ConnectionState.ParticipantReadingFailed, "已连接");
            PublishSnapshot(now);
            return;
        }

        if (!detection.IsParticipantListFound)
        {
            await logger.WriteAsync(
                DiagnosticLogLevel.Information,
                "monitoring.participants.missing",
                "有通话但没有参与者名单。",
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            SetState(ConnectionState.ParticipantPanelNotFound, "正在寻找参与者");
            if (_currentCallId is not null &&
                _lastParticipantListSeenAt is DateTimeOffset listSeen &&
                now - listSeen >= TimeSpan.FromSeconds(settings.OfflineWaitSeconds) &&
                discovery.CallWindow is null &&
                window.CallSignalCount == 0)
            {
                await CompleteCurrentCallAsync("CallEnded", cancellationToken).ConfigureAwait(false);
            }

            PublishSnapshot(now);
            return;
        }

        if (detection.Participants.Count == 0)
        {
            // 参与者数量为 0 绝不能回退到未连接。
            SetState(ConnectionState.ParticipantReadingFailed, "正在读取参与者");
            PublishSnapshot(now);
            return;
        }

        _lastParticipantListSeenAt = now;
        SetState(ConnectionState.ParticipantPanelFound, "正在读取参与者");

        if (_currentCallId is null)
        {
            _currentCallStartTime = now;
            _currentCallId = await attendanceRepository.StartCallAsync(
                now,
                "Viber在线记录",
                window.HandleHex,
                cancellationToken).ConfigureAwait(false);
            _tracker = new ParticipantPresenceTracker(
                _currentCallId.Value,
                settings.OnlineConfirmationCount,
                TimeSpan.FromSeconds(settings.OfflineWaitSeconds),
                TimeSpan.FromSeconds(5));
            sheetSync?.Watch(_currentCallId.Value);
        }

        var resolved = new List<ResolvedParticipantObservation>(detection.Participants.Count);
        foreach (var observation in detection.Participants)
        {
            var identity = await attendanceRepository.ResolveMemberAsync(observation, cancellationToken)
                .ConfigureAwait(false);
            resolved.Add(new ResolvedParticipantObservation(
                identity.MemberId,
                identity.DisplayName,
                observation.Source,
                observation.Confidence,
                observation.ObservedAtUtc,
                observation.StableUiIdentity));
        }

        var update = _tracker!.Apply(resolved, now);
        await PersistUpdateAsync(update, cancellationToken).ConfigureAwait(false);
        _participants = update.Participants;
        if (update.ChangedParticipants.Any(participant =>
                participant.State is MemberPresenceState.Online or MemberPresenceState.Offline))
        {
            await PushSheetAsync(_currentCallId.Value, cancellationToken).ConfigureAwait(false);
        }

        SetState(ConnectionState.Monitoring, "正在监控");
        PublishSnapshot(now);
    }

    private void ApplyTemporaryFailure()
    {
        if (UserFacingStatuses.ImpliesViberRunning(_connectionState))
        {
            _connectionStatus = UserFacingStatuses.DetectionTemporarilyFailed;
            _connectionState = ConnectionState.DetectionTemporarilyFailed;
            return;
        }

        SetState(ConnectionState.DetectionTemporarilyFailed, _monitoringStatus);
    }

    private void SetState(ConnectionState state, string monitoringStatus)
    {
        _connectionState = state;
        _connectionStatus = UserFacingStatuses.GetConnectionStatus(state);
        _monitoringStatus = monitoringStatus;
    }

    private TimeSpan GetAdaptiveDelay(ConnectionState state) => state switch
    {
        ConnectionState.ViberNotRunning => TimeSpan.FromSeconds(3),
        ConnectionState.ViberProcessFound => TimeSpan.FromSeconds(2),
        ConnectionState.ViberWindowFound => TimeSpan.FromSeconds(2),
        ConnectionState.CallNotDetected => TimeSpan.FromSeconds(2),
        ConnectionState.DetectionTemporarilyFailed => TimeSpan.FromSeconds(2),
        ConnectionState.ParticipantReadingFailed => TimeSpan.FromSeconds(2),
        ConnectionState.CallDetected => TimeSpan.FromSeconds(2),
        ConnectionState.ParticipantPanelNotFound => TimeSpan.FromSeconds(2),
        ConnectionState.ParticipantPanelFound => TimeSpan.FromSeconds(2),
        ConnectionState.Monitoring => TimeSpan.FromSeconds(Math.Clamp(settings.FastScanSeconds, 1, 30)),
        _ => TimeSpan.FromSeconds(2)
    };

    private async Task CompleteCurrentCallAsync(string reason, CancellationToken cancellationToken)
    {
        if (_currentCallId is not Guid callId || _tracker is null)
        {
            return;
        }

        var now = clock.UtcNow;
        var update = _tracker.Stop(now);
        await PersistUpdateAsync(update, cancellationToken).ConfigureAwait(false);
        await attendanceRepository.EndCallAsync(callId, now, reason, cancellationToken).ConfigureAwait(false);
        await attendanceRepository.FlushAsync(cancellationToken).ConfigureAwait(false);
        await PushSheetAsync(callId, cancellationToken).ConfigureAwait(false);
        sheetSync?.Unwatch();
        _lastCompletedCallId = callId;
        _participants = update.Participants;
        _tracker = null;
        _currentCallId = null;
        _currentCallStartTime = null;
    }

    private async Task PushSheetAsync(Guid callId, CancellationToken cancellationToken)
    {
        if (sheetSync is null)
        {
            return;
        }

        try
        {
            await sheetSync.PushCallAsync(callId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await logger.WriteAsync(
                DiagnosticLogLevel.Warning,
                "sheet.sync.failed",
                "谷歌表格写入失败。",
                exception,
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task PersistUpdateAsync(PresenceTrackingUpdate update, CancellationToken cancellationToken)
    {
        foreach (var participant in update.ChangedParticipants.Concat(update.HeartbeatParticipants)
                     .DistinctBy(participant => participant.IntervalId))
        {
            await attendanceRepository.SaveIntervalAsync(participant, cancellationToken).ConfigureAwait(false);
        }
    }

    private void PublishSnapshot(DateTimeOffset now)
    {
        var rows = _participants
            .OrderBy(participant => participant.State == MemberPresenceState.Offline)
            .ThenBy(participant => participant.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenByDescending(participant => participant.OnlineAtUtc)
            .Select(participant => ToRow(participant, now))
            .ToArray();
        SnapshotChanged?.Invoke(this, new MonitoringUiSnapshot(
            _connectionState,
            _connectionStatus,
            _monitoringStatus,
            _currentCallStartTime,
            rows,
            _isDetecting));
    }

    private static ParticipantRowViewModel ToRow(ParticipantPresenceSnapshot participant, DateTimeOffset now)
    {
        var stateText = participant.State switch
        {
            MemberPresenceState.Online => "在线",
            MemberPresenceState.Offline => "已离线",
            _ => "确认中"
        };
        var currentDuration = participant.OnlineAtUtc is DateTimeOffset onlineAt
            ? (participant.OfflineAtUtc ?? now) - onlineAt
            : TimeSpan.Zero;
        return new ParticipantRowViewModel(
            participant.MemberId,
            participant.DisplayName,
            string.IsNullOrWhiteSpace(participant.StableUiIdentity) ? "—" : participant.StableUiIdentity,
            FormatLocalTime(participant.OnlineAtUtc),
            FormatLocalTime(participant.OfflineAtUtc),
            participant.State is MemberPresenceState.Online or MemberPresenceState.CandidateOffline,
            FormatDuration(currentDuration),
            FormatDuration(participant.TotalDuration),
            stateText);
    }

    private static string FormatLocalTime(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture) ?? "—";

    private static string FormatDuration(TimeSpan value)
    {
        var totalHours = (int)Math.Floor(Math.Max(0, value.TotalHours));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{totalHours:00}:{Math.Abs(value.Minutes):00}:{Math.Abs(value.Seconds):00}");
    }

    private static string EscapeCsv(string value) =>
        value.ContainsAny([',', '"', '\r', '\n'])
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    public async ValueTask DisposeAsync()
    {
        await StopAsync("ApplicationExit").ConfigureAwait(false);
        _lifecycleGate.Dispose();
        _scanGate.Dispose();
    }
}

internal static class StringSearchExtensions
{
    internal static bool ContainsAny(this string value, IEnumerable<char> characters) =>
        characters.Any(value.Contains);
}
