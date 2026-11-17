using System.Text.RegularExpressions;

namespace ViberAttendance.UIAutomation;

/// <summary>
/// Viber 桌面版把通话画在 Qt WebEngine 里。参与者不是列表控件，
/// 而是 <c>container.participants</c> 下的文本节点。同一个人在完整页面和小窗里共用编号。
/// </summary>
public static partial class CallParticipantNames
{
    public static bool IsCallSurface(string? automationId, string? className)
    {
        if (ContainsCallSurfaceMarker(automationId) || ContainsCallSurfaceMarker(className))
        {
            return true;
        }

        return IsParticipantContainer(automationId);
    }

    public static bool IsParticipantContainer(string? automationId)
    {
        if (string.IsNullOrWhiteSpace(automationId))
        {
            return false;
        }

        var separator = automationId.LastIndexOf('.');
        var segment = separator >= 0 ? automationId[(separator + 1)..] : automationId;
        return segment.Equals("participants", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryReadNameNode(
        string? automationId,
        string? name,
        bool isText,
        out string stableId,
        out string displayName)
    {
        stableId = string.Empty;
        displayName = string.Empty;
        if (!isText || string.IsNullOrWhiteSpace(automationId) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var match = NameNodeId().Match(automationId);
        if (!match.Success)
        {
            return false;
        }

        var id = match.Groups["id"].Value;
        if (id.Equals("main", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var cleaned = CleanDisplayName(name, id);
        if (!IsUsableName(cleaned))
        {
            return false;
        }

        stableId = id;
        displayName = cleaned;
        return true;
    }

    public static string CleanDisplayName(string name, string stableId)
    {
        var cleaned = HiddenDirectionMarks().Replace(name, string.Empty).Trim();
        if (stableId.Equals("self", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = SelfSuffix().Replace(cleaned, string.Empty).Trim();
        }

        return cleaned;
    }

    private static bool ContainsCallSurfaceMarker(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.Contains("CallWebEngineView", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("videoPreviewWindow", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("CallPreviewWindow", StringComparison.OrdinalIgnoreCase));

    private static bool IsUsableName(string name) =>
        name.Length is > 0 and <= 120 &&
        name.Any(char.IsLetterOrDigit);

    [GeneratedRegex(@"\.name-(?<id>[^.]+)\.QObject(?:\.QObject)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NameNodeId();

    [GeneratedRegex(@"[\u200E\u200F\u202A-\u202E\u2066-\u2069\uFEFF]", RegexOptions.CultureInvariant)]
    private static partial Regex HiddenDirectionMarks();

    [GeneratedRegex(@"\s*[（(](我|You|Tú|Tu|Yo|Я|Ich|Moi|Eu|Ik|Jeg|Sen|Io|나|自分)[)）]\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SelfSuffix();
}
