using System.Windows.Automation;
using ViberAttendance.Core.Contracts;
using ViberAttendance.Core.Models;

namespace ViberAttendance.UIAutomation;

public sealed class UiAutomationParticipantDetector(IClock clock, IDiagnosticLogger logger)
    : IUiAutomationParticipantDetector
{
    private static readonly string[] ParticipantListTokens =
    [
        "participant", "participants", "member", "members",
        "参与者", "成员", "参会者", "participante", "participantes"
    ];

    private static readonly string[] IgnoredNameTokens =
    [
        "participant", "participants", "member", "members", "invite", "add participant",
        "mute", "unmute", "camera", "microphone", "hang up", "leave", "search",
        "参与者", "成员", "邀请", "添加成员", "静音", "麦克风", "摄像头", "挂断", "离开", "搜索",
        "participante", "participantes", "invitar", "silenciar", "cámara", "micrófono", "colgar"
    ];

    private readonly CachedContainerReader<AutomationElement, ParticipantDetectionResult> _containerReader = new();

    public Task<ParticipantDetectionResult> DetectAsync(
        ViberWindowCandidate window,
        CancellationToken cancellationToken = default) =>
        StaTaskRunner.RunAsync(() => DetectCore(window, cancellationToken));

    private ParticipantDetectionResult DetectCore(
        ViberWindowCandidate window,
        CancellationToken cancellationToken)
    {
        try
        {
            var root = AutomationElement.FromHandle(new nint(window.WindowHandle));
            var webParticipants = _containerReader.Read(
                window.WindowHandle,
                container => ReadParticipantContainer(container, cancellationToken),
                () => CollectWebViewParticipants(root, cancellationToken));
            if (webParticipants is not null)
            {
                return webParticipants;
            }

            var listRoot = FindParticipantList(root, cancellationToken);
            if (listRoot is null)
            {
                return new ParticipantDetectionResult(false, []);
            }

            var observations = CollectParticipantRows(listRoot, cancellationToken)
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Select(item => new ParticipantObservation(
                    item.Name,
                    item.RuntimeId,
                    null,
                    DetectionSource.UiAutomation,
                    0.9,
                    clock.UtcNow))
                .ToArray();
            return new ParticipantDetectionResult(true, observations);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            _ = logger.WriteAsync(
                DiagnosticLogLevel.Error,
                "participant.uia.access.denied",
                "找到窗口但 UIA 因权限不足无法读取。",
                exception,
                CancellationToken.None);
            return new ParticipantDetectionResult(false, [], IsUiAutomationAccessDenied: true, exception.Message);
        }
        catch (Exception exception) when (
            exception is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            var accessDenied = exception is System.Runtime.InteropServices.COMException com &&
                (com.HResult == unchecked((int)0x80070005) || com.HResult == unchecked((int)0x800401E4));
            _ = logger.WriteAsync(
                DiagnosticLogLevel.Error,
                accessDenied ? "participant.uia.access.denied" : "participant.uia.read.failed",
                accessDenied ? "找到窗口但 UIA 因权限不足无法读取。" : "参与者名单 UI Automation 读取失败。",
                exception,
                CancellationToken.None);
            return new ParticipantDetectionResult(false, [], accessDenied, exception.Message);
        }
    }

    private ParticipantDetectionResult? ReadParticipantContainer(
        AutomationElement container,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CallParticipantNames.IsParticipantContainer(container.Current.AutomationId))
        {
            return null;
        }

        var request = new CacheRequest { TreeScope = TreeScope.Element };
        request.Add(AutomationElement.NameProperty);
        request.Add(AutomationElement.AutomationIdProperty);
        request.Add(AutomationElement.ControlTypeProperty);

        AutomationElementCollection texts;
        using (request.Activate())
        {
            texts = container.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
        }

        var byIdentity = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (AutomationElement text in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = text.Cached;
            if (CallParticipantNames.TryReadNameNode(cached.AutomationId, cached.Name, true, out var stableId, out var displayName))
            {
                byIdentity.TryAdd(stableId, displayName);
            }
        }

        return byIdentity.Count == 0 ? null : ToResult(byIdentity);
    }

    private (AutomationElement? Container, ParticipantDetectionResult? Result) CollectWebViewParticipants(
        AutomationElement root,
        CancellationToken cancellationToken)
    {
        var walker = TreeWalker.ControlViewWalker;
        var queue = new Queue<(AutomationElement Element, int Depth)>();
        queue.Enqueue((root, 0));
        var foundSurface = false;
        AutomationElement? container = null;
        var byIdentity = new Dictionary<string, string>(StringComparer.Ordinal);
        var visited = 0;

        while (queue.Count > 0 && visited < 5_000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (element, depth) = queue.Dequeue();
            visited++;
            var automationId = Safe(() => element.Current.AutomationId, string.Empty);
            var className = Safe(() => element.Current.ClassName, string.Empty);
            var name = Safe(() => element.Current.Name, string.Empty);
            var isText = Safe(() => element.Current.ControlType, ControlType.Custom) == ControlType.Text;
            if (CallParticipantNames.IsCallSurface(automationId, className))
            {
                foundSurface = true;
            }

            if (container is null && CallParticipantNames.IsParticipantContainer(automationId))
            {
                container = element;
            }

            if (CallParticipantNames.TryReadNameNode(automationId, name, isText, out var stableId, out var displayName))
            {
                byIdentity.TryAdd(stableId, displayName);
            }

            if (depth >= 18)
            {
                continue;
            }

            foreach (var child in ReadChildren(element, walker))
            {
                queue.Enqueue((child, depth + 1));
            }
        }

        if (!foundSurface && byIdentity.Count == 0)
        {
            return (null, null);
        }

        return (byIdentity.Count > 0 ? container : null, ToResult(byIdentity));
    }

    private ParticipantDetectionResult ToResult(IReadOnlyDictionary<string, string> byIdentity)
    {
        var now = clock.UtcNow;
        var observations = byIdentity
            .Select(item => new ParticipantObservation(
                item.Value,
                item.Key,
                null,
                DetectionSource.UiAutomation,
                0.95,
                now))
            .ToArray();
        return new ParticipantDetectionResult(true, observations);
    }

    private static AutomationElement? FindParticipantList(
        AutomationElement root,
        CancellationToken cancellationToken)
    {
        var walker = TreeWalker.ControlViewWalker;
        var queue = new Queue<(AutomationElement Element, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = 0;

        while (queue.Count > 0 && visited < 5_000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (element, depth) = queue.Dequeue();
            visited++;
            var text = ReadSearchableText(element);
            var controlType = Safe(() => element.Current.ControlType, ControlType.Custom);

            if (IsListControl(controlType) && ContainsAny(text, ParticipantListTokens))
            {
                return element;
            }

            if (ContainsAny(text, ParticipantListTokens))
            {
                var nearbyList = FindNearbyList(element, walker);
                if (nearbyList is not null)
                {
                    return nearbyList;
                }
            }

            if (depth >= 18)
            {
                continue;
            }

            foreach (var child in ReadChildren(element, walker))
            {
                queue.Enqueue((child, depth + 1));
            }
        }

        return null;
    }

    private static AutomationElement? FindNearbyList(AutomationElement anchor, TreeWalker walker)
    {
        var candidates = new List<AutomationElement> { anchor };
        var parent = Safe(() => walker.GetParent(anchor), null);
        if (parent is not null)
        {
            candidates.Add(parent);
        }

        foreach (var candidate in candidates)
        {
            var queue = new Queue<(AutomationElement Element, int Depth)>();
            queue.Enqueue((candidate, 0));
            while (queue.Count > 0)
            {
                var (element, depth) = queue.Dequeue();
                if (IsListControl(Safe(() => element.Current.ControlType, ControlType.Custom)))
                {
                    return element;
                }

                if (depth >= 3)
                {
                    continue;
                }

                foreach (var child in ReadChildren(element, walker))
                {
                    queue.Enqueue((child, depth + 1));
                }
            }
        }

        return null;
    }

    private static List<ParticipantElement> CollectParticipantRows(
        AutomationElement listRoot,
        CancellationToken cancellationToken)
    {
        var walker = TreeWalker.ControlViewWalker;
        var queue = new Queue<(AutomationElement Element, int Depth)>();
        queue.Enqueue((listRoot, 0));
        var preferred = new List<ParticipantElement>();
        var fallback = new List<ParticipantElement>();
        var visited = 0;

        while (queue.Count > 0 && visited < 2_000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (element, depth) = queue.Dequeue();
            visited++;
            var current = element.Current;
            var name = Safe(() => current.Name, string.Empty).Trim();
            var controlType = Safe(() => current.ControlType, ControlType.Custom);
            if (IsUsableParticipantName(name))
            {
                var item = new ParticipantElement(name, FormatRuntimeId(Safe(element.GetRuntimeId, [])));
                if (controlType == ControlType.ListItem || controlType == ControlType.DataItem)
                {
                    preferred.Add(item);
                }
                else if (controlType == ControlType.Text && depth <= 4)
                {
                    fallback.Add(item);
                }
            }

            if (depth >= 8)
            {
                continue;
            }

            foreach (var child in ReadChildren(element, walker))
            {
                queue.Enqueue((child, depth + 1));
            }
        }

        return preferred.Count > 0 ? preferred : fallback;
    }

    private static List<AutomationElement> ReadChildren(AutomationElement element, TreeWalker walker)
    {
        var children = new List<AutomationElement>();
        try
        {
            var child = walker.GetFirstChild(element);
            while (child is not null)
            {
                children.Add(child);
                child = walker.GetNextSibling(child);
            }
        }
        catch (ElementNotAvailableException)
        {
            return [];
        }
        catch (InvalidOperationException)
        {
            return [];
        }

        return children;
    }

    private static string ReadSearchableText(AutomationElement element)
    {
        try
        {
            var current = element.Current;
            return string.Join(' ',
                Safe(() => current.Name, string.Empty),
                Safe(() => current.AutomationId, string.Empty),
                Safe(() => current.ClassName, string.Empty),
                Safe(() => current.LocalizedControlType, string.Empty));
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static bool IsUsableParticipantName(string name) =>
        name.Length is > 0 and <= 120 &&
        name.Any(char.IsLetterOrDigit) &&
        !ContainsAny(name, IgnoredNameTokens) &&
        !name.All(character => char.IsDigit(character) || char.IsPunctuation(character) || char.IsWhiteSpace(character));

    private static bool IsListControl(ControlType controlType) =>
        controlType == ControlType.List || controlType == ControlType.DataGrid || controlType == ControlType.Table;

    private static bool ContainsAny(string value, IEnumerable<string> tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string FormatRuntimeId(IReadOnlyList<int> runtimeId) =>
        runtimeId.Count == 0 ? string.Empty : string.Join('.', runtimeId);

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

    private sealed record ParticipantElement(string Name, string RuntimeId);
}
