using ViberAttendance.Core.Attendance;
using ViberAttendance.Core.Models;
using System.Globalization;

namespace ViberAttendance.Tests;

public sealed class ParticipantPresenceTrackerTests
{
    private static readonly Guid CallId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MemberId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse(
        "2026-07-17T10:00:00Z",
        CultureInfo.InvariantCulture);

    [Fact]
    public void NewParticipantIsConfirmedOnlineAfterTwoConsecutiveScans()
    {
        var tracker = CreateTracker();

        var first = tracker.Apply([Observation(Start)], Start);
        var second = tracker.Apply([Observation(Start.AddSeconds(2))], Start.AddSeconds(2));

        Assert.Equal(MemberPresenceState.CandidateOnline, Assert.Single(first.Participants).State);
        var online = Assert.Single(second.Participants);
        Assert.Equal(MemberPresenceState.Online, online.State);
        Assert.Equal(Start, online.FirstSeenAtUtc);
        Assert.Equal(Start.AddSeconds(2), online.OnlineAtUtc);
    }

    [Fact]
    public void OneShortAbsenceDoesNotConfirmOffline()
    {
        var tracker = CreateOnlineTracker();
        var missing = tracker.Apply([], Start.AddSeconds(4));
        var returned = tracker.Apply([Observation(Start.AddSeconds(6))], Start.AddSeconds(6));

        Assert.Equal(MemberPresenceState.CandidateOffline, Assert.Single(missing.Participants).State);
        var participant = Assert.Single(returned.Participants);
        Assert.Equal(MemberPresenceState.Online, participant.State);
        Assert.Null(participant.OfflineAtUtc);
    }

    [Fact]
    public void FiveSecondContinuousAbsenceConfirmsOffline()
    {
        var tracker = CreateOnlineTracker();
        tracker.Apply([], Start.AddSeconds(4));

        var result = tracker.Apply([], Start.AddSeconds(7));

        var participant = Assert.Single(result.Participants);
        Assert.Equal(MemberPresenceState.Offline, participant.State);
        Assert.Equal(Start.AddSeconds(7), participant.OfflineAtUtc);
    }

    [Fact]
    public void ParticipantReturningAfterOfflineCreatesNewInterval()
    {
        var tracker = CreateOnlineTracker();
        tracker.Apply([], Start.AddSeconds(7));
        tracker.Apply([Observation(Start.AddSeconds(9))], Start.AddSeconds(9));
        var result = tracker.Apply([Observation(Start.AddSeconds(11))], Start.AddSeconds(11));

        Assert.Equal(2, result.Participants.Count);
        Assert.Equal(2, result.Participants.Select(participant => participant.IntervalId).Distinct().Count());
        Assert.Contains(result.Participants, participant => participant.State == MemberPresenceState.Offline);
        Assert.Contains(result.Participants, participant => participant.State == MemberPresenceState.Online);
    }

    [Fact]
    public void StopClosesEveryConfirmedOnlineInterval()
    {
        var tracker = CreateOnlineTracker();

        var result = tracker.Stop(Start.AddSeconds(12));

        var participant = Assert.Single(result.Participants);
        Assert.Equal(MemberPresenceState.Offline, participant.State);
        Assert.Equal(Start.AddSeconds(12), participant.OfflineAtUtc);
        Assert.Single(result.ChangedParticipants);
    }

    private static ParticipantPresenceTracker CreateTracker() =>
        new(CallId, 2, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

    private static ParticipantPresenceTracker CreateOnlineTracker()
    {
        var tracker = CreateTracker();
        tracker.Apply([Observation(Start)], Start);
        tracker.Apply([Observation(Start.AddSeconds(2))], Start.AddSeconds(2));
        return tracker;
    }

    private static ResolvedParticipantObservation Observation(DateTimeOffset observedAt) =>
        new(MemberId, "测试成员", DetectionSource.UiAutomation, 0.9, observedAt);
}
