using System.Diagnostics;
using ViberAttendance.Core.Contracts;

namespace ViberAttendance.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public long GetMonotonicTimestamp() => Stopwatch.GetTimestamp();

    public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp) =>
        Stopwatch.GetElapsedTime(startTimestamp, endTimestamp);
}

