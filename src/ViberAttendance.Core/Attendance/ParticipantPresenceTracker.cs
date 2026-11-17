using ViberAttendance.Core.Models;

namespace ViberAttendance.Core.Attendance;

public sealed class ParticipantPresenceTracker(
    Guid callId,
    int onlineConfirmationCount,
    TimeSpan offlineConfirmationTime,
    TimeSpan heartbeatInterval)
{
    private readonly Dictionary<Guid, Entry> _activeEntries = [];
    private readonly Dictionary<Guid, TimeSpan> _completedTotals = [];
    private readonly List<Entry> _entries = [];

    public PresenceTrackingUpdate Apply(
        IReadOnlyList<ResolvedParticipantObservation> observations,
        DateTimeOffset now)
    {
        var changed = new List<Entry>();
        var heartbeats = new List<Entry>();
        var observedByMember = observations
            .GroupBy(observation => observation.MemberId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.Confidence).First());

        foreach (var observation in observedByMember.Values)
        {
            if (!_activeEntries.TryGetValue(observation.MemberId, out var entry))
            {
                entry = CreateEntry(observation, now);
                _activeEntries[observation.MemberId] = entry;
                _entries.Add(entry);
                changed.Add(entry);
                continue;
            }

            entry.DisplayName = observation.DisplayName;
            if (!string.IsNullOrWhiteSpace(observation.StableUiIdentity))
            {
                entry.StableUiIdentity = observation.StableUiIdentity;
            }

            entry.LastSeenAtUtc = now;
            entry.Source = observation.Source;
            entry.Confidence = observation.Confidence;

            switch (entry.State)
            {
                case MemberPresenceState.CandidateOnline:
                    entry.ConsecutiveSeenCount++;
                    if (entry.ConsecutiveSeenCount >= onlineConfirmationCount)
                    {
                        entry.State = MemberPresenceState.Online;
                        entry.OnlineAtUtc = now;
                        entry.LastPersistedAtUtc = now;
                        changed.Add(entry);
                    }
                    break;

                case MemberPresenceState.CandidateOffline:
                    entry.State = MemberPresenceState.Online;
                    entry.LastPersistedAtUtc = now;
                    changed.Add(entry);
                    break;

                case MemberPresenceState.Online:
                    if (now - entry.LastPersistedAtUtc >= heartbeatInterval)
                    {
                        entry.LastPersistedAtUtc = now;
                        heartbeats.Add(entry);
                    }
                    break;
            }
        }

        foreach (var pair in _activeEntries.ToArray())
        {
            if (observedByMember.ContainsKey(pair.Key))
            {
                continue;
            }

            var entry = pair.Value;
            if (entry.State == MemberPresenceState.CandidateOnline)
            {
                _activeEntries.Remove(pair.Key);
                _entries.Remove(entry);
                continue;
            }

            if (entry.State == MemberPresenceState.Online)
            {
                entry.State = MemberPresenceState.CandidateOffline;
                changed.Add(entry);
            }

            if (now - entry.LastSeenAtUtc < offlineConfirmationTime)
            {
                continue;
            }

            CompleteEntry(entry, now);
            _activeEntries.Remove(pair.Key);
            changed.Add(entry);
        }

        return CreateUpdate(now, changed, heartbeats);
    }

    public PresenceTrackingUpdate Stop(DateTimeOffset now)
    {
        var changed = new List<Entry>();
        foreach (var entry in _activeEntries.Values)
        {
            if (entry.OnlineAtUtc is not null)
            {
                CompleteEntry(entry, now);
                changed.Add(entry);
            }
        }

        _activeEntries.Clear();
        _entries.RemoveAll(entry => entry.OnlineAtUtc is null);
        return CreateUpdate(now, changed, []);
    }

    private Entry CreateEntry(ResolvedParticipantObservation observation, DateTimeOffset now)
    {
        var entry = new Entry
        {
            IntervalId = Guid.NewGuid(),
            MemberId = observation.MemberId,
            DisplayName = observation.DisplayName,
            StableUiIdentity = observation.StableUiIdentity,
            FirstSeenAtUtc = now,
            LastSeenAtUtc = now,
            LastPersistedAtUtc = now,
            State = onlineConfirmationCount <= 1
                ? MemberPresenceState.Online
                : MemberPresenceState.CandidateOnline,
            ConsecutiveSeenCount = 1,
            Source = observation.Source,
            Confidence = observation.Confidence,
            OnlineAtUtc = onlineConfirmationCount <= 1 ? now : null,
            PreviousTotal = _completedTotals.GetValueOrDefault(observation.MemberId)
        };
        return entry;
    }

    private void CompleteEntry(Entry entry, DateTimeOffset now)
    {
        entry.State = MemberPresenceState.Offline;
        entry.OfflineAtUtc = now;
        var duration = entry.OnlineAtUtc is DateTimeOffset onlineAt
            ? now - onlineAt
            : TimeSpan.Zero;
        _completedTotals[entry.MemberId] = entry.PreviousTotal + duration;
    }

    private PresenceTrackingUpdate CreateUpdate(
        DateTimeOffset now,
        IReadOnlyList<Entry> changed,
        IReadOnlyList<Entry> heartbeats) =>
        new(
            _entries.Select(entry => ToSnapshot(entry, now)).ToArray(),
            changed
                .DistinctBy(entry => entry.IntervalId)
                .Select(entry => ToSnapshot(entry, now))
                .ToArray(),
            heartbeats
                .DistinctBy(entry => entry.IntervalId)
                .Select(entry => ToSnapshot(entry, now))
                .ToArray());

    private ParticipantPresenceSnapshot ToSnapshot(Entry entry, DateTimeOffset now)
    {
        var currentDuration = entry.OnlineAtUtc is not DateTimeOffset onlineAt
            ? TimeSpan.Zero
            : (entry.OfflineAtUtc ?? now) - onlineAt;
        return new ParticipantPresenceSnapshot(
            entry.IntervalId,
            callId,
            entry.MemberId,
            entry.DisplayName,
            entry.FirstSeenAtUtc,
            entry.OnlineAtUtc,
            entry.LastSeenAtUtc,
            entry.OfflineAtUtc,
            entry.State,
            currentDuration,
            entry.PreviousTotal + currentDuration,
            entry.Source,
            entry.Confidence,
            entry.StableUiIdentity);
    }

    private sealed class Entry
    {
        internal Guid IntervalId { get; init; }
        internal Guid MemberId { get; init; }
        internal required string DisplayName { get; set; }
        internal string? StableUiIdentity { get; set; }
        internal DateTimeOffset FirstSeenAtUtc { get; init; }
        internal DateTimeOffset? OnlineAtUtc { get; set; }
        internal DateTimeOffset LastSeenAtUtc { get; set; }
        internal DateTimeOffset? OfflineAtUtc { get; set; }
        internal DateTimeOffset LastPersistedAtUtc { get; set; }
        internal MemberPresenceState State { get; set; }
        internal int ConsecutiveSeenCount { get; set; }
        internal DetectionSource Source { get; set; }
        internal double Confidence { get; set; }
        internal TimeSpan PreviousTotal { get; init; }
    }
}
