using System.Globalization;
using ViberAttendance.Core.Models;
using ViberAttendance.UIAutomation;

namespace ViberAttendance.Tests;

public sealed class UiAutomationTreeExporterTests
{
    [Fact]
    public async Task ExportDefaultPrivacyModeRedactsUiTextButKeepsStructureAndProperties()
    {
        var folder = CreateTemporaryFolder();
        try
        {
            var scan = CreateScan();
            var path = Path.Combine(folder, "tree.json");

            var result = await new UiAutomationTreeExporter().ExportAsync(scan, path, includeSensitiveUiText: false);
            var json = await File.ReadAllTextAsync(result.JsonPath);
            var text = await File.ReadAllTextAsync(result.TextPath);

            Assert.DoesNotContain("私人聊天标题", json);
            Assert.DoesNotContain("张三的私密消息", json);
            Assert.DoesNotContain("张三的私密消息", text);
            Assert.Contains("redacted", json);
            Assert.Contains("participant-row", json);
            Assert.Contains("0/0", text);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ExportExplicitSensitiveModeIncludesLocalUiText()
    {
        var folder = CreateTemporaryFolder();
        try
        {
            var result = await new UiAutomationTreeExporter().ExportAsync(
                CreateScan(),
                Path.Combine(folder, "sensitive.json"),
                includeSensitiveUiText: true);
            var json = await File.ReadAllTextAsync(result.JsonPath);

            Assert.Contains("私人聊天标题", json);
            Assert.Contains("张三的私密消息", json);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static UiAutomationScanResult CreateScan()
    {
        var root = new UiAutomationNode
        {
            Path = "0",
            RuntimeId = "42.1",
            AutomationId = "root",
            Name = "私人聊天标题",
            ControlType = "ControlType.Window",
            LocalizedControlType = "窗口",
            ClassName = "QtWindow",
            FrameworkId = "Qt",
            HelpText = string.Empty,
            AccessKey = string.Empty,
            AcceleratorKey = string.Empty,
            ProcessId = 42,
            NativeWindowHandle = 100,
            IsEnabled = true,
            IsOffscreen = false,
            IsKeyboardFocusable = true,
            BoundingRectangle = new PixelRect(10, 20, 800, 600),
            SupportedPatterns = ["WindowPatternIdentifiers.Pattern"]
        };
        root.Children.Add(new UiAutomationNode
        {
            Path = "0/0",
            RuntimeId = "42.2",
            AutomationId = "participant-row",
            Name = "张三的私密消息",
            ControlType = "ControlType.Text",
            LocalizedControlType = "文本",
            ClassName = "TextBlock",
            FrameworkId = "Qt",
            HelpText = "敏感帮助文字",
            AccessKey = string.Empty,
            AcceleratorKey = string.Empty,
            ProcessId = 42,
            NativeWindowHandle = 0,
            IsEnabled = true,
            IsOffscreen = false,
            IsKeyboardFocusable = false,
            BoundingRectangle = new PixelRect(20, 40, 200, 30)
        });

        var window = new ViberWindowCandidate(
            42,
            "Viber",
            100,
            "私人聊天标题",
            "QtWindow",
            new PixelRect(10, 20, 800, 600),
            true,
            false);
        return new UiAutomationScanResult(
            DateTimeOffset.Parse("2026-07-17T10:00:00Z", CultureInfo.InvariantCulture),
            window,
            root,
            2,
            false,
            TimeSpan.FromMilliseconds(12),
            []);
    }

    private static string CreateTemporaryFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "ViberAttendance.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
