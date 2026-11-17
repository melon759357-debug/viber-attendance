namespace ViberAttendance.UIAutomation;

/// <summary>进程路径、文件版本信息在进程生命周期内不变，按 PID + 启动时间缓存识别结果。</summary>
internal sealed class ProcessMatchCache
{
    internal const long UnknownStartTime = -1;

    private readonly Dictionary<int, (long StartTicks, StaticProcessMatch Match)> _entries = [];

    internal int Count => _entries.Count;

    internal bool TryGet(int processId, long startTicks, out StaticProcessMatch match)
    {
        if (_entries.TryGetValue(processId, out var entry) && entry.StartTicks == startTicks)
        {
            match = entry.Match;
            return true;
        }

        match = StaticProcessMatch.None;
        return false;
    }

    internal void Set(int processId, long startTicks, StaticProcessMatch match) =>
        _entries[processId] = (startTicks, match);

    internal void RemoveExcept(IReadOnlySet<int> liveProcessIds)
    {
        foreach (var processId in _entries.Keys.Where(id => !liveProcessIds.Contains(id)).ToArray())
        {
            _entries.Remove(processId);
        }
    }
}

internal sealed record StaticProcessMatch(
    bool IsExcluded,
    string ProcessName,
    string? ExecutablePath,
    string? ProductName,
    string? FileDescription,
    IReadOnlyList<string> MatchReasons)
{
    internal static StaticProcessMatch None { get; } = new(true, string.Empty, null, null, null, []);
}
