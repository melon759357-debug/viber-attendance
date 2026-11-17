using ViberAttendance.Core.Models;

namespace ViberAttendance.Core.Attendance;

/// <summary>纯函数状态解析，避免把参与者/UIA 失败误判为 Viber 未运行。</summary>
public static class ConnectionStateResolver
{
    public static ConnectionState Resolve(
        bool processFound,
        bool windowFound,
        bool callDetected,
        bool participantPanelFound,
        bool participantNamesReadable,
        bool readingFailed,
        bool temporaryFailure,
        ConnectionState previousState)
    {
        if (!processFound)
        {
            // 单次失败不得清除已连接：若上一轮已确认进程存在，则保持临时失败而非未运行。
            if (temporaryFailure && UserFacingStatuses.ImpliesViberRunning(previousState))
            {
                return ConnectionState.DetectionTemporarilyFailed;
            }

            return ConnectionState.ViberNotRunning;
        }

        if (temporaryFailure && UserFacingStatuses.ImpliesViberRunning(previousState))
        {
            return ConnectionState.DetectionTemporarilyFailed;
        }

        if (!windowFound)
        {
            return ConnectionState.ViberProcessFound;
        }

        if (!callDetected)
        {
            return ConnectionState.CallNotDetected;
        }

        if (!participantPanelFound)
        {
            return ConnectionState.ParticipantPanelNotFound;
        }

        if (readingFailed || !participantNamesReadable)
        {
            return participantPanelFound
                ? ConnectionState.ParticipantReadingFailed
                : ConnectionState.ParticipantPanelNotFound;
        }

        if (participantPanelFound && participantNamesReadable)
        {
            return ConnectionState.Monitoring;
        }

        return ConnectionState.ParticipantPanelFound;
    }

    public static ConnectionState ResolveFromDiscovery(
        int processCount,
        bool hasWindow,
        bool hasCall,
        ConnectionState previousState)
    {
        if (processCount <= 0)
        {
            return previousState != ConnectionState.ViberNotRunning &&
                   previousState != ConnectionState.DetectionTemporarilyFailed
                ? ConnectionState.DetectionTemporarilyFailed
                : ConnectionState.ViberNotRunning;
        }

        if (!hasWindow)
        {
            return ConnectionState.ViberProcessFound;
        }

        return hasCall ? ConnectionState.CallDetected : ConnectionState.CallNotDetected;
    }
}
