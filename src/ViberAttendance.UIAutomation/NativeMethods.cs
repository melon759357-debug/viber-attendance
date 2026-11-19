using System.Runtime.InteropServices;
using ViberAttendance.Core.Models;

namespace ViberAttendance.UIAutomation;

internal static class NativeMethods
{
    internal const uint GaRoot = 2;
    internal const uint GwOwner = 4;
    internal const int GwlStyle = -16;
    internal const int GwlExStyle = -20;
    internal const long WsChild = 0x40000000L;
    internal const long WsExToolWindow = 0x00000080L;
    internal const uint MonitorDefaultToNull = 0;
    internal const int DwmwaCloaked = 14;

    internal delegate bool EnumWindowsProc(nint windowHandle, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll")]
    internal static extern nint GetAncestor(nint windowHandle, uint flags);

    [DllImport("user32.dll")]
    internal static extern nint GetWindow(nint windowHandle, uint command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern nint GetWindowLongPtr(nint windowHandle, int index);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", SetLastError = true)]
    internal static extern int GetWindowTextLength(nint windowHandle);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowText(nint windowHandle, [Out] char[] text, int maximumCount);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetClassName(nint windowHandle, [Out] char[] className, int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromRect([In] ref NativeRect rectangle, uint flags);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(
        nint windowHandle,
        int attribute,
        out int value,
        int valueSize);

    internal static string ReadTitle(nint windowHandle)
    {
        var length = Math.Max(GetWindowTextLength(windowHandle), 0);
        var buffer = new char[length + 1];
        var copied = GetWindowText(windowHandle, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : string.Empty;
    }

    internal static string ReadClassName(nint windowHandle)
    {
        var buffer = new char[512];
        var copied = GetClassName(windowHandle, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : string.Empty;
    }

    internal static PixelRect ReadBounds(nint windowHandle)
    {
        if (IsIconic(windowHandle))
        {
            var restored = ReadRestoredBounds(windowHandle);
            if (restored.Width > 0 && restored.Height > 0)
            {
                return restored;
            }
        }

        return GetWindowRect(windowHandle, out var rectangle)
            ? new PixelRect(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top)
            : new PixelRect(0, 0, 0, 0);
    }

    private static PixelRect ReadRestoredBounds(nint windowHandle)
    {
        var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
        if (!GetWindowPlacement(windowHandle, ref placement))
        {
            return new PixelRect(0, 0, 0, 0);
        }

        var rectangle = placement.NormalPosition;
        return new PixelRect(
            rectangle.Left,
            rectangle.Top,
            Math.Max(0, rectangle.Right - rectangle.Left),
            Math.Max(0, rectangle.Bottom - rectangle.Top));
    }

    internal static (int Width, int Height) ReadClientSize(nint windowHandle) =>
        GetClientRect(windowHandle, out var rectangle)
            ? (Math.Max(0, rectangle.Right - rectangle.Left), Math.Max(0, rectangle.Bottom - rectangle.Top))
            : (0, 0);

    internal static bool IntersectsDesktop(nint windowHandle)
    {
        if (!GetWindowRect(windowHandle, out var rectangle))
        {
            return false;
        }

        return MonitorFromRect(ref rectangle, MonitorDefaultToNull) != nint.Zero;
    }

    internal static bool IsCloaked(nint windowHandle) =>
        DwmGetWindowAttribute(
            windowHandle,
            DwmwaCloaked,
            out var cloaked,
            Marshal.SizeOf<int>()) == 0 && cloaked != 0;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(nint windowHandle, ref WindowPlacement placement);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        internal int Length;
        internal int Flags;
        internal int ShowCmd;
        internal NativePoint MinPosition;
        internal NativePoint MaxPosition;
        internal NativeRect NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }
}
