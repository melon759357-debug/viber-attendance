using ViberAttendance.Core.Configuration;

namespace ViberAttendance.Tests;

public sealed class SheetSetupTransferTests
{
    [Fact]
    public void ExportLeavesOutLinksAndImportKeepsTheExistingOnes()
    {
        var source = new SheetSyncSettings
        {
            IntervalSeconds = 45,
            WriteMode = "Script",
            ServiceAccountJsonPath = @"D:\secret\key.json",
            ScriptWebAppUrl = "https://script.example/exec",
            DetailEnabled = true,
            DetailSpreadsheetUrl = "https://docs.example/live",
            DetailSheetName = "实时表",
            DetailNameColumn = "C",
            DetailOnlineColumn = "D",
            DetailOfflineColumn = "E",
            DetailStatusColumn = "F",
            DetailDurationColumn = "G",
            DetailCodeEnabled = true,
            DetailCodeColumn = "H",
            TimeEnabled = true,
            TimeSpreadsheetUrl = "https://docs.example/final",
            TimeSheetName = "最终表",
            NameColumn = "B",
            TimeColumn = "D",
            StartRow = 3,
            EndRow = 20,
            TimeCodeEnabled = true,
            TimeCodeColumn = "E",
            ActiveSchemeName = "夜班",
            Schemes =
            [
                new TimeMarkScheme
                {
                    Name = "夜班",
                    Suffix = "VB",
                    DefaultSymbol = "甲",
                    DefaultColor = "#112233",
                    PrefixMinimumMinutes = 90,
                    PrefixText = "优秀",
                    Tiers = [new TimeMarkTier { MinimumMinutes = 90, Symbol = "长档", Color = "#00FF00" }]
                }
            ],
            CustomNumbers = [new CustomNumber { Name = "张 三", Code = "18" }]
        };
        source.Normalize();

        var json = SheetSetupTransfer.Export(source);

        Assert.DoesNotContain("docs.example", json, StringComparison.Ordinal);
        Assert.DoesNotContain("script.example", json, StringComparison.Ordinal);
        Assert.DoesNotContain("key.json", json, StringComparison.Ordinal);
        Assert.Contains("实时表", json, StringComparison.Ordinal);
        Assert.Contains("夜班", json, StringComparison.Ordinal);
        Assert.Contains("18", json, StringComparison.Ordinal);

        var destination = new SheetSyncSettings
        {
            WriteMode = "Key",
            ServiceAccountJsonPath = @"D:\mine\account.json",
            ScriptWebAppUrl = "https://mine.example/exec",
            DetailSpreadsheetUrl = "https://mine.example/live",
            TimeSpreadsheetUrl = "https://mine.example/final"
        };
        destination.Normalize();

        Assert.True(SheetSetupTransfer.TryImport(destination, json, out var message));
        Assert.Equal(string.Empty, message);
        Assert.Equal("https://mine.example/live", destination.DetailSpreadsheetUrl);
        Assert.Equal("https://mine.example/final", destination.TimeSpreadsheetUrl);
        Assert.Equal("https://mine.example/exec", destination.ScriptWebAppUrl);
        Assert.Equal(@"D:\mine\account.json", destination.ServiceAccountJsonPath);
        Assert.Equal("Key", destination.WriteMode);
        Assert.Equal(45, destination.IntervalSeconds);
        Assert.True(destination.DetailEnabled);
        Assert.Equal("实时表", destination.DetailSheetName);
        Assert.Equal("C", destination.DetailNameColumn);
        Assert.Equal("H", destination.DetailCodeColumn);
        Assert.True(destination.DetailCodeEnabled);
        Assert.Equal("最终表", destination.TimeSheetName);
        Assert.Equal("B", destination.NameColumn);
        Assert.Equal(3, destination.StartRow);
        Assert.Equal("夜班", destination.ActiveSchemeName);
        Assert.Equal("长档", destination.ActiveScheme().Tiers[0].Symbol);
        Assert.Equal("优秀", destination.ActiveScheme().PrefixText);
        Assert.Equal("18", destination.CustomNumbers[0].Code);
    }

    [Fact]
    public void ImportRejectsAFileThatIsNotSheetSetup()
    {
        var settings = new SheetSyncSettings { DetailNameColumn = "C", DetailSpreadsheetUrl = "https://keep.example" };
        settings.Normalize();

        Assert.False(SheetSetupTransfer.TryImport(settings, """{"detailSpreadsheetUrl":"https://other.example"}""", out var message));

        Assert.Equal("这个文件不是表格设置。", message);
        Assert.Equal("C", settings.DetailNameColumn);
        Assert.Equal("https://keep.example", settings.DetailSpreadsheetUrl);
    }
}
