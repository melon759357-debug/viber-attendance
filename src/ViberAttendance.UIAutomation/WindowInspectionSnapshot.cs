using ViberAttendance.Core.Models;

namespace ViberAttendance.UIAutomation;

internal sealed class WindowInspectionSnapshot
{
    internal required long Hwnd { get; init; }
    internal required int ProcessId { get; init; }
    internal string ProcessName { get; init; } = "Viber";
    internal string WindowTitle { get; init; } = string.Empty;
    internal string ClassName { get; init; } = string.Empty;
    internal bool IsWindow { get; init; } = true;
    internal bool IsVisible { get; init; } = true;
    internal bool IsRootWindow { get; init; } = true;
    internal bool IsCloaked { get; init; }
    internal bool IsToolWindow { get; init; }
    internal bool IsChildWindow { get; init; }
    internal bool HasOwner { get; init; }
    internal bool IsMinimized { get; init; }
    internal PixelRect Bounds { get; init; } = new(0, 0, 0, 0);
    internal int ClientWidth { get; init; }
    internal int ClientHeight { get; init; }
    internal bool IntersectsDesktop { get; init; } = true;
    internal string UiaName { get; init; } = string.Empty;
    internal string UiaClassName { get; init; } = string.Empty;
    internal string UiaControlType { get; init; } = string.Empty;
    internal int UiaNativeWindowHandle { get; init; }
    internal int UiaVisitedNodeCount { get; init; }
    internal int MainInterfaceSignalCount { get; init; }
    internal int CallSignalCount { get; init; }
    internal bool HasCallSurface { get; init; }
    internal bool IsForeground { get; init; }
}
