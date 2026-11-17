using ViberAttendance.Core.Models;

namespace ViberAttendance.UIAutomation;

internal sealed class ViberWindowEvaluator(int minimumWidth, int minimumHeight)
{
    private static readonly string[] NotificationTokens =
    [
        "notification", "toast", "popup", "notify", "通知", "提醒"
    ];

    private static readonly string[] CallTitleTokens =
    [
        "call", "calling", "conference", "meeting", "通话", "会议", "llamada", "reunión"
    ];

    private static readonly string[] InternalWindowClasses =
    [
        "Chrome_RenderWidgetHostHWND", "Intermediate D3D Window", "CefBrowserWindow",
        "CEF-OSC-WIDGET", "Chrome_RenderInputWindow", "ViberRenderWidget"
    ];

    internal ViberWindowDiscoveryResult Evaluate(
        IReadOnlyList<ViberProcessInfo> processes,
        IReadOnlyList<WindowInspectionSnapshot> snapshots)
    {
        var uniqueSnapshots = new List<WindowInspectionSnapshot>(snapshots.Count);
        var seenHwnds = new HashSet<long>();
        foreach (var snapshot in snapshots)
        {
            if (seenHwnds.Add(snapshot.Hwnd))
            {
                uniqueSnapshots.Add(snapshot);
            }
        }

        var allWindows = new List<ViberWindowCandidate>(uniqueSnapshots.Count);
        var seenNativeHandles = new HashSet<int>();
        var seenSignatures = new HashSet<WindowSignature>();
        var uiaAccessFailed = false;

        foreach (var snapshot in uniqueSnapshots)
        {
            var candidateType = Classify(snapshot);
            var score = Score(snapshot, candidateType);
            var rejectionReason = GetRejectionReason(snapshot, candidateType);

            if (string.IsNullOrWhiteSpace(snapshot.UiaControlType) &&
                snapshot.UiaVisitedNodeCount <= 1 &&
                snapshot.MainInterfaceSignalCount == 0 &&
                snapshot.CallSignalCount == 0)
            {
                uiaAccessFailed = true;
            }

            if (rejectionReason == ViberWindowRejectionReasons.Accepted &&
                snapshot.UiaNativeWindowHandle != 0 &&
                !seenNativeHandles.Add(snapshot.UiaNativeWindowHandle))
            {
                rejectionReason = ViberWindowRejectionReasons.DuplicateNativeWindowHandle;
            }

            var signature = new WindowSignature(
                snapshot.ProcessId,
                snapshot.Bounds,
                snapshot.WindowTitle,
                snapshot.ClassName);
            if (rejectionReason == ViberWindowRejectionReasons.Accepted && !seenSignatures.Add(signature))
            {
                rejectionReason = ViberWindowRejectionReasons.DuplicateSignature;
            }

            allWindows.Add(ToCandidate(snapshot, candidateType, score, rejectionReason));
        }

        var candidates = allWindows
            .Where(window => window.IsAccepted)
            .OrderByDescending(window => window.Score)
            .ThenByDescending(window => window.IsForeground)
            .ThenBy(window => window.Hwnd)
            .ToArray();

        var mainWindow = candidates
            .Where(window => window.CandidateType is not ViberWindowCandidateType.CallWindow
                and not ViberWindowCandidateType.NotificationWindow
                and not ViberWindowCandidateType.AuxiliaryWindow)
            .OrderByDescending(window => window.CandidateType == ViberWindowCandidateType.MainWindow)
            .ThenByDescending(window => window.IsVisible)
            .ThenByDescending(window => !window.IsMinimized)
            .ThenByDescending(window => window.Score)
            .FirstOrDefault()
            ?? candidates
                .Where(window => window.CandidateType is not ViberWindowCandidateType.NotificationWindow
                    and not ViberWindowCandidateType.AuxiliaryWindow)
                .OrderByDescending(window => window.Score)
                .FirstOrDefault()
            ?? candidates.FirstOrDefault();

        var callWindow = candidates
            .Where(window => window.CandidateType == ViberWindowCandidateType.CallWindow)
            .OrderByDescending(window => window.CallSignalCount)
            .ThenByDescending(window => window.Score)
            .FirstOrDefault();

        var visibleTopLevelCount = uniqueSnapshots.Count(snapshot =>
            snapshot.IsWindow && snapshot.IsRootWindow && snapshot.IsVisible && !snapshot.IsChildWindow);

        return new ViberWindowDiscoveryResult(
            processes.Count,
            uniqueSnapshots.Count,
            visibleTopLevelCount,
            processes,
            allWindows
                .OrderByDescending(window => window.IsAccepted)
                .ThenByDescending(window => window.Score)
                .ThenBy(window => window.Hwnd)
                .ToArray(),
            candidates,
            mainWindow,
            callWindow,
            uiaAccessFailed);
    }

