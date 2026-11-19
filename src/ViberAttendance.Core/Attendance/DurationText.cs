using System.Globalization;

namespace ViberAttendance.Core.Attendance;

public static class DurationText
{
    public static string Format(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        var hours = (int)Math.Floor(value.TotalHours);
        var minutes = (int)Math.Floor(value.TotalMinutes);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours:00}:{value.Minutes:00}:{value.Seconds:00}（{minutes}分钟）");
    }
}
