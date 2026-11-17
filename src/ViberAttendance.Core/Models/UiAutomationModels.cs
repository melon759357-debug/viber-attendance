namespace ViberAttendance.Core.Models;

public sealed class UiAutomationNode
{
    public required string Path { get; init; }
    public required string RuntimeId { get; init; }
    public required string AutomationId { get; init; }
    public required string Name { get; init; }
    public required string ControlType { get; init; }
    public required string LocalizedControlType { get; init; }
    public required string ClassName { get; init; }
    public required string FrameworkId { get; init; }
    public required string HelpText { get; init; }
    public required string AccessKey { get; init; }
    public required string AcceleratorKey { get; init; }
    public int ProcessId { get; init; }
    public int NativeWindowHandle { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsOffscreen { get; init; }
    public bool IsKeyboardFocusable { get; init; }
    public bool HasKeyboardFocus { get; init; }
    public PixelRect? BoundingRectangle { get; init; }
    public IReadOnlyList<string> SupportedPatterns { get; init; } = [];
    public List<UiAutomationNode> Children { get; } = [];
}

public sealed record UiAutomationCaptureOptions(int MaxNodes, int MaxDepth);

public sealed record UiAutomationScanResult(
    DateTimeOffset CapturedAtUtc,
    ViberWindowCandidate Window,
    UiAutomationNode? Root,
    int NodeCount,
    bool WasTruncated,
    TimeSpan Duration,
    IReadOnlyList<string> Errors);

public sealed class UiAutomationEventArgs(
    DateTimeOffset eventTimeUtc,
    string eventType,
    string runtimeId,
    string detail) : EventArgs
{
    public DateTimeOffset EventTimeUtc { get; } = eventTimeUtc;
    public string EventType { get; } = eventType;
    public string RuntimeId { get; } = runtimeId;
    public string Detail { get; } = detail;
}

public sealed record UiAutomationExportResult(string JsonPath, string TextPath);
