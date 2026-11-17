namespace ViberAttendance.Core.Models;

public sealed record FullDiagnosticReport(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    TimeSpan TotalDuration,
    DiagnosticStageResult ProcessStage,
    DiagnosticStageResult WindowStage,
    DiagnosticStageResult UiaStage,
    DiagnosticStageResult MsaaStage,
    DiagnosticStageResult ParticipantStage,
    IReadOnlyList<ViberProcessInfo> DetectedProcesses,
    IReadOnlyList<DiagnosticWindowInfo> DetectedWindows,
    IReadOnlyList<DiagnosticWindowInfo> RejectedWindows,
    DiagnosticWindowInfo? SelectedWindow,
    IReadOnlyList<DiagnosticUiaWindowResult> UiaResults,
    IReadOnlyList<DiagnosticMsaaWindowResult> MsaaResults,
    DiagnosticParticipantStageResult ParticipantDetails,
    ConnectionState FinalConnectionState,
    ParticipantCaptureStrategy RecommendedStrategy,
    string RecommendationSummary,
    string? LastException);

public sealed record DiagnosticStageResult(
    string StageName,
    bool Succeeded,
    string Summary,
    string? Error = null);

public sealed record DiagnosticWindowInfo(
    long Hwnd,
    int ProcessId,
    string ProcessName,
    string Title,
    string ClassName,
    bool IsVisible,
    bool IsMinimized,
    bool IsCloaked,
    bool IsToolWindow,
    bool HasOwner,
    int Width,
    int Height,
    bool IsAccepted,
    string RejectionReason,
    ViberWindowCandidateType CandidateType,
    int Score,
    int CallSignalCount,
    int MainInterfaceSignalCount);

public sealed record DiagnosticUiaWindowResult(
    long Hwnd,
    bool RootSucceeded,
    string? RootName,
    string? RootClassName,
    string? RootAutomationId,
    string? RootControlType,
    int NativeWindowHandle,
    int ChildCount,
    int NodeCount,
    bool HasTextElements,
    bool HasListOrListItem,
    bool HasParticipantRelatedText,
    int TextElementCount,
    IReadOnlyList<DiagnosticTextElement> TextElements,
    IReadOnlyList<string> Errors,
    UiAutomationNode? RedactedTreeRoot);

public sealed record DiagnosticTextElement(
    string Path,
    string ControlType,
    string RedactedName,
    int NameLength,
    string AutomationId,
    bool LooksLikeParticipantName,
    string AcceptOrRejectReason);

public sealed record DiagnosticMsaaWindowResult(
    long Hwnd,
    bool Succeeded,
    string? RootName,
    string? RootRole,
    int ChildCount,
    int NamedChildCount,
    bool HasParticipantLikeNames,
    IReadOnlyList<DiagnosticTextElement> Names,
    string? Error);

public sealed record DiagnosticParticipantStageResult(
    long? SelectedWindowHwnd,
    string? SelectedContainerPath,
    string? SelectedContainerControlType,
    int CandidateNameCount,
    IReadOnlyList<DiagnosticTextElement> CandidateNames,
    bool PanelFound,
    bool NamesReadable,
    string Summary);

public static class FullDiagnosticFileNames
{
    public static string CreateJsonFileName(DateTimeOffset timestamp) =>
        $"viber-full-diagnostic-{timestamp:yyyyMMdd-HHmmss}.json";
}
