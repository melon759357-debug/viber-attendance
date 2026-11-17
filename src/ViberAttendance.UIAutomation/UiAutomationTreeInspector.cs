using System.Windows;
using System.Windows.Automation;
using ViberAttendance.Core.Contracts;
using ViberAttendance.Core.Models;

namespace ViberAttendance.UIAutomation;

public sealed class UiAutomationTreeInspector(IClock clock) : IUiAutomationTreeInspector
{
    public Task<UiAutomationScanResult> CaptureAsync(
        ViberWindowCandidate window,
        UiAutomationCaptureOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(options);
        return StaTaskRunner.RunAsync(() => CaptureCore(window, options, cancellationToken));
    }

    private UiAutomationScanResult CaptureCore(
        ViberWindowCandidate window,
        UiAutomationCaptureOptions options,
        CancellationToken cancellationToken)
    {
        var started = clock.GetMonotonicTimestamp();
        var capturedAt = clock.UtcNow;
        var context = new CaptureContext(options, cancellationToken);
        UiAutomationNode? rootNode = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = AutomationElement.FromHandle(new nint(window.WindowHandle));
            rootNode = BuildNode(root, "0", 0, context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            context.AddError("root", exception);
        }

        var finished = clock.GetMonotonicTimestamp();
        return new UiAutomationScanResult(
            capturedAt,
            window,
            rootNode,
            context.NodeCount,
            context.WasTruncated,
            clock.GetElapsedTime(started, finished),
            context.Errors.ToArray());
    }

    private static UiAutomationNode? BuildNode(
        AutomationElement element,
        string path,
        int depth,
        CaptureContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.NodeCount >= context.Options.MaxNodes || depth > context.Options.MaxDepth)
        {
            context.WasTruncated = true;
            return null;
        }

        context.NodeCount++;
        var current = element.Current;
        var rectangle = Safe(() => current.BoundingRectangle, Rect.Empty);
        var node = new UiAutomationNode
        {
            Path = path,
            RuntimeId = FormatRuntimeId(Safe(element.GetRuntimeId, Array.Empty<int>())),
            AutomationId = Safe(() => current.AutomationId, string.Empty),
            Name = Safe(() => current.Name, string.Empty),
            ControlType = Safe(() => current.ControlType.ProgrammaticName, "ControlType.Unknown"),
            LocalizedControlType = Safe(() => current.LocalizedControlType, string.Empty),
            ClassName = Safe(() => current.ClassName, string.Empty),
            FrameworkId = Safe(() => current.FrameworkId, string.Empty),
            HelpText = Safe(() => current.HelpText, string.Empty),
            AccessKey = Safe(() => current.AccessKey, string.Empty),
            AcceleratorKey = Safe(() => current.AcceleratorKey, string.Empty),
            ProcessId = Safe(() => current.ProcessId, 0),
            NativeWindowHandle = Safe(() => current.NativeWindowHandle, 0),
            IsEnabled = Safe(() => current.IsEnabled, false),
            IsOffscreen = Safe(() => current.IsOffscreen, true),
            IsKeyboardFocusable = Safe(() => current.IsKeyboardFocusable, false),
            HasKeyboardFocus = Safe(() => current.HasKeyboardFocus, false),
            BoundingRectangle = ToPixelRect(rectangle),
            SupportedPatterns = Safe(
                () => element.GetSupportedPatterns().Select(pattern => pattern.ProgrammaticName).OrderBy(name => name).ToArray(),
                Array.Empty<string>())
        };

        if (depth == context.Options.MaxDepth)
        {
            context.WasTruncated = true;
            return node;
        }

        var walker = TreeWalker.RawViewWalker;
        AutomationElement? child;
        try
        {
            child = walker.GetFirstChild(element);
        }
        catch (Exception exception)
        {
            context.AddError(path, exception);
            return node;
        }

        var childIndex = 0;
        while (child is not null)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var childNode = BuildNode(child, $"{path}/{childIndex}", depth + 1, context);
            if (childNode is not null)
            {
                node.Children.Add(childNode);
            }

            if (context.NodeCount >= context.Options.MaxNodes)
            {
                context.WasTruncated = true;
                break;
            }

            try
            {
                child = walker.GetNextSibling(child);
            }
            catch (Exception exception)
            {
                context.AddError(path, exception);
                break;
            }

            childIndex++;
        }

        return node;
    }

    private static T Safe<T>(Func<T> action, T fallback)
    {
        try
        {
            return action();
        }
        catch (ElementNotAvailableException)
        {
            return fallback;
        }
        catch (InvalidOperationException)
        {
            return fallback;
        }
    }

    private static string FormatRuntimeId(IReadOnlyList<int> runtimeId) =>
        runtimeId.Count == 0 ? string.Empty : string.Join('.', runtimeId);

    private static PixelRect? ToPixelRect(Rect rectangle)
    {
        if (rectangle.IsEmpty ||
            double.IsInfinity(rectangle.X) ||
            double.IsInfinity(rectangle.Y) ||
            double.IsNaN(rectangle.X) ||
            double.IsNaN(rectangle.Y))
        {
            return null;
        }

        return new PixelRect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    }

    private sealed class CaptureContext(UiAutomationCaptureOptions options, CancellationToken cancellationToken)
    {
        internal UiAutomationCaptureOptions Options { get; } = options;
        internal CancellationToken CancellationToken { get; } = cancellationToken;
        internal List<string> Errors { get; } = [];
        internal int NodeCount { get; set; }
        internal bool WasTruncated { get; set; }

        internal void AddError(string path, Exception exception)
        {
            if (Errors.Count < 100)
            {
                Errors.Add($"{path}: {exception.GetType().Name} (0x{exception.HResult:X8})");
            }
        }
    }
}
