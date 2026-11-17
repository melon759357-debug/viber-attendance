using System.Diagnostics;
using System.Windows.Automation;
using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Contracts;
using ViberAttendance.Core.Models;

namespace ViberAttendance.UIAutomation;

public sealed class ViberWindowLocator(
    AppSettings settings,
    IDiagnosticLogger logger,
    IViberProcessLocator processLocator) : IViberWindowLocator
{
    private static readonly string[] MainInterfaceTokens =
    [
        "chat", "chats", "conversation", "contact", "contacts", "message", "more",
        "聊天", "消息", "联系人", "对话", "更多",
        "conversación", "conversaciones", "contactos", "mensajes"
    ];

    private static readonly string[] CallInterfaceTokens =
    [
        "hang up", "end call", "leave call", "mute", "unmute", "microphone", "camera",
        "participant", "participants", "call duration", "screen share",
        "挂断", "结束通话", "离开通话", "静音", "麦克风", "摄像头", "参与者", "通话时长", "共享屏幕",
        "colgar", "finalizar llamada", "silenciar", "micrófono", "cámara", "participantes", "compartir pantalla"
    ];

    private readonly object _scanStateGate = new();
    private Dictionary<long, WindowInspectionSnapshot> _lastSnapshots = [];
    private IReadOnlyDictionary<long, string> _lastLayouts = new Dictionary<long, string>();
    private long? _lastCallWindowHwnd;
    private DateTimeOffset _lastFullScanAtUtc = DateTimeOffset.MinValue;
    private bool _fullScanRequested = true;

    private enum UiaProbeDepth
    {
        Full,
        Light
    }

    public async Task<ViberWindowDiscoveryResult> FindAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var discovery = await StaTaskRunner.RunAsync(() => FindCore(cancellationToken)).ConfigureAwait(false);
        stopwatch.Stop();
        discovery = discovery with
        {
            DiscoveryDuration = stopwatch.Elapsed,
            DiscoveredAtUtc = DateTimeOffset.UtcNow
        };

        var rejectionSummary = string.Join(
            ", ",
            discovery.AllWindows
                .Where(window => !window.IsAccepted)
                .GroupBy(window => window.RejectionReason)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Key}={group.Count()}"));

        string outcome;
        if (discovery.ProcessCount == 0)
        {
            outcome = "没找到进程";
        }
        else if (!discovery.HasDiscoveredWindow)
        {
            outcome = "找到进程但没找到窗口";
        }
        else if (discovery.HasUiAutomationAccessFailure)
        {
            outcome = "找到窗口但 UIA 失败";
        }
        else if (discovery.CallWindow is null)
        {
            outcome = "已连接但没有通话";
        }
        else
        {
            outcome = "已找到通话窗口";
        }

        await logger.WriteAsync(
            DiagnosticLogLevel.Information,
            "viber.windows.discovery.completed",
            $"outcome={outcome}, processes={discovery.ProcessCount}, topLevel={discovery.TotalTopLevelHandleCount}, " +
            $"visibleTopLevel={discovery.VisibleTopLevelWindowCount}, candidates={discovery.Candidates.Count}, " +
            $"ignored={discovery.IgnoredWindowCount}, uiaFailed={discovery.HasUiAutomationAccessFailure}, " +
            $"elapsedMs={discovery.DiscoveryDuration.TotalMilliseconds:0}, rejections=[{rejectionSummary}]。",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return discovery;
    }

    private ViberWindowDiscoveryResult FindCore(CancellationToken cancellationToken)
    {
        var processes = processLocator.FindCandidates(cancellationToken);
        if (processes.Count == 0)
        {
            return new ViberWindowDiscoveryResult(0, 0, 0, [], [], [], null, null);
        }

        var processNames = processes.ToDictionary(
            process => process.ProcessId,
            process => process.ProcessName);

        var foregroundWindow = NativeMethods.GetForegroundWindow();
        var windows = new List<(nint Handle, int ProcessId, string ProcessName, string Layout)>();
        var seenHwnds = new HashSet<long>();

        _ = NativeMethods.EnumWindows((windowHandle, unusedParameter) =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (!NativeMethods.IsWindow(windowHandle))
            {
                return true;
            }

            var hwndValue = windowHandle.ToInt64();
            if (!seenHwnds.Add(hwndValue))
            {
                return true;
            }

            var threadId = NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
            if (threadId == 0 || !processNames.TryGetValue((int)processId, out var processName))
            {
                return true;
            }

            var style = NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GwlStyle).ToInt64();
            var isChildWindow = (style & NativeMethods.WsChild) != 0;
            var bounds = NativeMethods.ReadBounds(windowHandle);
            if (isChildWindow || bounds.Width <= 0 || bounds.Height <= 0)
            {
                // 第一阶段宽松筛选：仅排除无效句柄、其他进程、子窗口与零尺寸。
                return true;
            }

            var layout = string.Join('|',
                NativeMethods.IsWindowVisible(windowHandle),
                NativeMethods.IsIconic(windowHandle),
                bounds);
            windows.Add((windowHandle, (int)processId, processName, layout));
            return true;
        }, nint.Zero);

        cancellationToken.ThrowIfCancellationRequested();

        var layouts = windows.ToDictionary(window => window.Handle.ToInt64(), window => window.Layout);
        var now = DateTimeOffset.UtcNow;
        bool lightScan;
        long? lastCallWindow;
        lock (_scanStateGate)
        {
            lightScan = !_fullScanRequested &&
                _lastCallWindowHwnd is not null &&
                now - _lastFullScanAtUtc < TimeSpan.FromSeconds(settings.FullScanSeconds) &&
                SameLayouts(_lastLayouts, layouts);
            lastCallWindow = _lastCallWindowHwnd;
        }

        var snapshots = new List<WindowInspectionSnapshot>(windows.Count);
        var uiaAccessFailed = false;
        foreach (var window in windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hwnd = window.Handle.ToInt64();
            WindowInspectionSnapshot? reused = null;
            if (lightScan && hwnd != lastCallWindow)
            {
                lock (_scanStateGate)
                {
                    _lastSnapshots.TryGetValue(hwnd, out reused);
                }
            }

            if (reused is not null)
            {
                snapshots.Add(reused);
                continue;
            }

            var probe = !lightScan ? UiaProbeDepth.Full : UiaProbeDepth.Light;
            snapshots.Add(InspectWindow(
                window.Handle,
                window.ProcessId,
                window.ProcessName,
                foregroundWindow,
                probe,
                cancellationToken,
                out var probeFailed));
            uiaAccessFailed |= probeFailed;
        }

        var evaluated = new ViberWindowEvaluator(settings.WindowMinimumWidth, settings.WindowMinimumHeight)
            .Evaluate(processes, snapshots);

        lock (_scanStateGate)
        {
            if (!lightScan)
            {
                _lastFullScanAtUtc = now;
                _fullScanRequested = false;
            }

            _lastLayouts = layouts;
            _lastCallWindowHwnd = evaluated.CallWindow?.Hwnd;
            _lastSnapshots = snapshots
                .GroupBy(snapshot => snapshot.Hwnd)
                .ToDictionary(group => group.Key, group => group.First());
        }

        return evaluated with { HasUiAutomationAccessFailure = uiaAccessFailed || evaluated.HasUiAutomationAccessFailure };
    }

    public void RequestFullScan()
    {
        lock (_scanStateGate)
        {
            _fullScanRequested = true;
        }
    }

    private static bool SameLayouts(
        IReadOnlyDictionary<long, string> previous,
        IReadOnlyDictionary<long, string> current) =>
        previous.Count == current.Count &&
        current.All(pair => previous.TryGetValue(pair.Key, out var layout) && layout == pair.Value);

    private WindowInspectionSnapshot InspectWindow(
        nint windowHandle,
        int processId,
        string processName,
        nint foregroundWindow,
        UiaProbeDepth probeDepth,
        CancellationToken cancellationToken,
        out bool uiaProbeFailed)
    {
        uiaProbeFailed = false;
        var isWindow = NativeMethods.IsWindow(windowHandle);
        var isVisible = isWindow && NativeMethods.IsWindowVisible(windowHandle);
        var rootWindow = isWindow ? NativeMethods.GetAncestor(windowHandle, NativeMethods.GaRoot) : nint.Zero;
        var style = isWindow ? NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GwlStyle).ToInt64() : 0;
        var extendedStyle = isWindow ? NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GwlExStyle).ToInt64() : 0;
        var isChildWindow = (style & NativeMethods.WsChild) != 0;
        var isToolWindow = (extendedStyle & NativeMethods.WsExToolWindow) != 0;
        var isCloaked = isWindow && NativeMethods.IsCloaked(windowHandle);
        var bounds = isWindow ? NativeMethods.ReadBounds(windowHandle) : new PixelRect(0, 0, 0, 0);
        var clientSize = isWindow ? NativeMethods.ReadClientSize(windowHandle) : (0, 0);

        // 第一阶段：允许在最小化、ToolWindow、无标题等情况下继续探测；UIA 失败不判定“无 Viber”。
        var canProbeUia = isWindow &&
            rootWindow == windowHandle &&
            !isChildWindow &&
            bounds.Width > 0 &&
            bounds.Height > 0;
        var uia = UiaWindowProbe.Empty;
        if (canProbeUia)
        {
            var (maxNodes, maxDepth) = isVisible
                ? (Math.Max(settings.WindowDiscoveryUiaMaxNodes, 1_200), 16)
                : (Math.Min(settings.WindowDiscoveryUiaMaxNodes, 80), 4);
            uia = ProbeUiAutomation(
                windowHandle,
                maxNodes,
                maxDepth,
                stopAtCallSurface: probeDepth == UiaProbeDepth.Light,
                cancellationToken,
                out uiaProbeFailed);
        }

        return new WindowInspectionSnapshot
        {
            Hwnd = windowHandle.ToInt64(),
            ProcessId = processId,
            ProcessName = processName,
            WindowTitle = NativeMethods.ReadTitle(windowHandle),
            ClassName = NativeMethods.ReadClassName(windowHandle),
            IsWindow = isWindow,
            IsVisible = isVisible,
            IsRootWindow = rootWindow == windowHandle,
            IsCloaked = isCloaked,
            IsToolWindow = isToolWindow,
            IsChildWindow = isChildWindow,
            HasOwner = isWindow && NativeMethods.GetWindow(windowHandle, NativeMethods.GwOwner) != nint.Zero,
            IsMinimized = isWindow && NativeMethods.IsIconic(windowHandle),
            Bounds = bounds,
            ClientWidth = clientSize.Item1,
            ClientHeight = clientSize.Item2,
            IntersectsDesktop = isWindow && NativeMethods.IntersectsDesktop(windowHandle),
            UiaName = uia.Name,
            UiaClassName = uia.ClassName,
            UiaControlType = uia.ControlType,
            UiaNativeWindowHandle = uia.NativeWindowHandle,
            UiaVisitedNodeCount = uia.VisitedNodeCount,
            MainInterfaceSignalCount = uia.MainSignals.Count,
            CallSignalCount = uia.CallSignals.Count,
            HasCallSurface = uia.HasCallSurface,
            IsForeground = foregroundWindow == windowHandle
        };
    }

    private static UiaWindowProbe ProbeUiAutomation(
        nint windowHandle,
        int maximumNodes,
        int maximumDepth,
        bool stopAtCallSurface,
        CancellationToken cancellationToken,
        out bool accessFailed)
    {
        accessFailed = false;
        try
        {
            var root = AutomationElement.FromHandle(windowHandle);
            if (root is null)
            {
                accessFailed = true;
                return UiaWindowProbe.Empty;
            }

            var current = root.Current;
            var mainSignals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var callSignals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hasCallSurface = false;
            var stack = new Stack<(AutomationElement Element, int Depth)>();
            stack.Push((root, 0));
            var visited = 0;
            var walker = TreeWalker.ControlViewWalker;

            while (stack.Count > 0 && visited < maximumNodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (element, depth) = stack.Pop();
                visited++;
                if (ReadSignals(element, mainSignals, callSignals))
                {
                    hasCallSurface = true;
                    if (stopAtCallSurface)
                    {
                        break;
                    }
                }

                if (depth >= maximumDepth)
                {
                    continue;
                }

                var children = new List<AutomationElement>();
                AutomationElement? child;
                try
                {
                    child = walker.GetFirstChild(element);
                    while (child is not null && visited + children.Count < maximumNodes)
                    {
                        children.Add(child);
                        child = walker.GetNextSibling(child);
                    }
                }
                catch (ElementNotAvailableException)
                {
                    continue;
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                for (var index = children.Count - 1; index >= 0; index--)
                {
                    stack.Push((children[index], depth + 1));
                }
            }

            return new UiaWindowProbe(
                Safe(() => current.Name, string.Empty),
                Safe(() => current.ClassName, string.Empty),
                Safe(() => current.ControlType.ProgrammaticName, string.Empty),
                Safe(() => current.NativeWindowHandle, 0),
                visited,
                mainSignals,
                callSignals,
                hasCallSurface);
        }
        catch (ElementNotAvailableException)
        {
            accessFailed = true;
            return UiaWindowProbe.Empty;
        }
        catch (InvalidOperationException)
        {
            accessFailed = true;
            return UiaWindowProbe.Empty;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            accessFailed = true;
            return UiaWindowProbe.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            accessFailed = true;
            return UiaWindowProbe.Empty;
        }
    }

    private static bool ReadSignals(
        AutomationElement element,
        HashSet<string> mainSignals,
        HashSet<string> callSignals)
    {
        string searchableText;
        string automationId;
        string className;
        try
        {
            var current = element.Current;
            automationId = Safe(() => current.AutomationId, string.Empty);
            className = Safe(() => current.ClassName, string.Empty);
            searchableText = string.Join(' ',
                Safe(() => current.Name, string.Empty),
                automationId,
                className,
                Safe(() => current.ControlType.ProgrammaticName, string.Empty));
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        foreach (var token in MainInterfaceTokens)
        {
            if (searchableText.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                mainSignals.Add(token);
            }
        }

        foreach (var token in CallInterfaceTokens)
        {
            if (searchableText.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                callSignals.Add(token);
            }
        }

        return CallParticipantNames.IsCallSurface(automationId, className);
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

    private sealed record UiaWindowProbe(
        string Name,
        string ClassName,
        string ControlType,
        int NativeWindowHandle,
        int VisitedNodeCount,
        IReadOnlySet<string> MainSignals,
        IReadOnlySet<string> CallSignals,
        bool HasCallSurface)
    {
        internal static UiaWindowProbe Empty { get; } = new(
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            0,
            new HashSet<string>(),
            new HashSet<string>(),
            false);
    }
}
