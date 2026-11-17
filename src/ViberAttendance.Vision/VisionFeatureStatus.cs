using ViberAttendance.Core.Models;

namespace ViberAttendance.Vision;

/// <summary>
/// 局部 OCR 备用方案状态。完整诊断若判定 UIA/MSAA 无法读取姓名，则建议启用。
/// </summary>
public static class VisionFeatureStatus
{
    public const bool IsImplemented = false;
    public const string Reason =
        "需先通过高级诊断确认 UIA/MSAA 无法读取参与者姓名后，再实现局部框选 + 本地 OCR。";

    public static bool ShouldPreferLocalOcr(ParticipantCaptureStrategy strategy) =>
        strategy == ParticipantCaptureStrategy.PreferLocalRegionOcr;

    public static string Describe(ParticipantCaptureStrategy strategy) => strategy switch
    {
        ParticipantCaptureStrategy.ContinueUiAutomation =>
            "继续使用 UI Automation，优化参与者容器选择与过滤。",
        ParticipantCaptureStrategy.TryMsaaThenUiAutomation =>
            "优先 MSAA/LegacyIAccessible，再回退 UIA；仍失败则局部 OCR。",
        ParticipantCaptureStrategy.PreferLocalRegionOcr =>
            "建议切换：Win32 定位窗口 + 用户框选名单区域 + 本地 OCR（不上传）。",
        _ => "证据不足，请展开参与者名单后重新运行完整诊断。"
    };
}
