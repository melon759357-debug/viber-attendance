using ViberAttendance.Core.Models;
using ViberAttendance.UIAutomation;

namespace ViberAttendance.Tests;

public sealed class ViberWindowEvaluatorTests
{
    private readonly ViberWindowEvaluator _evaluator = new(300, 200);
    private static readonly IReadOnlyList<ViberProcessInfo> OneProcess =
    [
        new(100, "Viber", @"C:\Program Files\Viber\Viber.exe", "Viber", "Viber", 0, "Viber", ["ProcessName"])
    ];

    [Fact]
    public void OneVisibleMainWindowAndTwelveHiddenWindowsProducesOneUsableCandidate()
    {
        var snapshots = new List<WindowInspectionSnapshot>
        {
            CreateSnapshot(1, title: "Viber", mainSignals: 3)
        };
        snapshots.AddRange(Enumerable.Range(2, 12).Select(hwnd =>
            CreateSnapshot(hwnd, isVisible: false, uiaControlType: string.Empty, uiaVisitedNodes: 0)));

        var result = _evaluator.Evaluate(OneProcess, snapshots);

        Assert.Equal(13, result.TotalTopLevelHandleCount);
        Assert.Equal(1, result.VisibleTopLevelWindowCount);
        Assert.Single(result.Candidates);
        Assert.Equal(12, result.IgnoredWindowCount);
        Assert.Equal(12, result.AllWindows.Count(window =>
            window.RejectionReason == ViberWindowRejectionReasons.Hidden));
        Assert.True(result.IsViberConnected);
        Assert.True(result.HasDiscoveredWindow);
    }

    [Fact]
    public void ToolWindowWithViberTitleIsNotTreatedAsMissingViber()
    {
        var result = _evaluator.Evaluate(OneProcess, [CreateSnapshot(1, isToolWindow: true, title: "Viber")]);

        Assert.True(result.IsViberConnected);
        Assert.True(result.HasDiscoveredWindow);
        Assert.NotNull(result.MainWindow);
    }

    [Fact]
    public void CloakedWindowIsStillDiscoverable()
    {
        var result = _evaluator.Evaluate(OneProcess, [CreateSnapshot(1, isCloaked: true, title: "Viber", mainSignals: 2)]);

        Assert.True(result.IsViberConnected);
        Assert.NotEmpty(result.Candidates);
        Assert.Equal(ViberWindowRejectionReasons.Accepted, result.Candidates[0].RejectionReason);
    }

    [Fact]
    public void ChildWindowIsRejected()
    {
        var result = _evaluator.Evaluate(OneProcess, [CreateSnapshot(1, isChildWindow: true, isRootWindow: false)]);

        var window = Assert.Single(result.AllWindows);
        Assert.Empty(result.Candidates);
        Assert.Equal(ViberWindowRejectionReasons.ChildWindow, window.RejectionReason);
    }

    [Fact]
    public void SmallNotificationWindowIsClassifiedButCannotBecomeMainWindow()
    {
        var notification = CreateSnapshot(
            1,
            title: "Viber Notification",
            width: 250,
            height: 120,
            hasOwner: true);

        var result = _evaluator.Evaluate(OneProcess, [notification]);

        var window = Assert.Single(result.AllWindows);
        Assert.Equal(ViberWindowCandidateType.NotificationWindow, window.CandidateType);
        Assert.Equal(ViberWindowRejectionReasons.NotificationPopup, window.RejectionReason);
        Assert.Null(result.MainWindow);
        Assert.True(result.IsViberConnected);
    }

