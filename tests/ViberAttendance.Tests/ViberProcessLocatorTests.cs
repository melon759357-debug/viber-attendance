using ViberAttendance.UIAutomation;

namespace ViberAttendance.Tests;

public sealed class ViberProcessLocatorTests
{
    [Fact]
    public void ProcessNameContainingViberMatchesCaseInsensitive()
    {
        Assert.True(ViberProcessLocator.MatchesCandidate(
            "viber",
            null,
            null,
            null,
            null,
            out var reasons));
        Assert.Contains("ProcessName", reasons);
    }

    [Fact]
    public void ExecutableNamedViberExeMatches()
    {
        Assert.True(ViberProcessLocator.MatchesCandidate(
            "App",
            @"C:\Program Files\Rakuten Viber\Viber.exe",
            null,
            null,
            null,
            out var reasons));
        Assert.Contains("ExecutableFileName", reasons);
        Assert.Contains("ExecutablePath", reasons);
    }

    [Fact]
    public void ProductNameOrFileDescriptionContainingViberMatches()
    {
        Assert.True(ViberProcessLocator.MatchesCandidate(
            "host",
            null,
            "Rakuten Viber",
            "Viber Desktop",
            null,
            out var reasons));
        Assert.Contains("ProductName", reasons);
        Assert.Contains("FileDescription", reasons);
    }

    [Fact]
    public void WindowTitleContainingViberMatches()
    {
        Assert.True(ViberProcessLocator.MatchesCandidate(
            "unknown",
            null,
            null,
            null,
            "Viber",
            out var reasons));
        Assert.Contains("MainWindowTitle", reasons);
    }

    [Fact]
    public void UnrelatedProcessDoesNotMatch()
    {
        Assert.False(ViberProcessLocator.MatchesCandidate(
            "chrome",
            @"C:\Program Files\Google\Chrome\chrome.exe",
            "Google Chrome",
            "Chrome",
            "New Tab",
            out var reasons));
        Assert.Empty(reasons);
    }

    [Fact]
    public void MatchCacheHitsOnlyForSameProcessStart()
    {
        var cache = new ProcessMatchCache();
        var match = new StaticProcessMatch(false, "Viber", @"C:\Viber\Viber.exe", null, null, ["ProcessName"]);
        cache.Set(42, 1_000, match);

        Assert.True(cache.TryGet(42, 1_000, out var hit));
        Assert.Same(match, hit);
        Assert.False(cache.TryGet(42, 2_000, out _));
        Assert.False(cache.TryGet(43, 1_000, out _));
    }

    [Fact]
    public void MatchCacheDropsExitedProcesses()
    {
        var cache = new ProcessMatchCache();
        cache.Set(1, 10, StaticProcessMatch.None);
        cache.Set(2, 20, StaticProcessMatch.None);

        cache.RemoveExcept(new HashSet<int> { 2 });

        Assert.Equal(1, cache.Count);
        Assert.False(cache.TryGet(1, 10, out _));
        Assert.True(cache.TryGet(2, 20, out _));
    }

    [Fact]
    public void AttendanceApplicationIsNotMistakenForViber()
    {
        Assert.False(ViberProcessLocator.MatchesCandidate(
            "ViberAttendance",
            @"D:\redownload\ruanjian\ViberAttendance\bin\ViberAttendance.exe",
            "Viber Attendance",
            "Viber online attendance",
            "Viber 在线记录",
            out var reasons));

        Assert.Empty(reasons);
    }
}