    /// <summary>
    /// 第二阶段评分用筛选。第一阶段已排除无效 HWND / 子窗口 / 零尺寸。
    /// 此处不再因无标题、ToolWindow、最小化、Owner、UIA 暂时失败或尺寸偏小而判定“无 Viber”。
    /// </summary>
    private static string GetRejectionReason(
        WindowInspectionSnapshot snapshot,
        ViberWindowCandidateType candidateType)
    {
        if (!snapshot.IsWindow)
        {
            return ViberWindowRejectionReasons.InvalidWindow;
        }

        if (!snapshot.IsRootWindow || snapshot.IsChildWindow)
        {
            return ViberWindowRejectionReasons.ChildWindow;
        }

        var width = (int)Math.Max(0, snapshot.Bounds.Width);
        var height = (int)Math.Max(0, snapshot.Bounds.Height);
        if (width == 0 || height == 0)
        {
            return ViberWindowRejectionReasons.ZeroSize;
        }

        if (IsInternalRenderingClass(snapshot.ClassName))
        {
            return ViberWindowRejectionReasons.InternalRenderingWindow;
        }

        if (candidateType == ViberWindowCandidateType.NotificationWindow)
        {
            return ViberWindowRejectionReasons.NotificationPopup;
        }

        if (candidateType == ViberWindowCandidateType.AuxiliaryWindow)
        {
            return ViberWindowRejectionReasons.AuxiliaryWindow;
        }

        // 完全不可见且非最小化的后台句柄不作为可用候选，但仍保留在 AllWindows 中供诊断。
        if (!snapshot.IsVisible && !snapshot.IsMinimized)
        {
            return ViberWindowRejectionReasons.Hidden;
        }

        return ViberWindowRejectionReasons.Accepted;
    }

    private static ViberWindowCandidateType Classify(WindowInspectionSnapshot snapshot)
    {
        var combinedText = string.Join(' ',
            snapshot.WindowTitle,
            snapshot.ClassName,
            snapshot.UiaName,
            snapshot.UiaClassName);

        if (ContainsAny(combinedText, NotificationTokens) ||
            (snapshot.HasOwner && snapshot.Bounds.Width < 300 && snapshot.Bounds.Height < 200))
        {
            return ViberWindowCandidateType.NotificationWindow;
        }

        if (snapshot.HasCallSurface ||
            snapshot.CallSignalCount >= 2 ||
            ContainsAny(combinedText, CallTitleTokens))
        {
            return ViberWindowCandidateType.CallWindow;
        }

        if (snapshot.MainInterfaceSignalCount > 0 ||
            combinedText.Contains("viber", StringComparison.OrdinalIgnoreCase) ||
            IsKnownApplicationWindowClass(snapshot.ClassName))
        {
            return ViberWindowCandidateType.MainWindow;
        }

        if (snapshot.HasOwner || snapshot.IsToolWindow || snapshot.IsChildWindow ||
            IsInternalRenderingClass(snapshot.ClassName) || IsKnownAuxiliaryClass(snapshot.ClassName))
        {
            return ViberWindowCandidateType.AuxiliaryWindow;
        }

        // 未知但属于 Viber 进程的顶层窗口仍可作为连接证据与候选。
        return ViberWindowCandidateType.Unknown;
    }

