using ViberAttendance.App.Services;
using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Contracts;
using ViberAttendance.Core.Sheets;
using ViberAttendance.Infrastructure;

namespace ViberAttendance.Tests;

public sealed class SecurityHardeningTests
{
    [Fact]
    public void ScriptAddressMustStayOnGoogleAppsScript()
    {
        Assert.True(ScriptSheetProtocol.IsScriptExecUrl("https://script.google.com/macros/s/abc/exec"));
        Assert.False(ScriptSheetProtocol.IsScriptExecUrl("https://script.google.com.evil.example/exec"));
        Assert.False(ScriptSheetProtocol.IsScriptExecUrl("https://evil.example/macros/s/abc/exec"));
        Assert.False(ScriptSheetProtocol.IsScriptExecUrl("http://script.google.com/macros/s/abc/exec"));
    }

    [Fact]
    public void FormulaTextIsForcedToPlainTextForTheScript()
    {
        Assert.Equal("'=HYPERLINK(\"http://evil.example\")", ScriptSheetProtocol.AsPlainText("=HYPERLINK(\"http://evil.example\")"));
        Assert.Equal("+15551212", ScriptSheetProtocol.AsPlainText("+15551212"));
        Assert.Contains("asPlainText_", ScriptSheetProtocol.AppsScript, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateDownloadRejectsAForeignHost()
    {
        var chosen = AppRelease.ChooseDownload(
        [
            ("viber-attendance-1.1.17.zip", "https://evil.example/viber-attendance-1.1.17.zip", "sha256:" + new string('a', 64)),
            ("viber-attendance-1.1.17.zip", "https://github.com/melon759357-debug/viber-attendance/releases/download/v1.1.17/viber-attendance-1.1.17.zip", "sha256:" + new string('b', 64))
        ]);

        Assert.NotNull(chosen);
        Assert.StartsWith("https://github.com/", chosen.Value.Url, StringComparison.Ordinal);
        Assert.False(AppRelease.IsGitHubUpdateHost(new Uri("https://evil.example/payload.exe")));
    }

    [Fact]
    public async Task DiagnosticLogDropsUrlsAndPrivateKeys()
    {
        var root = Path.Combine(Path.GetTempPath(), "va-audit-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = ApplicationPaths.ForRoot(root);
            var settings = new AppSettings { SaveDiagnosticLogs = true };
            var logger = new PrivacyFileLogger(paths, new FixedClock(), settings);
            await logger.WriteAsync(
                DiagnosticLogLevel.Warning,
                "sheet.failed",
                "open https://docs.google.com/spreadsheets/d/abc/edit \"private_key\":\"secret-key-material\"");
            logger.Dispose();

            var text = await File.ReadAllTextAsync(Directory.GetFiles(paths.LogsFolder).Single());
            Assert.DoesNotContain("docs.google.com", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret-key-material", text, StringComparison.Ordinal);
            Assert.Contains("redacted-url", text, StringComparison.Ordinal);
            Assert.Contains("redacted-key", text, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);

        public long GetMonotonicTimestamp() => 0;

        public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp) => TimeSpan.Zero;
    }
}
