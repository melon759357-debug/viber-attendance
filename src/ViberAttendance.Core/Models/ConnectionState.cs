namespace ViberAttendance.Core.Models;

/// <summary>
/// 分层连接状态。进程/窗口/通话/参与者面板/姓名读取互相独立，禁止用后层失败回退到 ViberNotRunning。
/// </summary>
public enum ConnectionState
{
    ViberNotRunning,
    ViberProcessFound,
    ViberWindowFound,
    CallNotDetected,
    CallDetected,
    ParticipantPanelNotFound,
    ParticipantPanelFound,
    ParticipantReadingFailed,
    Monitoring,
    DetectionTemporarilyFailed
}

/// <summary>完整诊断后对识别方案的建议。</summary>
public enum ParticipantCaptureStrategy
{
    ContinueUiAutomation,
    TryMsaaThenUiAutomation,
    PreferLocalRegionOcr,
    InsufficientEvidence
}