    private int Score(WindowInspectionSnapshot snapshot, ViberWindowCandidateType candidateType)
    {
        var score = 0;
        if (snapshot.ProcessName.Contains("Viber", StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        if (snapshot.IsVisible)
        {
            score += 15;
        }

        if (snapshot.Bounds.Width >= minimumWidth && snapshot.Bounds.Height >= minimumHeight)
        {
            score += 15;
        }
        else if (snapshot.Bounds.Width > 0 && snapshot.Bounds.Height > 0)
        {
            score += 5;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.WindowTitle))
        {
            score += 5;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.UiaControlType))
        {
            score += 15;
        }

        score += Math.Min(snapshot.MainInterfaceSignalCount * 4, 24);
        score += Math.Min(snapshot.CallSignalCount * 7, 42);

        if (snapshot.IsForeground)
        {
            score += 10;
        }

        if (IsKnownApplicationWindowClass(snapshot.ClassName))
        {
            score += 8;
        }

        if (candidateType == ViberWindowCandidateType.CallWindow)
        {
            score += 8;
        }

        if (candidateType == ViberWindowCandidateType.MainWindow)
        {
            score += 6;
        }

        if (snapshot.HasOwner)
        {
            score -= 3;
        }

        if (snapshot.IsToolWindow)
        {
            score -= 8;
        }

        if (snapshot.IsMinimized)
        {
            score -= 5;
        }

        if (snapshot.IsCloaked)
        {
            score -= 6;
        }

        return score;
    }

    private static ViberWindowCandidate ToCandidate(
        WindowInspectionSnapshot snapshot,
        ViberWindowCandidateType candidateType,
        int score,
        string rejectionReason) =>
        new(
            snapshot.Hwnd,
            snapshot.ProcessId,
            snapshot.ProcessName,
            snapshot.WindowTitle,
            snapshot.ClassName,
            snapshot.IsWindow,
            snapshot.IsVisible,
            snapshot.IsCloaked,
            snapshot.IsToolWindow,
            snapshot.IsChildWindow || !snapshot.IsRootWindow,
            snapshot.HasOwner,
            snapshot.IsMinimized,
            (int)Math.Max(0, snapshot.Bounds.Width),
            (int)Math.Max(0, snapshot.Bounds.Height),
            snapshot.Bounds,
            snapshot.ClientWidth,
            snapshot.ClientHeight,
            snapshot.IntersectsDesktop,
            snapshot.UiaName,
            snapshot.UiaClassName,
            snapshot.UiaControlType,
            snapshot.UiaNativeWindowHandle,
            snapshot.UiaVisitedNodeCount,
            snapshot.MainInterfaceSignalCount,
            snapshot.CallSignalCount,
            snapshot.IsForeground,
            candidateType,
            score,
            rejectionReason);

    private static bool IsInternalRenderingClass(string className) =>
        InternalWindowClasses.Any(token => className.Equals(token, StringComparison.OrdinalIgnoreCase));

    private static bool IsKnownApplicationWindowClass(string className) =>
        className.Contains("Qt", StringComparison.OrdinalIgnoreCase) ||
        className.Contains("QWidget", StringComparison.OrdinalIgnoreCase) ||
        className.Equals("Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase) ||
        className.Contains("Viber", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownAuxiliaryClass(string className) =>
        className.Equals("IME", StringComparison.OrdinalIgnoreCase) ||
        className.Equals("_q_titlebar", StringComparison.OrdinalIgnoreCase) ||
        className.Equals("Chrome_WidgetWin_0", StringComparison.OrdinalIgnoreCase) ||
        className.StartsWith("MSCTFIME", StringComparison.OrdinalIgnoreCase) ||
        className.Contains("ObserverWindow", StringComparison.OrdinalIgnoreCase) ||
        className.Contains("PowerMessageWindow", StringComparison.OrdinalIgnoreCase) ||
        className.Contains("TrayIconMessageWindow", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string value, IEnumerable<string> tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private sealed record WindowSignature(
        int ProcessId,
        PixelRect Bounds,
        string WindowTitle,
        string ClassName);
}
