using System.Text.Encodings.Web;
using System.Text.Json;

namespace ViberAttendance.Core.Configuration;

public static class SheetSetupTransfer
{
    public const string Kind = "viber-attendance-sheet-setup";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Export(SheetSyncSettings settings)
    {
        var document = new SheetSetupDocument
        {
            Kind = Kind,
            IntervalSeconds = settings.IntervalSeconds,
            Live = new LiveSheetSetup
            {
                Enabled = settings.DetailEnabled,
                SheetName = settings.DetailSheetName,
                NameColumn = settings.DetailNameColumn,
                OnlineColumn = settings.DetailOnlineColumn,
                OfflineColumn = settings.DetailOfflineColumn,
                StatusColumn = settings.DetailStatusColumn,
                DurationColumn = settings.DetailDurationColumn,
                ExportCustomNumber = settings.DetailCodeEnabled,
                CustomNumberColumn = settings.DetailCodeColumn
            },
            Final = new FinalSheetSetup
            {
                Enabled = settings.TimeEnabled,
                SheetName = settings.TimeSheetName,
                NameColumn = settings.NameColumn,
                TimeColumn = settings.TimeColumn,
                StartRow = settings.StartRow,
                EndRow = settings.EndRow,
                ExportCustomNumber = settings.TimeCodeEnabled,
                CustomNumberColumn = settings.TimeCodeColumn,
                NoteColumn = settings.NoteColumn,
                ActiveSchemeName = settings.ActiveSchemeName,
                Schemes = settings.Schemes.Select(CopyScheme).ToList()
            },
            CustomNumbers = settings.CustomNumbers
                .Select(item => new CustomNumber { Name = item.Name, Code = item.Code })
                .ToList()
        };
        return JsonSerializer.Serialize(document, Json);
    }

    public static bool TryImport(SheetSyncSettings settings, string json, out string message)
    {
        SheetSetupDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<SheetSetupDocument>(json, Json);
        }
        catch (JsonException)
        {
            message = "这个文件不是表格设置。";
            return false;
        }

        if (document is null || document.Kind != Kind || document.Live is null || document.Final is null)
        {
            message = "这个文件不是表格设置。";
            return false;
        }

        var detailUrl = settings.DetailSpreadsheetUrl;
        var timeUrl = settings.TimeSpreadsheetUrl;
        var scriptUrl = settings.ScriptWebAppUrl;
        var keyPath = settings.ServiceAccountJsonPath;
        var writeMode = settings.WriteMode;

        settings.IntervalSeconds = document.IntervalSeconds;
        settings.DetailEnabled = document.Live.Enabled;
        settings.DetailSheetName = document.Live.SheetName ?? string.Empty;
        settings.DetailNameColumn = document.Live.NameColumn ?? string.Empty;
        settings.DetailOnlineColumn = document.Live.OnlineColumn ?? string.Empty;
        settings.DetailOfflineColumn = document.Live.OfflineColumn ?? string.Empty;
        settings.DetailStatusColumn = document.Live.StatusColumn ?? string.Empty;
        settings.DetailDurationColumn = document.Live.DurationColumn ?? string.Empty;
        settings.DetailCodeEnabled = document.Live.ExportCustomNumber;
        settings.DetailCodeColumn = document.Live.CustomNumberColumn ?? string.Empty;
        settings.TimeEnabled = document.Final.Enabled;
        settings.TimeSheetName = document.Final.SheetName ?? string.Empty;
        settings.NameColumn = document.Final.NameColumn ?? string.Empty;
        settings.TimeColumn = document.Final.TimeColumn ?? string.Empty;
        settings.StartRow = document.Final.StartRow;
        settings.EndRow = document.Final.EndRow;
        settings.TimeCodeEnabled = document.Final.ExportCustomNumber;
        settings.TimeCodeColumn = document.Final.CustomNumberColumn ?? string.Empty;
        settings.NoteColumn = document.Final.NoteColumn ?? string.Empty;
        settings.ActiveSchemeName = document.Final.ActiveSchemeName ?? string.Empty;
        settings.Schemes = (document.Final.Schemes ?? []).Select(CopyScheme).ToList();
        settings.CustomNumbers = (document.CustomNumbers ?? [])
            .Select(item => new CustomNumber { Name = item.Name ?? string.Empty, Code = item.Code ?? string.Empty })
            .ToList();

        settings.DetailSpreadsheetUrl = detailUrl;
        settings.TimeSpreadsheetUrl = timeUrl;
        settings.ScriptWebAppUrl = scriptUrl;
        settings.ServiceAccountJsonPath = keyPath;
        settings.WriteMode = writeMode;
        settings.Normalize();
        settings.NotifyAll();
        message = string.Empty;
        return true;
    }

    private static TimeMarkScheme CopyScheme(TimeMarkScheme scheme) => new()
    {
        Name = scheme.Name,
        Suffix = scheme.Suffix,
        DefaultSymbol = scheme.DefaultSymbol,
        DefaultColor = scheme.DefaultColor,
        PrefixMinimumMinutes = scheme.PrefixMinimumMinutes,
        PrefixText = scheme.PrefixText,
        Tiers = (scheme.Tiers ?? []).Select(tier => new TimeMarkTier
        {
            MinimumMinutes = tier.MinimumMinutes,
            Symbol = tier.Symbol,
            Color = tier.Color
        }).ToList()
    };
}

public sealed class SheetSetupDocument
{
    public string Kind { get; set; } = SheetSetupTransfer.Kind;
    public double IntervalSeconds { get; set; } = 30;
    public LiveSheetSetup? Live { get; set; }
    public FinalSheetSetup? Final { get; set; }
    public List<CustomNumber> CustomNumbers { get; set; } = [];
}

public sealed class LiveSheetSetup
{
    public bool Enabled { get; set; }
    public string SheetName { get; set; } = string.Empty;
    public string NameColumn { get; set; } = "A";
    public string OnlineColumn { get; set; } = "B";
    public string OfflineColumn { get; set; } = "C";
    public string StatusColumn { get; set; } = "D";
    public string DurationColumn { get; set; } = "E";
    public bool ExportCustomNumber { get; set; }
    public string CustomNumberColumn { get; set; } = "F";
}

public sealed class FinalSheetSetup
{
    public bool Enabled { get; set; }
    public string SheetName { get; set; } = string.Empty;
    public string NameColumn { get; set; } = "A";
    public string TimeColumn { get; set; } = "B";
    public int StartRow { get; set; } = 2;
    public int EndRow { get; set; } = 12;
    public bool ExportCustomNumber { get; set; }
    public string CustomNumberColumn { get; set; } = "C";
    public string NoteColumn { get; set; } = "C";
    public string ActiveSchemeName { get; set; } = "默认";
    public List<TimeMarkScheme> Schemes { get; set; } = [];
}
