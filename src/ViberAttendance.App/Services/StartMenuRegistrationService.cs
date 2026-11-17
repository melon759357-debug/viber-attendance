using System.IO;
using System.Runtime.InteropServices;

namespace ViberAttendance.App.Services;

public static class StartMenuRegistrationService
{
    private const string ShortcutFileName = "Viber Attendance.lnk";

    public static bool EnsureShortcut()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(executablePath) ||
                string.IsNullOrWhiteSpace(applicationData) ||
                !File.Exists(executablePath))
            {
                return false;
            }

            var programsFolder = Path.Combine(
                applicationData,
                "Microsoft",
                "Windows",
                "Start Menu",
                "Programs");
            Directory.CreateDirectory(programsFolder);
            var shortcutPath = Path.Combine(programsFolder, ShortcutFileName);
            CreateShortcut(shortcutPath, executablePath);
            Shell32.SHChangeNotify(
                Shell32.ShellChangeEventCreate,
                Shell32.ShellChangeNotifyPath,
                shortcutPath,
                null);
            TryDeleteDiagnostic();
            return true;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"开始菜单快捷方式创建失败: {exception}");
            TryWriteDiagnostic(exception);
            return false;
        }
    }

    private static void CreateShortcut(string shortcutPath, string executablePath)
    {
        object? shellObject = null;
        object? shortcutObject = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!;
            shellObject = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("无法创建 Windows Shell 对象。");
            dynamic shell = shellObject;
            shortcutObject = shell.CreateShortcut(shortcutPath);
            dynamic shortcut = shortcutObject
                ?? throw new InvalidOperationException("无法创建开始菜单快捷方式。");
            shortcut.TargetPath = executablePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(executablePath)!;
            shortcut.IconLocation = $"{executablePath},0";
            shortcut.Description = "Viber 在线记录 - 记录通话参与者的在线时间";
            shortcut.WindowStyle = 1;
            shortcut.Save();
        }
        finally
        {
            ReleaseComObject(shortcutObject);
            ReleaseComObject(shellObject);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }

    private static void TryWriteDiagnostic(Exception exception)
    {
        try
        {
            var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logsFolder = Path.Combine(localApplicationData, "ViberAttendance", "Logs");
            Directory.CreateDirectory(logsFolder);
            File.WriteAllText(
                Path.Combine(logsFolder, "start-menu-registration-error.log"),
                $"{DateTimeOffset.UtcNow:O}{Environment.NewLine}{exception}");
        }
        catch
        {
            // The shortcut is optional; diagnostic logging must not block startup either.
        }
    }

    private static void TryDeleteDiagnostic()
    {
        try
        {
            var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var path = Path.Combine(
                localApplicationData,
                "ViberAttendance",
                "Logs",
                "start-menu-registration-error.log");
            File.Delete(path);
        }
        catch
        {
            // Stale diagnostics do not affect shortcut registration.
        }
    }

    private static class Shell32
    {
        internal const uint ShellChangeEventCreate = 0x00000002;
        internal const uint ShellChangeNotifyPath = 0x0005;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern void SHChangeNotify(
            uint eventId,
            uint flags,
            string item1,
            string? item2);
    }

}
