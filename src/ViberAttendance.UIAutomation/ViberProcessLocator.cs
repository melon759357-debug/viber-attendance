using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using ViberAttendance.Core.Contracts;
using ViberAttendance.Core.Models;

namespace ViberAttendance.UIAutomation;

/// <summary>综合进程名、路径、文件版本信息与窗口标题匹配 Viber 候选进程。</summary>
public sealed class ViberProcessLocator : IViberProcessLocator
{
    private readonly ProcessMatchCache _cache = new();
    private readonly object _cacheGate = new();

    public IReadOnlyList<ViberProcessInfo> FindCandidates(CancellationToken cancellationToken = default)
    {
        var results = new List<ViberProcessInfo>();
        var liveProcessIds = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    liveProcessIds.Add(process.Id);
                    if (TryMatchCached(process, out var info) && info is not null)
                    {
                        results.Add(info);
                    }
                }
                catch (InvalidOperationException)
                {
                    // 进程在枚举期间退出，跳过该进程继续。
                }
                catch (Win32Exception)
                {
                    // 无权访问的系统进程，跳过该进程继续。
                }
                catch (Exception)
                {
                    // 单个进程属性读取失败不得中断整轮检测。
                }
            }
        }

        lock (_cacheGate)
        {
            _cache.RemoveExcept(liveProcessIds);
        }

        return results
            .OrderBy(process => process.ProcessId)
            .ToArray();
    }

    private bool TryMatchCached(Process process, out ViberProcessInfo? info)
    {
        info = null;
        if (process.Id == Environment.ProcessId)
        {
            // 本程序的名称、安装路径和产品信息均含 ViberAttendance；绝不能把自己当 Viber。
            return false;
        }

        var startTicks = ReadStartTicks(process);
        StaticProcessMatch match;
        bool cached;
        lock (_cacheGate)
        {
            cached = _cache.TryGet(process.Id, startTicks, out match);
        }

        if (!cached)
        {
            match = ReadStaticMatch(process);
            lock (_cacheGate)
            {
                _cache.Set(process.Id, startTicks, match);
            }
        }

        if (match.IsExcluded)
        {
            return false;
        }

        var matchReasons = new List<string>(match.MatchReasons);
        long mainWindowHandle = 0;
        var mainWindowTitle = string.Empty;
        if (matchReasons.Count > 0 || !cached)
        {
            try
            {
                mainWindowHandle = process.MainWindowHandle.ToInt64();
                mainWindowTitle = process.MainWindowTitle ?? string.Empty;
                if (mainWindowTitle.Contains("Viber", StringComparison.OrdinalIgnoreCase))
                {
                    matchReasons.Add("MainWindowTitle");
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }

        if (matchReasons.Count == 0)
        {
            return false;
        }

        info = new ViberProcessInfo(
            process.Id,
            match.ProcessName,
            match.ExecutablePath,
            match.ProductName,
            match.FileDescription,
            mainWindowHandle,
            mainWindowTitle,
            matchReasons);
        return true;
    }

    private static long ReadStartTicks(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch (Win32Exception)
        {
            return ProcessMatchCache.UnknownStartTime;
        }
        catch (InvalidOperationException)
        {
            return ProcessMatchCache.UnknownStartTime;
        }
        catch (NotSupportedException)
        {
            return ProcessMatchCache.UnknownStartTime;
        }
    }

    private static StaticProcessMatch ReadStaticMatch(Process process)
    {
        var matchReasons = new List<string>();
        string processName;
        try
        {
            processName = process.ProcessName;
        }
        catch (InvalidOperationException)
        {
            return StaticProcessMatch.None;
        }
        catch (Win32Exception)
        {
            return StaticProcessMatch.None;
        }

        if (IsAttendanceHost(processName, null, null, null))
        {
            return StaticProcessMatch.None;
        }

        if (processName.Contains("Viber", StringComparison.OrdinalIgnoreCase))
        {
            matchReasons.Add("ProcessName");
        }

        string? executablePath = null;
        string? productName = null;
        string? fileDescription = null;
        try
        {
            executablePath = SafeReadExecutablePath(process);
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                var fileName = Path.GetFileName(executablePath);
                if (fileName.Equals("Viber.exe", StringComparison.OrdinalIgnoreCase))
                {
                    matchReasons.Add("ExecutableFileName");
                }

                if (executablePath.Contains("Viber", StringComparison.OrdinalIgnoreCase) ||
                    executablePath.Contains("Rakuten Viber", StringComparison.OrdinalIgnoreCase))
                {
                    matchReasons.Add("ExecutablePath");
                }

                try
                {
                    var version = FileVersionInfo.GetVersionInfo(executablePath);
                    productName = version.ProductName;
                    fileDescription = version.FileDescription;
                    if (IsAttendanceHost(processName, executablePath, productName, fileDescription))
                    {
                        return StaticProcessMatch.None;
                    }

                    if (!string.IsNullOrWhiteSpace(productName) &&
                        productName.Contains("Viber", StringComparison.OrdinalIgnoreCase))
                    {
                        matchReasons.Add("ProductName");
                    }

                    if (!string.IsNullOrWhiteSpace(fileDescription) &&
                        fileDescription.Contains("Viber", StringComparison.OrdinalIgnoreCase))
                    {
                        matchReasons.Add("FileDescription");
                    }
                }
                catch (FileNotFoundException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
                catch (Win32Exception)
                {
                }
            }
        }
        catch (Win32Exception)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }

        return new StaticProcessMatch(
            false,
            processName,
            executablePath,
            productName,
            fileDescription,
            matchReasons);
    }

    /// <summary>供单元测试使用的纯匹配逻辑，不依赖真实进程句柄。</summary>
    internal static bool MatchesCandidate(
        string processName,
        string? executablePath,
        string? productName,
        string? fileDescription,
        string? windowTitle,
        out IReadOnlyList<string> matchReasons)
    {
        var reasons = new List<string>();
        if (IsAttendanceHost(processName, executablePath, productName, fileDescription))
        {
            matchReasons = reasons;
            return false;
        }

        if (processName.Contains("Viber", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("ProcessName");
        }

        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var fileName = Path.GetFileName(executablePath);
            if (fileName.Equals("Viber.exe", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("ExecutableFileName");
            }

            if (executablePath.Contains("Viber", StringComparison.OrdinalIgnoreCase) ||
                executablePath.Contains("Rakuten Viber", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("ExecutablePath");
            }
        }

        if (!string.IsNullOrWhiteSpace(productName) &&
            productName.Contains("Viber", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("ProductName");
        }

        if (!string.IsNullOrWhiteSpace(fileDescription) &&
            fileDescription.Contains("Viber", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("FileDescription");
        }

        if (!string.IsNullOrWhiteSpace(windowTitle) &&
            windowTitle.Contains("Viber", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("MainWindowTitle");
        }

        matchReasons = reasons;
        return reasons.Count > 0;
    }

    private static bool IsAttendanceHost(
        string processName,
        string? executablePath,
        string? productName,
        string? fileDescription)
    {
        if (processName.Equals("ViberAttendance", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("Viber Attendance", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var executableName = string.IsNullOrWhiteSpace(executablePath)
            ? string.Empty
            : Path.GetFileNameWithoutExtension(executablePath);
        if (executableName.Equals("ViberAttendance", StringComparison.OrdinalIgnoreCase) ||
            executableName.Equals("Viber Attendance", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ContainsAttendanceProductMarker(productName) ||
               ContainsAttendanceProductMarker(fileDescription);
    }

    private static bool ContainsAttendanceProductMarker(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.Contains("ViberAttendance", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("Viber Attendance", StringComparison.OrdinalIgnoreCase));

    private static string? SafeReadExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
