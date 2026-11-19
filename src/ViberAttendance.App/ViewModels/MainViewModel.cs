using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ViberAttendance.App.Commands;
using ViberAttendance.App.Services;
using ViberAttendance.Core.Attendance;
using ViberAttendance.Core.Models;

namespace ViberAttendance.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly MonitoringCoordinator _coordinator;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly DispatcherTimer _durationTimer;
    private readonly AsyncRelayCommand _startMonitoringCommand;
    private readonly AsyncRelayCommand _stopMonitoringCommand;
    private readonly AsyncRelayCommand _retryDetectionCommand;
    private Action? _openHistory;
    private DateTimeOffset? _currentCallStartedAt;
    private ConnectionState _connectionState = ConnectionState.ViberNotRunning;
    private string _connectionStatus = UserFacingStatuses.ViberNotRunning;
    private string _monitoringStatus = "未监控";
    private string _currentCallStartTime = "—";
    private string _currentCallDuration = "00:00:00";
    private string _viberConnectionText = "未连接";
    private string _statusBadgeText = "未连接";
    private string _statusTone = "Offline";
    private string _emptyStateTitle = UserFacingStatuses.ViberNotRunning;
    private string _emptyStateDescription = UserFacingStatuses.ViberNotRunningHint;
    private int _onlineParticipantCount;
    private bool _isMonitoring;
    private bool _isDetecting;
    private bool _disposed;

    public MainViewModel(
        MonitoringCoordinator coordinator,
        IConfirmationDialogService confirmationDialogService)
    {
        _coordinator = coordinator;
        _confirmationDialogService = confirmationDialogService;
        _coordinator.SnapshotChanged += OnSnapshotChanged;

        _startMonitoringCommand = new AsyncRelayCommand(
            () => _coordinator.StartAsync(),
            () => !IsMonitoring,
            OnCommandError);
        _stopMonitoringCommand = new AsyncRelayCommand(
            StopMonitoringAsync,
            () => IsMonitoring,
            OnCommandError);
        _retryDetectionCommand = new AsyncRelayCommand(
            () => _coordinator.RetryDetectionAsync(),
            () => !IsDetecting,
            OnCommandError);
        StartMonitoringCommand = _startMonitoringCommand;
        StopMonitoringCommand = _stopMonitoringCommand;
        RetryDetectionCommand = _retryDetectionCommand;
        ExportCommand = new AsyncRelayCommand(ExportAsync, onError: OnCommandError);
        OpenHistoryCommand = new RelayCommand(() => _openHistory?.Invoke());

        _durationTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _durationTimer.Tick += OnDurationTimerTick;
        _durationTimer.Start();
    }

    public ConnectionState ConnectionState
    {
        get => _connectionState;
        private set => SetProperty(ref _connectionState, value);
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetProperty(ref _connectionStatus, value);
    }

    public string MonitoringStatus
    {
        get => _monitoringStatus;
        private set => SetProperty(ref _monitoringStatus, value);
    }

    public string CurrentCallStartTime
    {
        get => _currentCallStartTime;
        private set => SetProperty(ref _currentCallStartTime, value);
    }

    public string CurrentCallDuration
    {
        get => _currentCallDuration;
        private set => SetProperty(ref _currentCallDuration, value);
    }

    public string ViberConnectionText
    {
        get => _viberConnectionText;
        private set => SetProperty(ref _viberConnectionText, value);
    }

    public string StatusBadgeText
    {
        get => _statusBadgeText;
        private set => SetProperty(ref _statusBadgeText, value);
    }

    public string StatusTone
    {
        get => _statusTone;
        private set => SetProperty(ref _statusTone, value);
    }

    public string EmptyStateTitle
    {
        get => _emptyStateTitle;
        private set => SetProperty(ref _emptyStateTitle, value);
    }

    public string EmptyStateDescription
    {
        get => _emptyStateDescription;
        private set => SetProperty(ref _emptyStateDescription, value);
    }

    public int OnlineParticipantCount
    {
        get => _onlineParticipantCount;
        private set => SetProperty(ref _onlineParticipantCount, value);
    }

    public bool IsMonitoring
    {
        get => _isMonitoring;
        private set
        {
            if (!SetProperty(ref _isMonitoring, value))
            {
                return;
            }

            RefreshCommandStates();
        }
    }

    public bool IsDetecting
    {
        get => _isDetecting;
        private set
        {
            if (!SetProperty(ref _isDetecting, value))
            {
                return;
            }

            RefreshCommandStates();
        }
    }

    public ObservableCollection<ParticipantRowViewModel> Participants { get; } = [];

    public void RefreshDisplayedNumbers() => _coordinator.Republish();
    public ICommand StartMonitoringCommand { get; }
    public ICommand StopMonitoringCommand { get; }
    public ICommand RetryDetectionCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand OpenHistoryCommand { get; }

    /// <summary>
    /// 连接检测始终启动；autoStart 仅保留兼容（考勤记录仍按检测到参与者后自动开始）。
    /// 修复：关闭 AutoMonitor 时界面永远停在“尚未打开Viber”。
    /// </summary>
    internal Task InitializeAsync(bool autoStart, CancellationToken cancellationToken = default)
    {
        _ = autoStart;
        return _coordinator.StartAsync(cancellationToken);
    }

    internal void SetOpenHistoryAction(Action action) => _openHistory = action;

    private async Task StopMonitoringAsync()
    {
        var confirmed = await _confirmationDialogService.ConfirmAsync(
            "停止监控？",
            "停止后会保存本场记录。之后仍可再次开始监控。",
            "停止并保存").ConfigureAwait(true);
        if (confirmed)
        {
            await _coordinator.StopAsync().ConfigureAwait(true);
        }
    }

    private async Task ExportAsync()
    {
        var path = await _coordinator.ExportCurrentAsync().ConfigureAwait(true);
        ConnectionStatus = path is null ? "当前没有可导出的记录" : "本场记录已保存";
        UpdateDerivedStatus();
    }

    private void OnSnapshotChanged(object? sender, MonitoringUiSnapshot snapshot)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            ApplySnapshot(snapshot);
            return;
        }

        _ = dispatcher.BeginInvoke(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(MonitoringUiSnapshot snapshot)
    {
        ConnectionState = snapshot.ConnectionState;
        ConnectionStatus = snapshot.ConnectionStatus;
        MonitoringStatus = snapshot.MonitoringStatus;
        _currentCallStartedAt = snapshot.CurrentCallStartTime;
        CurrentCallStartTime = _currentCallStartedAt?
            .ToLocalTime()
            .ToString("HH:mm:ss", CultureInfo.CurrentCulture) ?? "—";
        UpdateDuration(DateTimeOffset.UtcNow);
        OnlineParticipantCount = snapshot.Participants.Count(participant => participant.IsOnline);
        IsDetecting = snapshot.IsDetecting;
        IsMonitoring = snapshot.MonitoringStatus is "正在监控" or "正在寻找 Viber" or "正在寻找参与者" or "已连接"
            || _coordinator.IsLoopRunning;
        SynchronizeParticipants(snapshot.Participants);
        UpdateDerivedStatus();
        RefreshCommandStates();
    }

    private void SynchronizeParticipants(IReadOnlyList<ParticipantRowViewModel> incoming)
    {
        for (var index = 0; index < incoming.Count; index++)
        {
            var row = incoming[index];
            if (index < Participants.Count && SameRow(Participants[index], row))
            {
                if (Participants[index] != row)
                {
                    Participants[index] = row;
                }

                continue;
            }

            var existingIndex = -1;
            for (var candidateIndex = index + 1; candidateIndex < Participants.Count; candidateIndex++)
            {
                if (SameRow(Participants[candidateIndex], row))
                {
                    existingIndex = candidateIndex;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                Participants.Move(existingIndex, index);
                if (Participants[index] != row)
                {
                    Participants[index] = row;
                }
            }
            else
            {
                Participants.Insert(index, row);
            }
        }

        while (Participants.Count > incoming.Count)
        {
            Participants.RemoveAt(Participants.Count - 1);
        }
    }

    private static bool SameRow(ParticipantRowViewModel left, ParticipantRowViewModel right) =>
        left.MemberId == right.MemberId && left.OnlineAt == right.OnlineAt;

    private void UpdateDerivedStatus()
    {
        EmptyStateTitle = UserFacingStatuses.GetEmptyStateTitle(ConnectionState);
        EmptyStateDescription = UserFacingStatuses.GetEmptyStateDescription(ConnectionState);

        switch (ConnectionState)
        {
            case ConnectionState.ViberNotRunning:
                ViberConnectionText = "未连接";
                StatusBadgeText = "尚未打开Viber";
                StatusTone = "Offline";
                break;
            case ConnectionState.ViberProcessFound:
                ViberConnectionText = "已运行";
                StatusBadgeText = "Viber已运行";
                StatusTone = "Warning";
                break;
            case ConnectionState.ViberWindowFound:
                ViberConnectionText = "已连接";
                StatusBadgeText = "已找到窗口";
                StatusTone = "Online";
                break;
            case ConnectionState.CallNotDetected:
                ViberConnectionText = "已连接";
                StatusBadgeText = "等待通话";
                StatusTone = "Online";
                break;
            case ConnectionState.CallDetected:
                ViberConnectionText = "已连接";
                StatusBadgeText = "已检测到通话";
                StatusTone = "Warning";
                break;
            case ConnectionState.ParticipantPanelNotFound:
                ViberConnectionText = "已连接";
                StatusBadgeText = "请展开名单";
                StatusTone = "Warning";
                break;
            case ConnectionState.ParticipantPanelFound:
                ViberConnectionText = "已连接";
                StatusBadgeText = "正在读取参与者";
                StatusTone = "Warning";
                break;
            case ConnectionState.ParticipantReadingFailed:
                ViberConnectionText = "已连接";
                StatusBadgeText = "读取成员失败";
                StatusTone = "Error";
                break;
            case ConnectionState.Monitoring:
                ViberConnectionText = "已连接";
                StatusBadgeText = OnlineParticipantCount > 0 ? $"当前有{OnlineParticipantCount}人在线" : "正在监控";
                StatusTone = "Online";
                break;
            case ConnectionState.DetectionTemporarilyFailed:
                ViberConnectionText = UserFacingStatuses.ImpliesViberRunning(ConnectionState) ? "已连接" : "重试中";
                StatusBadgeText = "识别暂时失败";
                StatusTone = "Error";
                break;
            default:
                ViberConnectionText = "未连接";
                StatusBadgeText = "尚未打开Viber";
                StatusTone = "Offline";
                break;
        }
    }

    private void RefreshCommandStates()
    {
        _startMonitoringCommand.RaiseCanExecuteChanged();
        _stopMonitoringCommand.RaiseCanExecuteChanged();
        _retryDetectionCommand.RaiseCanExecuteChanged();
    }

    private void OnCommandError(Exception exception)
    {
        System.Diagnostics.Debug.WriteLine($"命令执行失败: {exception}");
        ConnectionState = ConnectionState.DetectionTemporarilyFailed;
        ConnectionStatus = UserFacingStatuses.DetectionTemporarilyFailed;
        UpdateDerivedStatus();
    }

    private void OnDurationTimerTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        UpdateDuration(now);
        _coordinator.Republish();
    }

    private void UpdateDuration(DateTimeOffset now)
    {
        CurrentCallDuration = _currentCallStartedAt is DateTimeOffset startedAt
            ? DurationText.Format(now - startedAt)
            : DurationText.Format(TimeSpan.Zero);
    }

    private static string FormatDuration(TimeSpan value) => DurationText.Format(value);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _durationTimer.Stop();
        _durationTimer.Tick -= OnDurationTimerTick;
        _coordinator.SnapshotChanged -= OnSnapshotChanged;
        GC.SuppressFinalize(this);
    }
}
