namespace ViberAttendance.Core.Models;

public enum DetectionSource
{
    Unknown,
    UiAutomation,
    LocalOcr,
    Manual
}

public enum MemberPresenceState
{
    Unknown,
    CandidateOnline,
    Online,
    CandidateOffline,
    Offline
}

public sealed record ParticipantObservation(
    string RawDisplayName,
    string? StableUiIdentity,
    string? AvatarHash,
    DetectionSource Source,
    double Confidence,
    DateTimeOffset ObservedAtUtc);

public sealed record ParticipantDetectionResult(
    bool IsParticipantListFound,
    IReadOnlyList<ParticipantObservation> Participants,
    bool IsUiAutomationAccessDenied = false,
    string? ErrorMessage = null);

public sealed record ParticipantIdentity(Guid MemberId, string DisplayName, bool IsNew);

public sealed record AttendanceTransition(Guid MemberId, MemberPresenceState Previous, MemberPresenceState Current);

public sealed record ResolvedParticipantObservation(
    Guid MemberId,
    string DisplayName,
    DetectionSource Source,
    double Confidence,
    DateTimeOffset ObservedAtUtc,
    string? StableUiIdentity = null);

public sealed record ParticipantPresenceSnapshot(
    Guid IntervalId,
    Guid CallId,
    Guid MemberId,
    string DisplayName,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset? OnlineAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset? OfflineAtUtc,
    MemberPresenceState State,
    TimeSpan CurrentDuration,
    TimeSpan TotalDuration,
    DetectionSource StartSource,
    double StartConfidence,
    string? StableUiIdentity = null);

public sealed record PresenceTrackingUpdate(
    IReadOnlyList<ParticipantPresenceSnapshot> Participants,
    IReadOnlyList<ParticipantPresenceSnapshot> ChangedParticipants,
    IReadOnlyList<ParticipantPresenceSnapshot> HeartbeatParticipants);

public sealed record CallHistoryRecord(
    Guid CallId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    int ParticipantCount,
    TimeSpan TotalDuration,
    string Status);

public sealed record AttendanceIntervalRecord(
    Guid IntervalId,
    Guid CallId,
    Guid MemberId,
    string DisplayName,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset? OnlineAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset? OfflineAtUtc,
    TimeSpan Duration,
    string Status,
    string? StableUiIdentity = null);

public sealed record CallPresence(bool IsDetected, double Confidence, string DiagnosticReason);

public sealed record AttendanceExportRequest(Guid? CallId, DateOnly? Date, Guid? MemberId, bool IncludeAllHistory);

public sealed class HistoryArchive
{
    public string Format { get; set; } = "viber-attendance-history";
    public int Version { get; set; } = 1;
    public List<HistoryArchiveCall> Calls { get; set; } = [];
}

public sealed class HistoryArchiveCall
{
    public Guid Id { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public string Status { get; set; } = "Completed";
    public string Title { get; set; } = "Viber在线记录";
    public List<HistoryArchiveInterval> Intervals { get; set; } = [];
}

public sealed class HistoryArchiveInterval
{
    public Guid Id { get; set; }
    public Guid MemberId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? StableUiIdentity { get; set; }
    public DateTimeOffset FirstSeenAtUtc { get; set; }
    public DateTimeOffset? OnlineAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public DateTimeOffset? OfflineAtUtc { get; set; }
    public long DurationMs { get; set; }
    public string Status { get; set; } = "Offline";
}

public sealed record HistoryImportResult(int ImportedCalls, int SkippedCalls);

public sealed record AttendanceExportResult(IReadOnlyList<string> Files);
