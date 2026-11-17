using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Models;
using ViberAttendance.Core.Sheets;

namespace ViberAttendance.Core.Contracts;

/// <summary>提供墙上时钟和不受系统时间跳变影响的单调时钟。</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
    long GetMonotonicTimestamp();
    TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp);
}

public enum DiagnosticLogLevel
{
    Information,
    Warning,
    Error
}

/// <summary>写入不包含完整成员姓名的本地诊断日志。</summary>
public interface IDiagnosticLogger
{
    Task WriteAsync(
        DiagnosticLogLevel level,
        string eventName,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default);
}

/// <summary>清除本机诊断日志，不影响 SQLite 考勤数据库。</summary>
public interface IDiagnosticLogMaintenance
{
    Task<int> ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>加载并原子保存本地应用设置。</summary>
public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>枚举当前系统中的 Viber 候选进程。</summary>
public interface IViberProcessLocator
{
    IReadOnlyList<ViberProcessInfo> FindCandidates(CancellationToken cancellationToken = default);
}

/// <summary>枚举、过滤并评分 Viber 进程拥有的顶层窗口。</summary>
public interface IViberWindowLocator
{
    Task<ViberWindowDiscoveryResult> FindAsync(CancellationToken cancellationToken = default);

    /// <summary>下一次 <see cref="FindAsync"/> 重新完整探测所有窗口，不复用上一轮结果。</summary>
    void RequestFullScan()
    {
    }
}

/// <summary>分层完整诊断：进程 → 窗口 → UIA → MSAA → 参与者。</summary>
public interface IFullDiagnosticRunner
{
    Task<FullDiagnosticReport> RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>采集指定窗口的原始 UI Automation 树。</summary>
public interface IUiAutomationTreeInspector
{
    Task<UiAutomationScanResult> CaptureAsync(
        ViberWindowCandidate window,
        UiAutomationCaptureOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>监听选定窗口的 UIA 结构与关键属性变化。</summary>
public interface IUiAutomationEventMonitor : IAsyncDisposable
{
    event EventHandler<UiAutomationEventArgs>? EventReceived;
    long EventCount { get; }
    Task StartAsync(ViberWindowCandidate window, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>将 UIA 层级同时导出为 JSON 和便于人工阅读的文本。</summary>
public interface IUiAutomationTreeExporter
{
    Task<UiAutomationExportResult> ExportAsync(
        UiAutomationScanResult scan,
        string jsonPath,
        bool includeSensitiveUiText,
        CancellationToken cancellationToken = default);
}

/// <summary>以事务方式初始化 SQLite 架构和索引。</summary>
public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>记录应用生命周期和心跳，并报告先前未正常关闭的会话。</summary>
public interface IAppSessionService : IAsyncDisposable
{
    IReadOnlyList<Guid> InterruptedSessionIds { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(string shutdownType, CancellationToken cancellationToken = default);
}

/// <summary>未来识别模块的统一入口；阶段 2 尚不假设 Viber 参与者控件。</summary>
public interface IParticipantDetector
{
    Task<ParticipantDetectionResult> DetectAsync(
        ViberWindowCandidate window,
        CancellationToken cancellationToken = default);
}

/// <summary>基于 UIA 的参与者识别边界；阶段 3 实现。</summary>
public interface IUiAutomationParticipantDetector : IParticipantDetector
{
}

/// <summary>完全本地视觉识别边界；仅在 UIA 不足时于阶段 5 实现。</summary>
public interface IVisionParticipantDetector : IParticipantDetector
{
}

/// <summary>把多源观察映射为稳定本地成员 ID。</summary>
public interface IParticipantIdentityResolver
{
    Task<ParticipantIdentity> ResolveAsync(ParticipantObservation observation, CancellationToken cancellationToken = default);
}

/// <summary>阶段 4 将实现的成员在线状态机边界。</summary>
public interface IAttendanceStateMachine
{
    Task<IReadOnlyList<AttendanceTransition>> ApplyAsync(
        IReadOnlyList<ParticipantObservation> observations,
        CancellationToken cancellationToken = default);
}

/// <summary>综合窗口与 UIA 信号判断通话是否存在。</summary>
public interface ICallSessionDetector
{
    Task<CallPresence> DetectAsync(ViberWindowCandidate window, CancellationToken cancellationToken = default);
}

/// <summary>考勤数据持久化边界。</summary>
public interface IAttendanceRepository
{
    Task<Guid> StartCallAsync(
        DateTimeOffset startedAtUtc,
        string title,
        string viberWindowIdentity,
        CancellationToken cancellationToken = default);
    Task<ParticipantIdentity> ResolveMemberAsync(
        ParticipantObservation observation,
        CancellationToken cancellationToken = default);
    Task SaveIntervalAsync(
        ParticipantPresenceSnapshot participant,
        CancellationToken cancellationToken = default);
    Task EndCallAsync(
        Guid callId,
        DateTimeOffset endedAtUtc,
        string reason,
        CancellationToken cancellationToken = default);
    /// <summary>把上次异常退出时仍为 Active 的通话和在线记录按最后看到时间结束，返回处理的通话数。</summary>
    Task<int> CloseInterruptedCallsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CallHistoryRecord>> GetCallsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AttendanceIntervalRecord>> GetCallIntervalsAsync(
        Guid callId,
        CancellationToken cancellationToken = default);
    Task DeleteCallAsync(Guid callId, CancellationToken cancellationToken = default);
    Task DeleteIntervalAsync(Guid intervalId, CancellationToken cancellationToken = default);
    Task<int> DeleteAllHistoryAsync(Guid? exceptCallId, CancellationToken cancellationToken = default);
    Task<HistoryArchive> ExportHistoryAsync(CancellationToken cancellationToken = default);
    Task<HistoryImportResult> ImportHistoryAsync(HistoryArchive archive, CancellationToken cancellationToken = default);
    Task FlushAsync(CancellationToken cancellationToken = default);
}

/// <summary>读写一张谷歌表格。调用方不得把链接或密钥写入日志。</summary>
public interface ISheetGridClient
{
    Task<IReadOnlyList<IReadOnlyList<string>>> ReadAsync(
        string spreadsheetUrl,
        string sheetName,
        CancellationToken cancellationToken = default);

    Task WriteAsync(
        string spreadsheetUrl,
        string sheetName,
        IReadOnlyList<SheetCell> cells,
        IReadOnlyList<SheetColor> colors,
        CancellationToken cancellationToken = default);
}

/// <summary>把一场通话的考勤写入已启用的谷歌表格。</summary>
public interface ISheetAttendanceSync
{
    Task PushCallAsync(Guid callId, CancellationToken cancellationToken = default);
    Task PushInterruptedAsync(CancellationToken cancellationToken = default);
    /// <summary>把本机全部历史写入实时明细表。返回给界面的说明不含链接。</summary>
    Task<string> PushHistoryAsync(CancellationToken cancellationToken = default);
    void Watch(Guid callId);
    void Unwatch();
}

/// <summary>阶段 6 的 CSV、XLSX、JSON 与 Markdown 业务导出边界。</summary>
public interface IExportService
{
    Task<AttendanceExportResult> ExportAsync(AttendanceExportRequest request, CancellationToken cancellationToken = default);
}
