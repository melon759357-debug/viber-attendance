using System.Text.Json.Serialization;

namespace ViberAttendance.Core.Models;

public sealed record PixelRect(double X, double Y, double Width, double Height)
{
    public override string ToString() => $"{X:0.##},{Y:0.##} {Width:0.##}x{Height:0.##}";
}

public enum ViberWindowCandidateType
{
    MainWindow,
    CallWindow,
    NotificationWindow,
    AuxiliaryWindow,
    Unknown
}

public static class ViberWindowRejectionReasons
{
    public const string Accepted = "Accepted";
    public const string DuplicateHwnd = "DuplicateHwnd";
    public const string DuplicateNativeWindowHandle = "DuplicateNativeWindowHandle";
    public const string DuplicateSignature = "DuplicateSignature";
    public const string InvalidWindow = "InvalidWindow";
    public const string Hidden = "Hidden";
    public const string ChildWindow = "ChildWindow";
    public const string ToolWindow = "ToolWindow";
    public const string Cloaked = "Cloaked";
    public const string ZeroSize = "ZeroSize";
    public const string TooSmall = "TooSmall";
    public const string NoClientArea = "NoClientArea";
    public const string OffDesktop = "OffDesktop";
    public const string NotificationPopup = "NotificationPopup";
    public const string AuxiliaryWindow = "AuxiliaryWindow";
    public const string InternalRenderingWindow = "InternalRenderingWindow";
    public const string NoUsableUiaRoot = "NoUsableUiaRoot";
}

public sealed record ViberWindowCandidate(
    long Hwnd,
    int ProcessId,
    string ProcessName,
    string WindowTitle,
    string ClassName,
    bool IsWindow,
    bool IsVisible,
    bool IsCloaked,
    bool IsToolWindow,
    bool IsChildWindow,
    bool HasOwner,
    bool IsMinimized,
    int Width,
    int Height,
    PixelRect Bounds,
    int ClientWidth,
    int ClientHeight,
    bool IntersectsDesktop,
    string UiaName,
    string UiaClassName,
    string UiaControlType,
    int UiaNativeWindowHandle,
    int UiaVisitedNodeCount,
    int MainInterfaceSignalCount,
    int CallSignalCount,
    bool IsForeground,
    ViberWindowCandidateType CandidateType,
    int Score,
    string RejectionReason)
{
    // Compatibility aliases used by the existing UIA capture/monitoring code.
    [JsonIgnore]
    public long WindowHandle => Hwnd;

    [JsonIgnore]
    public string Title => WindowTitle;

    public string HandleHex => $"0x{Hwnd:X}";

    public bool IsAccepted => RejectionReason == ViberWindowRejectionReasons.Accepted;

    public string DisplayLabel
    {
        get
        {
            var title = string.IsNullOrWhiteSpace(WindowTitle) ? "（无标题）" : WindowTitle;
            return $"{CandidateType} · {Score}分 · PID {ProcessId} · {HandleHex} · {ClassName} · {title}";
        }
    }

    public ViberWindowCandidate(
        int processId,
        string processName,
        long windowHandle,
        string title,
        string className,
        PixelRect bounds,
        bool isVisible,
        bool isMinimized)
        : this(
            windowHandle,
            processId,
            processName,
            title,
            className,
            true,
            isVisible,
            false,
            false,
            false,
            false,
            isMinimized,
            (int)bounds.Width,
            (int)bounds.Height,
            bounds,
            (int)bounds.Width,
            (int)bounds.Height,
            true,
            title,
            className,
            "ControlType.Window",
            checked((int)windowHandle),
            1,
            0,
            0,
            false,
            ViberWindowCandidateType.Unknown,
            0,
            ViberWindowRejectionReasons.Accepted)
    {
    }
}

public sealed record ViberWindowDiscoveryResult(
    int ProcessCount,
    int TotalTopLevelHandleCount,
    int VisibleTopLevelWindowCount,
    IReadOnlyList<ViberProcessInfo> Processes,
    IReadOnlyList<ViberWindowCandidate> AllWindows,
    IReadOnlyList<ViberWindowCandidate> Candidates,
    ViberWindowCandidate? MainWindow,
    ViberWindowCandidate? CallWindow,
    bool HasUiAutomationAccessFailure = false,
    string? LastDiscoveryError = null,
    TimeSpan DiscoveryDuration = default,
    DateTimeOffset? DiscoveredAtUtc = null)
{
    public int IgnoredWindowCount => TotalTopLevelHandleCount - Candidates.Count;
    public bool IsViberConnected => ProcessCount > 0;
    public bool HasDiscoveredWindow =>
        MainWindow is not null ||
        CallWindow is not null ||
        Candidates.Count > 0 ||
        AllWindows.Any(window =>
            window.IsWindow &&
            !window.IsChildWindow &&
            window.Width > 0 &&
            window.Height > 0 &&
            (window.IsVisible || window.IsMinimized));
    public ViberProcessInfo? SelectedProcess
    {
        get
        {
            var selectedPid = (MainWindow ?? CallWindow)?.ProcessId;
            if (selectedPid is int pid)
            {
                foreach (var process in Processes)
                {
                    if (process.ProcessId == pid)
                    {
                        return process;
                    }
                }
            }

            return Processes.Count > 0 ? Processes[0] : null;
        }
    }

    public ViberWindowCandidate? SelectedWindow =>
        CallWindow ?? MainWindow ?? (Candidates.Count > 0 ? Candidates[0] : null);
}
