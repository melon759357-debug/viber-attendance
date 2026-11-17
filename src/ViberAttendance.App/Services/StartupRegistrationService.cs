using Microsoft.Win32;

namespace ViberAttendance.App.Services;

public static class StartupRegistrationService
{
    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            writable: true);
        if (enabled)
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定程序路径。");
            key.SetValue("ViberAttendance", $"\"{executable}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue("ViberAttendance", throwOnMissingValue: false);
        }
    }
}
