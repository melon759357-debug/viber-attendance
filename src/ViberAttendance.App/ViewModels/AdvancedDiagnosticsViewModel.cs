using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Input;
using ViberAttendance.App.Commands;
using ViberAttendance.App.Services;
using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Contracts;
using ViberAttendance.Core.Models;
using ViberAttendance.Infrastructure;
using ViberAttendance.Vision;

namespace ViberAttendance.App.ViewModels;

public sealed class AdvancedDiagnosticsViewModel : ObservableObject, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppSettings _settings;
    private readonly ApplicationPaths _paths;
    private readonly IViberWindowLocator _windowLocator;
    private readonly IFullDiagnosticRunner _fullDiagnosticRunner;
    private readonly IUiAutomationTreeInspector _treeInspector;
    private readonly IUiAutomationTreeExporter _treeExporter;
    private readonly IUiAutomationEventMonitor _eventMonitor;
    private readonly IFileDialogService _fileDialogService;
    private readonly IDiagnosticLogMaintenance _logMaintenance;
    private readonly MonitoringCoordinator _monitoringCoordinator;
    private ViberWindowDiscoveryResult? _discovery;
    private FullDiagnosticReport? _fullReport;
    private UiAutomationScanResult? _lastScan;
    private ViberWindowCandidate? _selectedWindow;
    private string _statusMessage = "点击“运行完整诊断”确认故障落在进程/窗口/UIA/参与者哪一层。";
    private string _processStatus = "未检测";
    private string _connectionStateText = ConnectionState.ViberNotRunning.ToString();
    private string _callStateText = "未知";
    private string _detectionTimeText = "—";
    private string _detectionDurationText = "—";
    private string _selectedProcessText = "—";
    private string _selectedWindowText = "—";
    private string _strategyText = "—";
    private string _stage1Text = "—";
    private string _stage2Text = "—";
    private string _stage3Text = "—";
    private string _stage3bText = "—";
    private string _stage4Text = "—";
    private int _totalHandleCount;
    private int _candidateCount;
    private int _processCount;
    private int _nodeCount;
    private string _lastError = "无";
    private string _lastExportPath = "—";
    private bool _isDetecting;
    private bool _disposed;

    public AdvancedDiagnosticsViewModel(
        AppSettings settings,
        ApplicationPaths paths,
        IViberWindowLocator windowLocator,
        IFullDiagnosticRunner fullDiagnosticRunner,
        IUiAutomationTreeInspector treeInspector,
        IUiAutomationTreeExporter treeExporter,
        IUiAutomationEventMonitor eventMonitor,
        IFileDialogService fileDialogService,
        IDiagnosticLogMaintenance logMaintenance,
        MonitoringCoordinator monitoringCoordinator)
    {
        _settings = settings;
        _paths = paths;
        _windowLocator = windowLocator;
        _fullDiagnosticRunner = fullDiagnosticRunner;
        _treeInspector = treeInspector;
        _treeExporter = treeExporter;
        _eventMonitor = eventMonitor;
        _fileDialogService = fileDialogService;
        _logMaintenance = logMaintenance;
        _monitoringCoordinator = monitoringCoordinator;
        RunFullDiagnosticCommand = new AsyncRelayCommand(RunFullDiagnosticAsync, () => !IsDetecting);
        RetryDetectionCommand = RunFullDiagnosticCommand;
        FindWindowsCommand = new AsyncRelayCommand(FindWindowsAsync, () => !IsDetecting);
        CaptureTreeCommand = new AsyncRelayCommand(CaptureTreeAsync);
        ExportTreeCommand = new AsyncRelayCommand(ExportTreeAsync);
        CopyWindowDiagnosticsCommand = new RelayCommand(CopyWindowDiagnostics);
        ExportWindowDiagnosticsCommand = new AsyncRelayCommand(ExportWindowDiagnosticsAsync);
        ExportFullDiagnosticCommand = new AsyncRelayCommand(ExportFullDiagnosticAsync);
        OpenLogsFolderCommand = new RelayCommand(OpenLogsFolder);
        ClearLogsCommand = new AsyncRelayCommand(ClearLogsAsync);
    }

    public ObservableCollection<ViberProcessInfo> CandidateProcesses { get; } = [];
    public ObservableCollection<ViberWindowCandidate> WindowDiagnostics { get; } = [];
    public ObservableCollection<ViberWindowCandidate> CandidateWindows { get; } = [];
    public ObservableCollection<string> StageLines { get; } = [];
    public ObservableCollection<DiagnosticTreeNodeViewModel> RootNodes { get; } = [];

    public ViberWindowCandidate? SelectedWindow
    {
        get => _selectedWindow;
        set => SetProperty(ref _selectedWindow, value);
    }

    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string ProcessStatus { get => _processStatus; private set => SetProperty(ref _processStatus, value); }
    public string ConnectionStateText { get => _connectionStateText; private set => SetProperty(ref _connectionStateText, value); }
    public string CallStateText { get => _callStateText; private set => SetProperty(ref _callStateText, value); }
    public string DetectionTimeText { get => _detectionTimeText; private set => SetProperty(ref _detectionTimeText, value); }
    public string DetectionDurationText { get => _detectionDurationText; private set => SetProperty(ref _detectionDurationText, value); }
    public string SelectedProcessText { get => _selectedProcessText; private set => SetProperty(ref _selectedProcessText, value); }
    public string SelectedWindowText { get => _selectedWindowText; private set => SetProperty(ref _selectedWindowText, value); }
    public string StrategyText { get => _strategyText; private set => SetProperty(ref _strategyText, value); }
    public string Stage1Text { get => _stage1Text; private set => SetProperty(ref _stage1Text, value); }
    public string Stage2Text { get => _stage2Text; private set => SetProperty(ref _stage2Text, value); }
    public string Stage3Text { get => _stage3Text; private set => SetProperty(ref _stage3Text, value); }
    public string Stage3bText { get => _stage3bText; private set => SetProperty(ref _stage3bText, value); }
    public string Stage4Text { get => _stage4Text; private set => SetProperty(ref _stage4Text, value); }
    public int TotalHandleCount { get => _totalHandleCount; private set => SetProperty(ref _totalHandleCount, value); }
    public int CandidateCount { get => _candidateCount; private set => SetProperty(ref _candidateCount, value); }
    public int ProcessCount { get => _processCount; private set => SetProperty(ref _processCount, value); }
    public int NodeCount { get => _nodeCount; private set => SetProperty(ref _nodeCount, value); }
    public string LastError { get => _lastError; private set => SetProperty(ref _lastError, value); }
    public string LastExportPath { get => _lastExportPath; private set => SetProperty(ref _lastExportPath, value); }

    public bool IsDetecting
    {
        get => _isDetecting;
        private set
        {
            if (!SetProperty(ref _isDetecting, value))
            {
                return;
            }

            if (RunFullDiagnosticCommand is AsyncRelayCommand full)
            {
                full.RaiseCanExecuteChanged();
            }

            if (FindWindowsCommand is AsyncRelayCommand find)
            {
                find.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand RunFullDiagnosticCommand { get; }
    public ICommand RetryDetectionCommand { get; }
    public ICommand FindWindowsCommand { get; }
    public ICommand CaptureTreeCommand { get; }
    public ICommand ExportTreeCommand { get; }
    public ICommand CopyWindowDiagnosticsCommand { get; }
    public ICommand ExportWindowDiagnosticsCommand { get; }
    public ICommand ExportFullDiagnosticCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }
    public ICommand ClearLogsCommand { get; }

    internal Task InitializeAsync() => Task.CompletedTask;

    private async Task RunFullDiagnosticAsync()
    {
        try
        {
            IsDetecting = true;
            StatusMessage = "正在运行完整诊断（进程 → 窗口 → UIA → MSAA → 参与者）…";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            _fullReport = await _fullDiagnosticRunner.RunAsync(timeout.Token).ConfigureAwait(true);
            ApplyFullReport(_fullReport);
            StatusMessage = _fullReport.RecommendationSummary;
        }
        catch (OperationCanceledException)
        {
            LastError = "完整诊断超时";
            StatusMessage = "完整诊断超时，请重试。";
            ConnectionStateText = ConnectionState.DetectionTemporarilyFailed.ToString();
        }
        catch (Exception exception)
        {
            LastError = DiagnosticText.Sanitize(exception.GetType().Name + ": " + exception.Message);
            StatusMessage = "完整诊断失败，请查看本地日志。";
            ConnectionStateText = ConnectionState.DetectionTemporarilyFailed.ToString();
        }
        finally
        {
            IsDetecting = false;
        }
    }

    private void ApplyFullReport(FullDiagnosticReport report)
    {
        CandidateProcesses.Clear();
        foreach (var process in report.DetectedProcesses)
        {
            CandidateProcesses.Add(process);
        }

        WindowDiagnostics.Clear();
        CandidateWindows.Clear();
        foreach (var window in report.DetectedWindows)
        {
            var candidate = ToCandidate(window);
            WindowDiagnostics.Add(candidate);
            if (window.IsAccepted)
            {
                CandidateWindows.Add(candidate);
            }
        }

        SelectedWindow = CandidateWindows.FirstOrDefault(window =>
            window.Hwnd == report.SelectedWindow?.Hwnd) ?? CandidateWindows.FirstOrDefault();

        ProcessCount = report.DetectedProcesses.Count;
        ProcessStatus = report.ProcessStage.Succeeded ? "已找到进程" : "未找到进程";
        TotalHandleCount = report.DetectedWindows.Count;
        CandidateCount = report.DetectedWindows.Count(window => window.IsAccepted);
        DetectionTimeText = report.FinishedAtUtc.ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
        DetectionDurationText = $"{report.TotalDuration.TotalMilliseconds:0} ms";
        SelectedProcessText = report.DetectedProcesses.FirstOrDefault()?.DisplayLabel ?? "—";
        SelectedWindowText = report.SelectedWindow is null
            ? "—"
            : $"0x{report.SelectedWindow.Hwnd:X} · {report.SelectedWindow.ClassName} · {report.SelectedWindow.Title}";
        ConnectionStateText = report.FinalConnectionState.ToString();
        CallStateText = report.FinalConnectionState is ConnectionState.CallDetected
            or ConnectionState.ParticipantPanelNotFound
            or ConnectionState.ParticipantPanelFound
            or ConnectionState.ParticipantReadingFailed
            or ConnectionState.Monitoring
            ? "已检测到通话信号"
            : "无通话";
        StrategyText = $"{report.RecommendedStrategy}: {VisionFeatureStatus.Describe(report.RecommendedStrategy)}";
        Stage1Text = FormatStage(report.ProcessStage);
        Stage2Text = FormatStage(report.WindowStage);
        Stage3Text = FormatStage(report.UiaStage);
        Stage3bText = FormatStage(report.MsaaStage);
        Stage4Text = FormatStage(report.ParticipantStage);
        LastError = string.IsNullOrWhiteSpace(report.LastException) ? "无" : report.LastException!;

        StageLines.Clear();
        StageLines.Add(Stage1Text);
        StageLines.Add(Stage2Text);
        StageLines.Add(Stage3Text);
        StageLines.Add(Stage3bText);
        StageLines.Add(Stage4Text);
        StageLines.Add("建议方案：" + StrategyText);
        StageLines.Add("最终连接状态：" + ConnectionStateText);
        StageLines.Add("监控器当前状态：" + _monitoringCoordinator.ConnectionState);
    }

    private async Task FindWindowsAsync()
    {
        try
        {
            IsDetecting = true;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            _windowLocator.RequestFullScan();
            _discovery = await _windowLocator.FindAsync(timeout.Token).ConfigureAwait(true);
            CandidateProcesses.Clear();
            foreach (var process in _discovery.Processes)
            {
                CandidateProcesses.Add(process);
            }

            WindowDiagnostics.Clear();
            foreach (var window in _discovery.AllWindows)
            {
                WindowDiagnostics.Add(window);
            }

            CandidateWindows.Clear();
            foreach (var window in _discovery.Candidates)
            {
                CandidateWindows.Add(window);
            }

            SelectedWindow = _discovery.SelectedWindow;
            ProcessCount = _discovery.ProcessCount;
            ProcessStatus = _discovery.IsViberConnected ? "已找到进程" : "未找到进程";
            TotalHandleCount = _discovery.TotalTopLevelHandleCount;
            CandidateCount = _discovery.Candidates.Count;
            DetectionTimeText = (_discovery.DiscoveredAtUtc ?? DateTimeOffset.UtcNow)
                .ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
            DetectionDurationText = $"{_discovery.DiscoveryDuration.TotalMilliseconds:0} ms";
            SelectedProcessText = _discovery.SelectedProcess?.DisplayLabel ?? "—";
            SelectedWindowText = SelectedWindow?.DisplayLabel ?? "—";
            ConnectionStateText = _discovery.ProcessCount == 0
                ? ConnectionState.ViberNotRunning.ToString()
                : _discovery.HasDiscoveredWindow
                    ? ConnectionState.CallNotDetected.ToString()
                    : ConnectionState.ViberProcessFound.ToString();
            StatusMessage = _discovery.ProcessCount == 0
                ? "没找到进程。"
                : _discovery.HasDiscoveredWindow
                    ? "找到进程和窗口。请再运行完整诊断查看 UIA/MSAA。"
                    : "找到进程但没找到窗口。";
        }
        catch (Exception exception)
        {
            LastError = DiagnosticText.Sanitize(exception.GetType().Name + ": " + exception.Message);
            StatusMessage = "窗口扫描失败。";
        }
        finally
        {
            IsDetecting = false;
        }
    }

    private async Task CaptureTreeAsync()
    {
        if (SelectedWindow is null)
        {
            StatusMessage = "没有可采集的候选窗口。";
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.UiaScanTimeoutSeconds));
        _lastScan = await _treeInspector.CaptureAsync(
            SelectedWindow,
            new UiAutomationCaptureOptions(_settings.UiaMaxNodes, _settings.UiaMaxDepth),
            timeout.Token).ConfigureAwait(true);
        RootNodes.Clear();
        if (_lastScan.Root is not null)
        {
            RootNodes.Add(new DiagnosticTreeNodeViewModel(_lastScan.Root));
        }

        NodeCount = _lastScan.NodeCount;
        LastError = _lastScan.Errors.Count > 0 ? _lastScan.Errors[^1] : "无";
        StatusMessage = $"UIA树采集完成：{NodeCount}个节点。";
    }

    private async Task ExportTreeAsync()
    {
        if (_lastScan?.Root is null)
        {
            StatusMessage = "请先采集UIA树。";
            return;
        }

        var path = _fileDialogService.ChooseUiAutomationJsonPath(_settings.ExportFolder);
        if (path is null)
        {
            return;
        }

        var result = await _treeExporter.ExportAsync(
            _lastScan,
            path,
            _settings.IncludeSensitiveUiTextInExport).ConfigureAwait(true);
        LastExportPath = result.JsonPath;
        StatusMessage = $"已导出：{result.JsonPath}";
    }

    private void CopyWindowDiagnostics()
    {
        var builder = new StringBuilder();
        foreach (var line in StageLines)
        {
            builder.AppendLine(line);
        }

        builder.AppendLine();
        builder.AppendLine(BuildDiagnosticsText());
        Clipboard.SetText(builder.ToString());
        StatusMessage = "诊断信息已复制。";
    }

    private async Task ExportWindowDiagnosticsAsync()
    {
        if (_discovery is null && _fullReport is null)
        {
            await FindWindowsAsync().ConfigureAwait(true);
        }

        var path = _fileDialogService.ChooseWindowDiagnosticsJsonPath(_settings.ExportFolder);
        if (path is null)
        {
            return;
        }

        object payload = _fullReport is not null
            ? CreateExportPayload(_fullReport)
            : _discovery!;
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false)).ConfigureAwait(true);
        LastExportPath = path;
        StatusMessage = $"诊断 JSON 已导出：{path}";
    }

    private async Task ExportFullDiagnosticAsync()
    {
        if (_fullReport is null)
        {
            await RunFullDiagnosticAsync().ConfigureAwait(true);
        }

        if (_fullReport is null)
        {
            StatusMessage = "没有可导出的完整诊断。";
            return;
        }

        var path = _fileDialogService.ChooseFullDiagnosticJsonPath(_settings.ExportFolder);
        if (path is null)
        {
            // 即使用户取消对话框，也写入默认导出目录，便于本机核对。
            path = Path.Combine(
                _settings.ExportFolder,
                FullDiagnosticFileNames.CreateJsonFileName(DateTimeOffset.Now));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(CreateExportPayload(_fullReport), JsonOptions);
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false)).ConfigureAwait(true);
        LastExportPath = path;
        StatusMessage = $"完整诊断 JSON 已导出：{path}";
    }

    private void OpenLogsFolder()
    {
        Directory.CreateDirectory(_paths.LogsFolder);
        Process.Start(new ProcessStartInfo
        {
            FileName = _paths.LogsFolder,
            UseShellExecute = true
        });
        StatusMessage = $"已打开日志文件夹：{_paths.LogsFolder}";
    }

    private async Task ClearLogsAsync()
    {
        var count = await _logMaintenance.ClearAsync().ConfigureAwait(true);
        StatusMessage = $"已清除{count}个诊断日志文件。";
    }

    private static object CreateExportPayload(FullDiagnosticReport report) => new
    {
        timestamps = new
        {
            startedAtUtc = report.StartedAtUtc,
            finishedAtUtc = report.FinishedAtUtc,
            totalDurationMs = report.TotalDuration.TotalMilliseconds
        },
        finalConnectionState = report.FinalConnectionState.ToString(),
        recommendedStrategy = report.RecommendedStrategy.ToString(),
        recommendationSummary = report.RecommendationSummary,
        lastException = report.LastException,
        stages = new
        {
            process = report.ProcessStage,
            window = report.WindowStage,
            uia = report.UiaStage,
            msaa = report.MsaaStage,
            participant = report.ParticipantStage
        },
        detectedProcesses = report.DetectedProcesses,
        detectedWindows = report.DetectedWindows,
        rejectedWindows = report.RejectedWindows,
        selectedWindow = report.SelectedWindow,
        uiaRootInformation = report.UiaResults.Select(result => new
        {
            result.Hwnd,
            result.RootSucceeded,
            result.RootName,
            result.RootClassName,
            result.RootAutomationId,
            result.RootControlType,
            result.NativeWindowHandle,
            result.ChildCount,
            result.NodeCount,
            result.HasTextElements,
            result.HasListOrListItem,
            result.HasParticipantRelatedText
        }),
        uiaTree = report.UiaResults.Select(result => result.RedactedTreeRoot),
        detectedTextElements = report.UiaResults.SelectMany(result => result.TextElements),
        msaaResults = report.MsaaResults,
        participantCandidates = report.ParticipantDetails
    };

    private string BuildDiagnosticsText()
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"DetectionTime\t{DetectionTimeText}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Duration\t{DetectionDurationText}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"ConnectionState\t{ConnectionStateText}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Strategy\t{StrategyText}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"LastError\t{LastError}");
        builder.AppendLine();
        builder.AppendLine("ProcessName\tPID\tMainWindowHandle\tMainWindowTitle\tExecutablePath");
        foreach (var process in CandidateProcesses)
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"{Sanitize(process.ProcessName)}\t{process.ProcessId}\t0x{process.MainWindowHandle:X}\t" +
                $"{Sanitize(process.MainWindowTitle)}\t{Sanitize(process.ExecutablePath ?? "")}");
        }

        builder.AppendLine();
        builder.AppendLine("Hwnd\tTitle\tClass\tVisible\tMinimized\tAccepted\tReason");
        foreach (var window in WindowDiagnostics)
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"{window.HandleHex}\t{Sanitize(window.WindowTitle)}\t{Sanitize(window.ClassName)}\t" +
                $"{window.IsVisible}\t{window.IsMinimized}\t{window.IsAccepted}\t{window.RejectionReason}");
        }

        return builder.ToString();
    }

    private static string FormatStage(DiagnosticStageResult stage) =>
        $"{stage.StageName}：{(stage.Succeeded ? "成功" : "失败")} — {stage.Summary}" +
        (string.IsNullOrWhiteSpace(stage.Error) ? string.Empty : $"（{stage.Error}）");

    private static ViberWindowCandidate ToCandidate(DiagnosticWindowInfo window) =>
        new(
            window.Hwnd,
            window.ProcessId,
            window.ProcessName,
            window.Title,
            window.ClassName,
            true,
            window.IsVisible,
            window.IsCloaked,
            window.IsToolWindow,
            false,
            window.HasOwner,
            window.IsMinimized,
            window.Width,
            window.Height,
            new PixelRect(0, 0, window.Width, window.Height),
            window.Width,
            window.Height,
            true,
            window.Title,
            window.ClassName,
            string.Empty,
            unchecked((int)window.Hwnd),
            0,
            window.MainInterfaceSignalCount,
            window.CallSignalCount,
            false,
            window.CandidateType,
            window.Score,
            window.IsAccepted ? ViberWindowRejectionReasons.Accepted : window.RejectionReason);

    private static string Sanitize(string value) =>
        value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = _eventMonitor.StopAsync();
        GC.SuppressFinalize(this);
    }
}
