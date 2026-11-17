using ViberAttendance.Infrastructure;
using ViberAttendance.Core.Configuration;

namespace ViberAttendance.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task SaveAndLoadRoundTripsSettingsAndNormalizesUnsafeIntervals()
    {
        var root = CreateTemporaryFolder();
        try
        {
            var paths = ApplicationPaths.ForRoot(root);
            var store = new JsonSettingsStore(paths);
            var settings = await store.LoadAsync();
            settings.FastScanSeconds = 0;
            settings.UiaMaxNodes = 10;
            settings.IncludeSensitiveUiTextInExport = true;

            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();

            Assert.Equal(0.5, loaded.FastScanSeconds);
            Assert.Equal(100, loaded.UiaMaxNodes);
            Assert.True(loaded.IncludeSensitiveUiTextInExport);
            Assert.Equal(paths.DefaultDatabaseFile, loaded.DatabasePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadCorruptJsonPreservesBackupAndReturnsDefaults()
    {
        var root = CreateTemporaryFolder();
        try
        {
            var paths = ApplicationPaths.ForRoot(root);
            paths.EnsureDirectories();
            await File.WriteAllTextAsync(paths.SettingsFile, "{ invalid json");

            var settings = await new JsonSettingsStore(paths).LoadAsync();

            Assert.Equal(2, settings.FastScanSeconds);
            Assert.Single(Directory.GetFiles(root, "settings.json.corrupt-*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ClearDiagnosticLogsDeletesOnlyDiagnosticJsonLines()
    {
        var root = CreateTemporaryFolder();
        try
        {
            var paths = ApplicationPaths.ForRoot(root);
            paths.EnsureDirectories();
            var diagnostic = Path.Combine(paths.LogsFolder, "diagnostic-20260717.jsonl");
            var unrelated = Path.Combine(paths.LogsFolder, "keep.txt");
            await File.WriteAllTextAsync(diagnostic, "test");
            await File.WriteAllTextAsync(unrelated, "keep");

            var count = await new DiagnosticLogMaintenance(paths).ClearAsync();

            Assert.Equal(1, count);
            Assert.False(File.Exists(diagnostic));
            Assert.True(File.Exists(unrelated));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("System", "System")]
    [InlineData("Dark", "Dark")]
    [InlineData("Light", "Light")]
    [InlineData("跟随系统", "System")]
    public void ThemeNormalizationSupportsSystemLightAndDark(string input, string expected)
    {
        var settings = new AppSettings { UiTheme = input };

        settings.Normalize("attendance.db", "Exports");

        Assert.Equal(expected, settings.UiTheme);
    }

    private static string CreateTemporaryFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "ViberAttendance.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
