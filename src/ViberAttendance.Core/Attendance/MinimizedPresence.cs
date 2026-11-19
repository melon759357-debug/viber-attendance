using ViberAttendance.Core.Models;

namespace ViberAttendance.Core.Attendance;

public static class MinimizedPresence
{
    public static IReadOnlyList<ResolvedParticipantObservation> KeepKnownPeople(
        IReadOnlyList<ResolvedParticipantObservation> known,
        IReadOnlyList<ResolvedParticipantObservation> seenNow,
        DateTimeOffset now)
    {
        var merged = new Dictionary<Guid, ResolvedParticipantObservation>();
        foreach (var person in known)
        {
            merged[person.MemberId] = person with { ObservedAtUtc = now };
        }

        foreach (var person in seenNow)
        {
            merged[person.MemberId] = person;
        }

        return merged.Values.ToArray();
    }
}
