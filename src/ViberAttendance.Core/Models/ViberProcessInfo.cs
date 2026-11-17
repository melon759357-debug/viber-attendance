namespace ViberAttendance.Core.Models;

public sealed record ViberProcessInfo(
    int ProcessId,
    string ProcessName,
    string? ExecutablePath,
    string? ProductName,
    string? FileDescription,
    long MainWindowHandle,
    string MainWindowTitle,
    IReadOnlyList<string> MatchReasons)
{
    public string DisplayLabel =>
        $"{ProcessName} · PID {ProcessId} · {ExecutablePath ?? "（路径不可读）"}";
}
