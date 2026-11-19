using System.Globalization;
using ViberAttendance.Core.Attendance;
using ViberAttendance.Core.Models;

namespace ViberAttendance.Tests;

public sealed class DurationAndMinimizedPresenceTests
{
    [Fact]
    public void DurationKeepsTheClockAndAddsWholeMinutes()
    {
        var text = DurationText.Format(TimeSpan.FromMinutes(62) + TimeSpan.FromSeconds(5));

        Assert.Contains("01:02:05", text, StringComparison.Ordinal);
        Assert.Contains("62分钟", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MinimizedReadKeepsPeopleWhoDisappearedFromTheTree()
    {
        var now = DateTimeOffset.Parse("2026-11-19T12:00:00Z", CultureInfo.InvariantCulture);
        var kept = new ResolvedParticipantObservation(Guid.NewGuid(), "甲", DetectionSource.UiAutomation, 0.9, now.AddMinutes(-5));
        var seen = new ResolvedParticipantObservation(Guid.NewGuid(), "乙", DetectionSource.UiAutomation, 0.9, now);

        var merged = MinimizedPresence.KeepKnownPeople([kept], [seen], now);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, person => person.DisplayName == "甲" && person.ObservedAtUtc == now);
        Assert.Contains(merged, person => person.DisplayName == "乙");
    }
}
