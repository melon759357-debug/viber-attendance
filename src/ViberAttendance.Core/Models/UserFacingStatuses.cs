namespace ViberAttendance.Core.Models;

public static class UserFacingStatuses
{
    public const string ViberNotRunning = "尚未打开Viber";
    public const string ViberNotRunningHint = "请先打开 Viber 桌面版。";
    public const string ViberProcessFound = "Viber已运行";
    public const string ViberWindowFound = "已找到Viber窗口";
    public const string CallNotDetected = "Viber已连接，暂未检测到通话";
    public const string CallDetected = "已检测到通话";
    public const string ParticipantPanelNotFound = "已检测到通话，请展开参与者名单";
    public const string ParticipantPanelFound = "正在读取参与者";
    public const string ParticipantReadingFailed = "已找到参与者面板，但暂时无法读取成员";
    public const string Monitoring = "正在监控";
    public const string DetectionTemporarilyFailed = "识别暂时失败，正在重试";

    /// <summary>数据库中表示"程序异常退出后按最后看到时间结束"的状态值。</summary>
    public const string InterruptedStatus = "Interrupted";
    public const string Interrupted = "程序中断，按最后看到时间结束";

    public static string DescribeIntervalStatus(string status, bool isOffline) => status switch
    {
        InterruptedStatus => Interrupted,
        _ when isOffline => "已离线",
        _ => "在线"
    };

    /// <summary>兼容旧名称。</summary>
    public const string ParticipantListNotFound = ParticipantPanelNotFound;
    public const string ParticipantPanelRequired = ParticipantPanelNotFound;
    public const string ViberWindowFoundLegacy = CallNotDetected;
    public const string TechnicalRetry = DetectionTemporarilyFailed;
    public const string UiAutomationAccessDenied = ParticipantReadingFailed;

    public static string GetConnectionStatus(ConnectionState state) => state switch
    {
        ConnectionState.ViberNotRunning => ViberNotRunning,
        ConnectionState.ViberProcessFound => ViberProcessFound,
        ConnectionState.ViberWindowFound => ViberWindowFound,
        ConnectionState.CallNotDetected => CallNotDetected,
        ConnectionState.CallDetected => CallDetected,
        ConnectionState.ParticipantPanelNotFound => ParticipantPanelNotFound,
        ConnectionState.ParticipantPanelFound => ParticipantPanelFound,
        ConnectionState.ParticipantReadingFailed => ParticipantReadingFailed,
        ConnectionState.Monitoring => Monitoring,
        ConnectionState.DetectionTemporarilyFailed => DetectionTemporarilyFailed,
        _ => ViberNotRunning
    };

    public static string GetEmptyStateTitle(ConnectionState state) => state switch
    {
        ConnectionState.ViberNotRunning => ViberNotRunning,
        ConnectionState.ViberProcessFound => ViberProcessFound,
        ConnectionState.ViberWindowFound => ViberWindowFound,
        ConnectionState.CallNotDetected => "已连接Viber，等待通话",
        ConnectionState.CallDetected => CallDetected,
        ConnectionState.ParticipantPanelNotFound => "请展开参与者名单",
        ConnectionState.ParticipantPanelFound => ParticipantPanelFound,
        ConnectionState.ParticipantReadingFailed => "暂时无法读取成员",
        ConnectionState.Monitoring => Monitoring,
        ConnectionState.DetectionTemporarilyFailed => "识别暂时失败",
        _ => ViberNotRunning
    };

    public static string GetEmptyStateDescription(ConnectionState state) => state switch
    {
        ConnectionState.ViberNotRunning => ViberNotRunningHint,
        ConnectionState.ViberProcessFound => "正在查找 Viber 窗口。",
        ConnectionState.ViberWindowFound => "窗口已找到，正在确认通话状态。",
        ConnectionState.CallNotDetected => "打开通话后会自动检测参与者。",
        ConnectionState.CallDetected => "正在寻找参与者名单。",
        ConnectionState.ParticipantPanelNotFound => "在通话窗口中展开参与者名单后开始记录。",
        ConnectionState.ParticipantPanelFound => "正在从名单中读取成员姓名。",
        ConnectionState.ParticipantReadingFailed => "可在高级诊断中运行完整诊断，判断是否需要切换到局部 OCR。",
        ConnectionState.Monitoring => "成员上线、下线时间会自动写入本场记录。",
        ConnectionState.DetectionTemporarilyFailed => "不会因此判定 Viber 未运行，正在自动重试。",
        _ => ViberNotRunningHint
    };

    public static bool ImpliesViberRunning(ConnectionState state) =>
        state is not ConnectionState.ViberNotRunning;

    public static PrivacyRedactedName RedactDisplayName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new PrivacyRedactedName(string.Empty, 0, string.Empty);
        }

        var trimmed = name.Trim();
        var first = trimmed[0];
        return new PrivacyRedactedName($"{first}***", trimmed.Length, $"{first}***（长度{trimmed.Length}）");
    }
}

public sealed record PrivacyRedactedName(string Masked, int Length, string DisplayLabel);
