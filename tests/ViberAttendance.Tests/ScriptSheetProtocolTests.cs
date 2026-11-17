using ViberAttendance.App.Services;
using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Sheets;

namespace ViberAttendance.Tests;

public sealed class ScriptSheetProtocolTests
{
    [Fact]
    public void ReadResponseBecomesGridRows()
    {
        var rows = ScriptSheetProtocol.ReadRows("""
            {"ok":true,"rows":[["姓名","上线"],["甲",""]]}
            """);

        Assert.Equal("姓名", rows[0][0]);
        Assert.Equal(string.Empty, rows[1][1]);
    }

    [Fact]
    public void ScriptErrorDoesNotKeepTheAddress()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ScriptSheetProtocol.ReadRows("""
                {"ok":false,"error":"打不开 https://docs.google.com/spreadsheets/d/abc/edit"}
                """));

        Assert.DoesNotContain("docs.google.com", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted-url>", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectLinkReadRejectsTheLoginPage()
    {
        Assert.False(LinkSheetProtocol.LooksLikeGrid("<html><a href=\"https://accounts.google.com/ServiceLogin\">"));
        Assert.True(LinkSheetProtocol.LooksLikeGrid("姓名,上线\r\n甲,"));
    }

    [Fact]
    public void DirectLinkProbeDescribesARefusalWithoutTheAddress()
    {
        var message = LinkSheetProtocol.Describe(opened: true, writeStatus: 401);

        Assert.Contains("拒绝了未登录写入", message, StringComparison.Ordinal);
        Assert.DoesNotContain("http", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("docs.google.com", LinkSheetProtocol.CreateValueBody("Sheet1", [new SheetCell(2, 0, "甲")]), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriteModeKeepsTheLinkChoice()
    {
        var settings = new SheetSyncSettings { WriteMode = "link" };
        settings.Normalize();

        Assert.Equal("Link", settings.WriteMode);
    }

    [Fact]
    public void WriteModeKeepsOnlyTheTwoChoices()
    {
        var settings = new SheetSyncSettings { WriteMode = "其他", ScriptWebAppUrl = "  https://script.google.com/macros/s/test/exec  " };
        settings.Normalize();

        Assert.Equal("Key", settings.WriteMode);
        Assert.Equal("https://script.google.com/macros/s/test/exec", settings.ScriptWebAppUrl);
    }

    [Fact]
    public void WriteRequestUsesSheetCoordinates()
    {
        var json = ScriptSheetProtocol.CreateWrite(
            "https://docs.google.com/spreadsheets/d/example/edit",
            "Sheet1",
            [new SheetCell(2, 0, "甲")],
            [new SheetColor(2, 1, "#43A047")]);

        Assert.Contains("\"action\":\"write\"", json, StringComparison.Ordinal);
        Assert.Contains("\"row\":2", json, StringComparison.Ordinal);
        Assert.Contains("\"column\":0", json, StringComparison.Ordinal);
        Assert.Contains("#43A047", json, StringComparison.Ordinal);
    }
}
