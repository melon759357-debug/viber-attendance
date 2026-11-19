using System.Reflection;
using System.Runtime.InteropServices;
using ViberAttendance.App.Services;
using ViberAttendance.App.ViewModels;
using ViberAttendance.Core.Models;

namespace ViberAttendance.Tests;

public sealed class SimpleUiContractTests
{
    [Fact]
    public void MainViewModelDoesNotExposeWindowOrUiaDiagnostics()
    {
        var publicProperties = typeof(MainViewModel)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("ConnectionStatus", publicProperties);
        Assert.Contains("Participants", publicProperties);
        Assert.DoesNotContain(publicProperties, name =>
            name.Contains("Window", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Hwnd", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Uia", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Diagnostic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OrdinaryPagesDoNotBindTechnicalCountsAndAdvancedPageStartsHidden()
    {
        var xaml = File.ReadAllText(FindMainWindowXaml());
        var advancedStart = xaml.IndexOf("<TabItem x:Name=\"AdvancedTab\"", StringComparison.Ordinal);
        Assert.True(advancedStart > 0);
        var ordinaryXaml = xaml[..advancedStart];

        Assert.DoesNotContain("HWND", ordinaryXaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TotalHandleCount", ordinaryXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowDiagnostics", ordinaryXaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"Collapsed\"", xaml[advancedStart..], StringComparison.Ordinal);
    }

    [Fact]
    public void ParticipantListFailureUsesOnlySimpleChinesePrompt()
    {
        Assert.Equal(
            "已检测到通话，请展开参与者名单",
            UserFacingStatuses.ParticipantListNotFound);
        Assert.DoesNotContain("Exception", UserFacingStatuses.TechnicalRetry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0x", UserFacingStatuses.TechnicalRetry, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CurrentPageBindsRetryDetectionCommand()
    {
        var xaml = File.ReadAllText(FindAppFile("Views", "CurrentRecordView.xaml"));
        Assert.Contains("RetryDetectionCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("重新检测", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalInformationExistsOnlyOnAdvancedDiagnosticsViewModel()
    {
        var advancedProperties = typeof(AdvancedDiagnosticsViewModel)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        Assert.Contains("WindowDiagnostics", advancedProperties);
        Assert.Contains("RootNodes", advancedProperties);
        Assert.Contains("LastError", advancedProperties);
        Assert.Contains("RunFullDiagnosticCommand", advancedProperties);
        Assert.Contains("ExportFullDiagnosticCommand", advancedProperties);
    }

    [Fact]
    public void MainShellUsesSidebarAndContentOnlyNavigation()
    {
        var xaml = File.ReadAllText(FindAppFile("MainWindow.xaml"));

        Assert.Contains("Width=\"200\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"720\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"480\"", xaml, StringComparison.Ordinal);
        Assert.Contains("NavigationButtonStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("ContentOnlyTabControlStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedIndex=\"0\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentPageShowsUserFacingMetricsAndNoTechnicalWindowCounts()
    {
        var xaml = File.ReadAllText(FindAppFile("Views", "CurrentRecordView.xaml"));

        Assert.Contains("Viber 连接", xaml, StringComparison.Ordinal);
        Assert.Contains("当前在线", xaml, StringComparison.Ordinal);
        Assert.Contains("本场时长", xaml, StringComparison.Ordinal);
        Assert.Contains("Participants", xaml, StringComparison.Ordinal);
        Assert.Contains("EmptyStateTitle", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TotalHandleCount", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("HWND", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WindowDiagnostics", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeAndReusableControlResourcesAreMerged()
    {
        var appXaml = File.ReadAllText(FindAppFile("App.xaml"));
        var mergedResources = new[]
        {
            "LightTheme.xaml",
            "Typography.xaml",
            "Buttons.xaml",
            "Cards.xaml",
            "Inputs.xaml",
            "Navigation.xaml",
            "StatusBadges.xaml",
            "DataGrid.xaml"
        };

        foreach (var resource in mergedResources)
        {
            Assert.Contains(resource, appXaml, StringComparison.Ordinal);
            Assert.True(File.Exists(FindAppFile("Styles", resource)), $"Missing resource: {resource}");
        }

        Assert.True(File.Exists(FindAppFile("Styles", "DarkTheme.xaml")));

        var buttons = File.ReadAllText(FindAppFile("Styles", "Buttons.xaml"));
        Assert.Contains("PrimaryButtonStyle", buttons, StringComparison.Ordinal);
        Assert.Contains("SecondaryButtonStyle", buttons, StringComparison.Ordinal);
        Assert.Contains("DangerButtonStyle", buttons, StringComparison.Ordinal);
        Assert.Contains("IconButtonStyle", buttons, StringComparison.Ordinal);
        Assert.Contains("SmallActionButtonStyle", buttons, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationRegistersAStartMenuShortcutForWindowsSearch()
    {
        var appSource = File.ReadAllText(FindAppFile("App.xaml.cs"));
        var registrationSource = File.ReadAllText(
            FindAppFile("Services", "StartMenuRegistrationService.cs"));

        Assert.Contains("StartMenuRegistrationService.EnsureShortcut", appSource, StringComparison.Ordinal);
        Assert.Contains("Windows", registrationSource, StringComparison.Ordinal);
        Assert.Contains("Start Menu", registrationSource, StringComparison.Ordinal);
        Assert.Contains("Programs", registrationSource, StringComparison.Ordinal);
        Assert.Contains("Viber Attendance.lnk", registrationSource, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsShellShortcutContainsTheExecutableTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var folder = Path.Combine(Path.GetTempPath(), "ViberAttendance.Tests", Guid.NewGuid().ToString("N"));
        var shortcutPath = Path.Combine(folder, "Viber Attendance.lnk");
        Directory.CreateDirectory(folder);
        object? shell = null;
        object? shortcut = null;
        try
        {
            var createShortcut = typeof(StartMenuRegistrationService).GetMethod(
                "CreateShortcut",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(createShortcut);
            createShortcut.Invoke(null, [shortcutPath, Environment.ProcessPath!]);

            Assert.True(File.Exists(shortcutPath));
            var shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!;
            shell = Activator.CreateInstance(shellType);
            Assert.NotNull(shell);
            dynamic dynamicShell = shell;
            shortcut = dynamicShell.CreateShortcut(shortcutPath);
            dynamic dynamicShortcut = shortcut;
            Assert.Equal(Environment.ProcessPath, (string)dynamicShortcut.TargetPath);
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }

    private static string FindMainWindowXaml()
    {
        return FindAppFile("MainWindow.xaml");
    }

    private static string FindAppFile(params string[] parts)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(new[] { root, "src", "ViberAttendance.App" }.Concat(parts).ToArray());
    }
}