    [Fact]
    public void RepeatedHwndIsCountedOnlyOnce()
    {
        var first = CreateSnapshot(10, title: "Viber", mainSignals: 2);
        var duplicate = CreateSnapshot(10, title: "Viber", mainSignals: 2);

        var result = _evaluator.Evaluate(OneProcess, [first, duplicate]);

        Assert.Equal(1, result.TotalTopLevelHandleCount);
        Assert.Single(result.AllWindows);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void CallSurfaceInsideChatWindowIsTheCallWindow()
    {
        var chat = CreateSnapshot(
            1,
            title: "Rakuten Viber",
            mainSignals: 4,
            callSignals: 1,
            hasCallSurface: true);

        var result = _evaluator.Evaluate(OneProcess, [chat]);

        Assert.Equal(1, result.CallWindow?.Hwnd);
        Assert.Equal(ViberWindowCandidateType.CallWindow, result.CallWindow?.CandidateType);
    }

    [Fact]
    public void CollapsedPreviewIsChosenOverTheChatWindow()
    {
        var chat = CreateSnapshot(1, title: "Rakuten Viber", mainSignals: 4, width: 800, height: 617);
        var preview = CreateSnapshot(
            2,
            title: "ViberPC",
            isToolWindow: true,
            width: 336,
            height: 241,
            hasCallSurface: true);

        var result = _evaluator.Evaluate(OneProcess, [chat, preview]);

        Assert.Equal(2, result.CallWindow?.Hwnd);
        Assert.Equal(1, result.MainWindow?.Hwnd);
        Assert.Equal(ViberWindowRejectionReasons.Accepted, result.CallWindow?.RejectionReason);
    }

    [Fact]
    public void IndependentCallWindowIsSelectedSeparatelyFromMainWindow()
    {
        var main = CreateSnapshot(1, title: "Viber", mainSignals: 4, isForeground: false);
        var call = CreateSnapshot(2, title: "Viber Call", callSignals: 4, isForeground: true);

        var result = _evaluator.Evaluate(OneProcess, [main, call]);

        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal(1, result.MainWindow?.Hwnd);
        Assert.Equal(2, result.CallWindow?.Hwnd);
        Assert.Equal(ViberWindowCandidateType.CallWindow, result.CallWindow?.CandidateType);
    }

    [Fact]
    public void MinimizedMainWindowDoesNotCauseHiddenBackgroundSelection()
    {
        var minimizedMain = CreateSnapshot(
            1,
            title: "Viber",
            mainSignals: 3,
            isMinimized: true);
        var hiddenBackground = CreateSnapshot(
            2,
            title: "Viber Background",
            isVisible: false,
            uiaControlType: string.Empty,
            uiaVisitedNodes: 0);

        var result = _evaluator.Evaluate(OneProcess, [minimizedMain, hiddenBackground]);

        Assert.Equal(1, result.MainWindow?.Hwnd);
        Assert.DoesNotContain(result.Candidates, window => window.Hwnd == 2);
        Assert.Equal(ConnectionState.CallNotDetected, ResolveState(result));
    }

    [Fact]
    public void MainWindowHandleZeroStillKeepsProcessConnectedWhenEnumWindowsFindsWindow()
    {
        var processes = new List<ViberProcessInfo>
        {
            new(100, "Viber", @"C:\Viber\Viber.exe", "Viber", "Viber", 0, string.Empty, ["ProcessName"])
        };
        var snapshot = CreateSnapshot(42, title: "Viber", mainSignals: 2);

        var result = _evaluator.Evaluate(processes, [snapshot]);

        Assert.Equal(0, processes[0].MainWindowHandle);
        Assert.NotNull(result.MainWindow);
        Assert.Equal(42, result.MainWindow.Hwnd);
        Assert.NotEqual(ConnectionState.ViberNotRunning, ResolveState(result));
    }

    [Fact]
    public void NoUsableUiaRootStillKeepsWindowAsCandidate()
    {
        var snapshot = CreateSnapshot(
            1,
            title: "Viber",
            uiaControlType: string.Empty,
            uiaVisitedNodes: 0,
            mainSignals: 0);

        var result = _evaluator.Evaluate(OneProcess, [snapshot]);

        Assert.NotEmpty(result.Candidates);
        Assert.NotNull(result.MainWindow);
        Assert.NotEqual(ConnectionState.ViberNotRunning, ResolveState(result));
    }

    [Fact]
    public void NoUsableWindowReportsZeroInsteadOfRawHandleCount()
    {
        var snapshots = Enumerable.Range(1, 4)
            .Select(hwnd => CreateSnapshot(
                hwnd,
                isVisible: false,
                uiaControlType: string.Empty,
                uiaVisitedNodes: 0))
            .ToArray();

        var result = _evaluator.Evaluate(OneProcess, snapshots);

        Assert.Equal(4, result.TotalTopLevelHandleCount);
        Assert.Empty(result.Candidates);
        Assert.Equal(4, result.IgnoredWindowCount);
        Assert.True(result.IsViberConnected);
        Assert.False(result.HasDiscoveredWindow);
        Assert.Equal(ConnectionState.ViberProcessFound, ResolveState(result));
    }

    [Fact]
    public void DiagnosticAndUserFacingCountsRemainSeparate()
    {
        var accepted = CreateSnapshot(1, title: "Viber", mainSignals: 2);
        var hidden = CreateSnapshot(2, isVisible: false, uiaControlType: string.Empty, uiaVisitedNodes: 0);
        var tool = CreateSnapshot(3, isToolWindow: true, title: "helper", className: "IME");

        var result = _evaluator.Evaluate(OneProcess, [accepted, hidden, tool]);

        Assert.Equal(3, result.TotalTopLevelHandleCount);
        Assert.Equal(2, result.VisibleTopLevelWindowCount);
        Assert.Single(result.Candidates);
        Assert.Equal(2, result.IgnoredWindowCount);
        Assert.Equal(3, result.AllWindows.Count);
    }

    private static ConnectionState ResolveState(ViberWindowDiscoveryResult discovery)
    {
        if (discovery.ProcessCount == 0)
        {
            return ConnectionState.ViberNotRunning;
        }

        if (!discovery.HasDiscoveredWindow || discovery.Candidates.Count == 0 && discovery.MainWindow is null)
        {
            return discovery.HasDiscoveredWindow
                ? ConnectionState.ViberWindowFound
                : ConnectionState.ViberProcessFound;
        }

        return discovery.CallWindow is null
            ? ConnectionState.CallNotDetected
            : ConnectionState.CallDetected;
    }

    private static WindowInspectionSnapshot CreateSnapshot(
        long hwnd,
        string title = "Viber",
        string className = "Qt5152QWindowIcon",
        bool isVisible = true,
        bool isRootWindow = true,
        bool isCloaked = false,
        bool isToolWindow = false,
        bool isChildWindow = false,
        bool hasOwner = false,
        bool isMinimized = false,
        int width = 1_000,
        int height = 700,
        string uiaControlType = "ControlType.Window",
        int uiaVisitedNodes = 10,
        int mainSignals = 0,
        int callSignals = 0,
        bool hasCallSurface = false,
        bool isForeground = false) =>
        new()
        {
            Hwnd = hwnd,
            ProcessId = 100,
            ProcessName = "Viber",
            WindowTitle = title,
            ClassName = className,
            IsWindow = true,
            IsVisible = isVisible,
            IsRootWindow = isRootWindow,
            IsCloaked = isCloaked,
            IsToolWindow = isToolWindow,
            IsChildWindow = isChildWindow,
            HasOwner = hasOwner,
            IsMinimized = isMinimized,
            Bounds = new PixelRect(100, 100, width, height),
            ClientWidth = width,
            ClientHeight = height,
            IntersectsDesktop = true,
            UiaName = title,
            UiaClassName = className,
            UiaControlType = uiaControlType,
            UiaNativeWindowHandle = (int)hwnd,
            UiaVisitedNodeCount = uiaVisitedNodes,
            MainInterfaceSignalCount = mainSignals,
            CallSignalCount = callSignals,
            HasCallSurface = hasCallSurface,
            IsForeground = isForeground
        };
}
