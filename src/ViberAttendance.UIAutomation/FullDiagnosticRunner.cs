using ViberAttendance.Core.Attendance;
using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Contracts;
using ViberAttendance.Core.Models;

namespace ViberAttendance.UIAutomation;

public sealed class FullDiagnosticRunner(
    IViberProcessLocator processLocator,
    IViberWindowLocator windowLocator,
    IUiAutomationTreeInspector treeInspector,
    IUiAutomationParticipantDetector participantDetector,
    AppSettings settings,
    IClock clock) : IFullDiagnosticRunner
{
    private static readonly string[] ParticipantTokens =
    [
        "participant", "participants", "member", "members", "成员", "参与者", "参会者"
    ];

    public async Task<FullDiagnosticReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var started = clock.UtcNow;
        string? lastException = null;
        var scannedWindowHandles = new HashSet<long>();

        // 阶段1：进程
        IReadOnlyList<ViberProcessInfo> processes = [];
        DiagnosticStageResult processStage;
        try
        {
            processes = await Task.Run(() => processLocator.FindCandidates(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            processStage = processes.Count > 0
                ? new DiagnosticStageResult(
                    "阶段1：进程发现",
                    true,
                    $"找到 {processes.Count} 个 Viber 候选进程。")
                : new DiagnosticStageResult(
                    "阶段1：进程发现",
                    false,
                    "未找到包含 Viber 的进程。");
        }
        catch (Exception exception)
        {
            lastException = exception.ToString();
            processStage = new DiagnosticStageResult("阶段1：进程发现", false, "进程枚举异常。", exception.Message);
        }

        // 阶段2：窗口
        ViberWindowDiscoveryResult? discovery = null;
        IReadOnlyList<DiagnosticWindowInfo> detectedWindows = [];
        IReadOnlyList<DiagnosticWindowInfo> rejectedWindows = [];
        DiagnosticWindowInfo? selectedWindowInfo = null;
        DiagnosticStageResult windowStage;
        try
        {
            windowLocator.RequestFullScan();
            discovery = await windowLocator.FindAsync(cancellationToken).ConfigureAwait(false);
            detectedWindows = discovery.AllWindows
                .Select(ToWindowInfo)
                .ToArray();
            rejectedWindows = detectedWindows.Where(window => !window.IsAccepted).ToArray();
            var selected = discovery.SelectedWindow;
            selectedWindowInfo = selected is null ? null : ToWindowInfo(selected);
            windowStage = discovery.HasDiscoveredWindow || discovery.Candidates.Count > 0
                ? new DiagnosticStageResult(
                    "阶段2：窗口发现",
                    true,
                    $"顶级窗口 {discovery.TotalTopLevelHandleCount} 个，接受 {discovery.Candidates.Count} 个，拒绝 {rejectedWindows.Count} 个。")
                : new DiagnosticStageResult(
                    "阶段2：窗口发现",
                    false,
                    processes.Count > 0
                        ? "找到进程但没找到可用顶级窗口。"
                        : "因无进程而跳过窗口匹配。");
        }
        catch (Exception exception)
        {
            lastException = exception.ToString();
            windowStage = new DiagnosticStageResult("阶段2：窗口发现", false, "窗口枚举异常。", exception.Message);
        }

        // 阶段3：UIA（对每个候选窗口，去重）
        var uiaResults = new List<DiagnosticUiaWindowResult>();
        DiagnosticStageResult uiaStage;
        try
        {
            var windowsToScan = (discovery?.Candidates.Count > 0
                    ? discovery.Candidates
                    : discovery?.AllWindows.Where(window =>
                        window.IsWindow && !window.IsChildWindow && window.Width > 0 && window.Height > 0).ToArray()
                    ?? [])
                .Where(window => scannedWindowHandles.Add(window.Hwnd))
                .Take(8)
                .ToArray();

            foreach (var window in windowsToScan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                uiaResults.Add(await InspectUiaAsync(window, cancellationToken).ConfigureAwait(false));
            }

            var anyRoot = uiaResults.Any(result => result.RootSucceeded);
            var anyNames = uiaResults.Any(result => result.HasParticipantRelatedText ||
                result.TextElements.Any(item => item.LooksLikeParticipantName));
            uiaStage = windowsToScan.Length == 0
                ? new DiagnosticStageResult("阶段3：UI Automation读取", false, "没有可扫描的窗口。")
                : new DiagnosticStageResult(
                    "阶段3：UI Automation读取",
                    anyRoot,
                    anyRoot
                        ? anyNames
                            ? $"已扫描 {uiaResults.Count} 个窗口，UIA 能看到参与者相关文本。"
                            : $"已扫描 {uiaResults.Count} 个窗口，UIA 根元素可读，但未见参与者姓名文本。"
                        : $"已扫描 {uiaResults.Count} 个窗口，UIA 根元素均失败。");
        }
        catch (Exception exception)
        {
            lastException = exception.ToString();
            uiaStage = new DiagnosticStageResult("阶段3：UI Automation读取", false, "UIA 扫描异常。", exception.Message);
        }

        // 阶段3补充：MSAA
        var msaaResults = new List<DiagnosticMsaaWindowResult>();
        DiagnosticStageResult msaaStage;
        try
        {
            var hwnds = uiaResults.Select(result => result.Hwnd)
                .Concat(detectedWindows.Where(window => window.IsAccepted).Select(window => window.Hwnd))
                .Distinct()
                .Take(8)
                .ToArray();
            foreach (var hwnd in hwnds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                msaaResults.Add(await StaTaskRunner.RunAsync(() => MsaaAccessibilityProbe.Probe(hwnd))
                    .ConfigureAwait(false));
            }

            var anyMsaaNames = msaaResults.Any(result => result.HasParticipantLikeNames);
            msaaStage = hwnds.Length == 0
                ? new DiagnosticStageResult("阶段3b：MSAA读取", false, "没有可探测的窗口。")
                : new DiagnosticStageResult(
                    "阶段3b：MSAA读取",
                    msaaResults.Any(result => result.Succeeded),
                    anyMsaaNames
                        ? "MSAA/LegacyIAccessible 能看到疑似参与者姓名。"
                        : "MSAA 可连接，但未见疑似参与者姓名。");
        }
        catch (Exception exception)
        {
            lastException = exception.ToString();
            msaaStage = new DiagnosticStageResult("阶段3b：MSAA读取", false, "MSAA 探测异常。", exception.Message);
        }

        // 阶段4：参与者
        DiagnosticParticipantStageResult participantDetails;
        DiagnosticStageResult participantStage;
        try
        {
            participantDetails = await InspectParticipantsAsync(discovery, uiaResults, cancellationToken)
                .ConfigureAwait(false);
            participantStage = new DiagnosticStageResult(
                "阶段4：参与者读取",
                participantDetails.PanelFound || participantDetails.CandidateNameCount > 0,
                participantDetails.Summary);
        }
        catch (Exception exception)
        {
            lastException = exception.ToString();
            participantDetails = new DiagnosticParticipantStageResult(
                null, null, null, 0, [], false, false, "参与者阶段异常：" + exception.Message);
            participantStage = new DiagnosticStageResult("阶段4：参与者读取", false, "参与者读取异常。", exception.Message);
        }

        var finalState = ConnectionStateResolver.Resolve(
            processFound: processes.Count > 0,
            windowFound: windowStage.Succeeded,
            callDetected: discovery?.CallWindow is not null ||
                          (discovery?.SelectedWindow?.CallSignalCount ?? 0) > 0 ||
                          uiaResults.Any(result => result.HasParticipantRelatedText),
            participantPanelFound: participantDetails.PanelFound,
            participantNamesReadable: participantDetails.NamesReadable,
            readingFailed: participantDetails.PanelFound && !participantDetails.NamesReadable,
            temporaryFailure: false,
            previousState: ConnectionState.ViberNotRunning);

        var strategy = RecommendStrategy(uiaResults, msaaResults, participantDetails, windowStage.Succeeded);
        var finished = clock.UtcNow;
        return new FullDiagnosticReport(
            started,
            finished,
            finished - started,
            processStage,
            windowStage,
            uiaStage,
            msaaStage,
            participantStage,
            processes,
            detectedWindows,
            rejectedWindows,
            selectedWindowInfo,
            uiaResults,
            msaaResults,
            participantDetails,
            finalState,
            strategy.Strategy,
            strategy.Summary,
            lastException);
    }

    private async Task<DiagnosticUiaWindowResult> InspectUiaAsync(
        ViberWindowCandidate window,
        CancellationToken cancellationToken)
    {
        try
        {
            var scan = await treeInspector.CaptureAsync(
                window,
                new UiAutomationCaptureOptions(
                    Math.Min(settings.UiaMaxNodes, 3_000),
                    Math.Min(settings.UiaMaxDepth, 25)),
                cancellationToken).ConfigureAwait(false);

            var texts = new List<DiagnosticTextElement>();
            CollectTextElements(scan.Root, texts);
            var childCount = scan.Root?.Children.Count ?? 0;
            var hasList = ContainsControlType(scan.Root, "List");
            var hasParticipantText = texts.Any(item =>
                item.LooksLikeParticipantName ||
                ParticipantTokens.Any(token =>
                    item.RedactedName.Contains(token, StringComparison.OrdinalIgnoreCase)));

            return new DiagnosticUiaWindowResult(
                window.Hwnd,
                scan.Root is not null,
                RedactOrNull(scan.Root?.Name),
                scan.Root?.ClassName,
                scan.Root?.AutomationId,
                scan.Root?.ControlType,
                scan.Root?.NativeWindowHandle ?? 0,
                childCount,
                scan.NodeCount,
                texts.Count > 0,
                hasList,
                hasParticipantText,
                texts.Count,
                texts.Take(60).ToArray(),
                scan.Errors,
                RedactTree(scan.Root));
        }
        catch (Exception exception)
        {
            return new DiagnosticUiaWindowResult(
                window.Hwnd,
                false,
                null,
                null,
                null,
                null,
                0,
                0,
                0,
                false,
                false,
                false,
                0,
                [],
                [exception.GetType().Name + ": " + exception.Message],
                null);
        }
    }

    private async Task<DiagnosticParticipantStageResult> InspectParticipantsAsync(
        ViberWindowDiscoveryResult? discovery,
        IReadOnlyList<DiagnosticUiaWindowResult> uiaResults,
        CancellationToken cancellationToken)
    {
        var selected = discovery?.CallWindow ?? discovery?.MainWindow ?? discovery?.SelectedWindow;
        if (selected is null)
        {
            return new DiagnosticParticipantStageResult(
                null, null, null, 0, [], false, false, "没有可选窗口，跳过参与者读取。");
        }

        var detection = await participantDetector.DetectAsync(selected, cancellationToken).ConfigureAwait(false);
        var uiaForWindow = uiaResults.FirstOrDefault(result => result.Hwnd == selected.Hwnd);
        var candidates = detection.Participants
            .Select(participant =>
            {
                var redacted = UserFacingStatuses.RedactDisplayName(participant.RawDisplayName);
                return new DiagnosticTextElement(
                    participant.StableUiIdentity ?? "participant",
                    "ParticipantObservation",
                    redacted.Masked,
                    redacted.Length,
                    string.Empty,
                    true,
                    "接受：参与者检测器输出");
            })
            .Concat(uiaForWindow?.TextElements.Where(item => item.LooksLikeParticipantName) ?? [])
            .GroupBy(item => item.RedactedName + item.NameLength)
            .Select(group => group.First())
            .Take(40)
            .ToArray();

        var panelFound = detection.IsParticipantListFound ||
            (uiaForWindow?.HasListOrListItem ?? false) ||
            (uiaForWindow?.HasParticipantRelatedText ?? false);
        var namesReadable = candidates.Length > 0;
        string summary;
        if (!panelFound && !namesReadable)
        {
            summary = "有窗口但没有参与者名单（请展开参与者面板后重试）。";
        }
        else if (panelFound && !namesReadable)
        {
            summary = "已找到参与者面板/列表痕迹，但暂时读不到成员姓名。";
        }
        else
        {
            summary = $"最终窗口 0x{selected.Hwnd:X}，读到 {candidates.Length} 个候选姓名。";
        }

        return new DiagnosticParticipantStageResult(
            selected.Hwnd,
            uiaForWindow?.HasListOrListItem == true ? "List/ListItem" : null,
            uiaForWindow?.RootControlType,
            candidates.Length,
            candidates,
            panelFound,
            namesReadable,
            summary);
    }

    private static (ParticipantCaptureStrategy Strategy, string Summary) RecommendStrategy(
        IReadOnlyList<DiagnosticUiaWindowResult> uiaResults,
        IReadOnlyList<DiagnosticMsaaWindowResult> msaaResults,
        DiagnosticParticipantStageResult participants,
        bool windowFound)
    {
        if (!windowFound)
        {
            return (ParticipantCaptureStrategy.InsufficientEvidence, "尚未稳定找到窗口，先修复窗口发现。");
        }

        if (participants.NamesReadable ||
            uiaResults.Any(result => result.TextElements.Any(item => item.LooksLikeParticipantName)))
        {
            return (ParticipantCaptureStrategy.ContinueUiAutomation,
                "情况A：UIA 能读到参与者姓名，继续使用 UI Automation，并优化容器选择/过滤。");
        }

        if (msaaResults.Any(result => result.HasParticipantLikeNames))
        {
            return (ParticipantCaptureStrategy.TryMsaaThenUiAutomation,
                "MSAA 能看到疑似姓名而 UIA 文本不足：优先补强 LegacyIAccessible/MSAA，再必要时 OCR。");
        }

        if (uiaResults.Any(result => result.RootSucceeded) &&
            !uiaResults.Any(result => result.HasTextElements && result.TextElements.Any(item => item.LooksLikeParticipantName)))
        {
            return (ParticipantCaptureStrategy.PreferLocalRegionOcr,
                "情况B/C：UIA 只能读到窗口/Pane 或名单容器但无内部文字，且 MSAA 也未见姓名 → 建议切换局部截图 + 本地 OCR。");
        }

        return (ParticipantCaptureStrategy.InsufficientEvidence,
            "证据不足：请展开参与者名单后再次运行完整诊断。");
    }

    private static DiagnosticWindowInfo ToWindowInfo(ViberWindowCandidate window) =>
        new(
            window.Hwnd,
            window.ProcessId,
            window.ProcessName,
            window.WindowTitle,
            window.ClassName,
            window.IsVisible,
            window.IsMinimized,
            window.IsCloaked,
            window.IsToolWindow,
            window.HasOwner,
            window.Width,
            window.Height,
            window.IsAccepted,
            window.RejectionReason,
            window.CandidateType,
            window.Score,
            window.CallSignalCount,
            window.MainInterfaceSignalCount);

    private static void CollectTextElements(UiAutomationNode? node, List<DiagnosticTextElement> texts)
    {
        if (node is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(node.Name))
        {
            var redacted = UserFacingStatuses.RedactDisplayName(node.Name);
            var looksLike = LooksLikePersonName(node.Name);
            var participantToken = ParticipantTokens.Any(token =>
                node.Name.Contains(token, StringComparison.OrdinalIgnoreCase));
            texts.Add(new DiagnosticTextElement(
                node.Path,
                node.ControlType,
                redacted.Masked,
                redacted.Length,
                node.AutomationId,
                looksLike,
                looksLike ? "接受：疑似姓名"
                    : participantToken ? "标记：参与者相关标签"
                    : "排除：非姓名文本（已打码）"));
        }

        foreach (var child in node.Children)
        {
            CollectTextElements(child, texts);
        }
    }

    private static bool ContainsControlType(UiAutomationNode? node, string token)
    {
        if (node is null)
        {
            return false;
        }

        if (node.ControlType.Contains(token, StringComparison.OrdinalIgnoreCase) ||
            node.LocalizedControlType.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return node.Children.Any(child => ContainsControlType(child, token));
    }

    private static UiAutomationNode? RedactTree(UiAutomationNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var copy = new UiAutomationNode
        {
            Path = node.Path,
            RuntimeId = node.RuntimeId,
            AutomationId = node.AutomationId,
            Name = UserFacingStatuses.RedactDisplayName(node.Name).DisplayLabel,
            ControlType = node.ControlType,
            LocalizedControlType = node.LocalizedControlType,
            ClassName = node.ClassName,
            FrameworkId = node.FrameworkId,
            HelpText = string.Empty,
            AccessKey = node.AccessKey,
            AcceleratorKey = node.AcceleratorKey,
            ProcessId = node.ProcessId,
            NativeWindowHandle = node.NativeWindowHandle,
            IsEnabled = node.IsEnabled,
            IsOffscreen = node.IsOffscreen,
            IsKeyboardFocusable = node.IsKeyboardFocusable,
            HasKeyboardFocus = node.HasKeyboardFocus,
            BoundingRectangle = node.BoundingRectangle,
            SupportedPatterns = node.SupportedPatterns
        };
        foreach (var child in node.Children.Take(40))
        {
            var redactedChild = RedactTree(child);
            if (redactedChild is not null)
            {
                copy.Children.Add(redactedChild);
            }
        }

        return copy;
    }

    private static string? RedactOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? value : UserFacingStatuses.RedactDisplayName(value).DisplayLabel;

    private static bool LooksLikePersonName(string value)
    {
        var text = value.Trim();
        if (text.Length is < 2 or > 40)
        {
            return false;
        }

        if (ParticipantTokens.Any(token => text.Equals(token, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return text.Any(char.IsLetter);
    }
}
