using System.Globalization;
using System.Text.RegularExpressions;
using ViberAttendance.Core.Sheets;

namespace ViberAttendance.Core.Configuration;

public sealed class SheetSyncSettings
{
    public bool Enabled { get; set; }
    public double IntervalSeconds { get; set; } = 30;
    public string ServiceAccountJsonPath { get; set; } = string.Empty;
    public string WriteMode { get; set; } = "Key";
    public string ScriptWebAppUrl { get; set; } = string.Empty;

    public bool DetailEnabled { get; set; }
    public string DetailSpreadsheetUrl { get; set; } = string.Empty;
    public string DetailSheetName { get; set; } = string.Empty;
    public string DetailNameColumn { get; set; } = "A";
    public string DetailOnlineColumn { get; set; } = "B";
    public string DetailOfflineColumn { get; set; } = "C";
    public string DetailStatusColumn { get; set; } = "D";
    public string DetailDurationColumn { get; set; } = "E";
    public bool DetailCodeEnabled { get; set; }
    public string DetailCodeColumn { get; set; } = "F";

    public bool TimeEnabled { get; set; }
    public string TimeSpreadsheetUrl { get; set; } = string.Empty;
    public string TimeSheetName { get; set; } = string.Empty;
    public string NameColumn { get; set; } = "A";
    public string TimeColumn { get; set; } = "B";
    public bool TimeCodeEnabled { get; set; }
    public string TimeCodeColumn { get; set; } = "C";
    public string NoteColumn { get; set; } = "C";
    public List<CustomNumber> CustomNumbers { get; set; } = [];
    public int StartRow { get; set; } = 2;
    public int EndRow { get; set; } = 12;
    public string ActiveSchemeName { get; set; } = "默认";
    public List<TimeMarkScheme> Schemes { get; set; } = [TimeMarkScheme.CreateDefault()];

    public void Normalize()
    {
        IntervalSeconds = Math.Clamp(IntervalSeconds, 10, 3_600);
        ServiceAccountJsonPath = ServiceAccountJsonPath.Trim();
        WriteMode = WriteMode.Equals("Script", StringComparison.OrdinalIgnoreCase) ? "Script"
            : WriteMode.Equals("Link", StringComparison.OrdinalIgnoreCase) ? "Link"
            : "Key";
        ScriptWebAppUrl = ScriptWebAppUrl.Trim();
        DetailSpreadsheetUrl = DetailSpreadsheetUrl.Trim();
        DetailSheetName = DetailSheetName.Trim();
        DetailNameColumn = NormalizeColumn(DetailNameColumn, "A");
        DetailOnlineColumn = NormalizeColumn(DetailOnlineColumn, "B");
        DetailOfflineColumn = NormalizeColumn(DetailOfflineColumn, "C");
        DetailStatusColumn = NormalizeColumn(DetailStatusColumn, "D");
        DetailDurationColumn = NormalizeColumn(DetailDurationColumn, "E");
        DetailCodeColumn = NormalizeColumn(DetailCodeColumn, "F");
        TimeSpreadsheetUrl = TimeSpreadsheetUrl.Trim();
        TimeSheetName = TimeSheetName.Trim();
        NameColumn = NormalizeColumn(NameColumn, "A");
        TimeColumn = NormalizeColumn(TimeColumn, "B");
        TimeCodeColumn = NormalizeColumn(TimeCodeColumn, "C");
        NoteColumn = NormalizeColumn(NoteColumn, "C");
        CustomNumbers = CustomNumberDirectory.Normalize(CustomNumbers);
        StartRow = Math.Clamp(StartRow, 1, 5_000);
        EndRow = Math.Clamp(EndRow, StartRow, 5_000);
        Schemes ??= [];
        if (Schemes.Count == 0)
        {
            Schemes.Add(TimeMarkScheme.CreateDefault());
        }

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scheme in Schemes)
        {
            scheme.Normalize();
            if (string.IsNullOrWhiteSpace(scheme.Name) || !usedNames.Add(scheme.Name))
            {
                scheme.Name = NextName(usedNames);
                usedNames.Add(scheme.Name);
            }
        }

        ActiveSchemeName = Schemes.Any(scheme => scheme.Name.Equals(ActiveSchemeName, StringComparison.OrdinalIgnoreCase))
            ? Schemes.First(scheme => scheme.Name.Equals(ActiveSchemeName, StringComparison.OrdinalIgnoreCase)).Name
            : Schemes[0].Name;
        Enabled = DetailEnabled || TimeEnabled;
    }

    public TimeMarkScheme ActiveScheme() =>
        Schemes.First(scheme => scheme.Name.Equals(ActiveSchemeName, StringComparison.OrdinalIgnoreCase));

    private static string NextName(HashSet<string> used)
    {
        for (var index = 2; ; index++)
        {
            var name = $"方案{index}";
            if (used.Add(name))
            {
                return name;
            }
        }
    }

    private static string NormalizeColumn(string value, string fallback)
    {
        var text = value.Trim().ToUpperInvariant();
        return SheetColumns.TryParse(text, out _) ? text : fallback;
    }
}

public sealed class TimeMarkScheme
{
    public string Name { get; set; } = "默认";
    public string Suffix { get; set; } = "VB";
    public string DefaultSymbol { get; set; } = "○";
    public string DefaultColor { get; set; } = "#E53935";
    public int PrefixMinimumMinutes { get; set; } = 60;
    public string PrefixText { get; set; } = string.Empty;
    public List<TimeMarkTier> Tiers { get; set; } =
    [
        new() { MinimumMinutes = 60, Symbol = "★", Color = "#43A047" },
        new() { MinimumMinutes = 30, Symbol = "●", Color = "#F9A825" }
    ];

    public static TimeMarkScheme CreateDefault() => new();

    public void Normalize()
    {
        Name = Name.Trim();
        Suffix = Suffix.Trim();
        PrefixText = PrefixText.Trim();
        PrefixMinimumMinutes = Math.Max(0, PrefixMinimumMinutes);
        if (string.IsNullOrEmpty(DefaultSymbol))
        {
            DefaultSymbol = "○";
        }

        DefaultColor = NormalizeColor(DefaultColor, "#E53935");
        Tiers ??= [];
        foreach (var tier in Tiers)
        {
            tier.MinimumMinutes = Math.Max(0, tier.MinimumMinutes);
            if (string.IsNullOrEmpty(tier.Symbol))
            {
                tier.Symbol = "●";
            }

            tier.Color = NormalizeColor(tier.Color, DefaultColor);
        }

        Tiers = Tiers
            .GroupBy(tier => tier.MinimumMinutes)
            .Select(group => group.First())
            .OrderByDescending(tier => tier.MinimumMinutes)
            .ToList();
    }

    public (string Symbol, string Color) Resolve(TimeSpan duration)
    {
        var minutes = duration.TotalMinutes;
        foreach (var tier in Tiers.OrderByDescending(tier => tier.MinimumMinutes))
        {
            if (minutes >= tier.MinimumMinutes)
            {
                return (tier.Symbol, tier.Color);
            }
        }

        return (DefaultSymbol, DefaultColor);
    }

    public TimeMarkScheme Copy(string name) => new()
    {
        Name = name,
        Suffix = Suffix,
        DefaultSymbol = DefaultSymbol,
        DefaultColor = DefaultColor,
        PrefixMinimumMinutes = PrefixMinimumMinutes,
        PrefixText = PrefixText,
        Tiers = Tiers.Select(tier => new TimeMarkTier
        {
            MinimumMinutes = tier.MinimumMinutes,
            Symbol = tier.Symbol,
            Color = tier.Color
        }).ToList()
    };

    private static string NormalizeColor(string value, string fallback)
    {
        var text = value.Trim();
        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        return Regex.IsMatch(text, "^#[0-9A-Fa-f]{6}$")
            ? text.ToUpperInvariant()
            : fallback;
    }
}

public sealed class CustomNumber
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

public sealed class TimeMarkTier
{
    public int MinimumMinutes { get; set; }
    public string Symbol { get; set; } = "●";
    public string Color { get; set; } = "#F9A825";
}

public static class SheetColumns
{
    public static bool TryParse(string text, out int index)
    {
        index = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim().ToUpperInvariant();
        if (value.Length is < 1 or > 2 || value.Any(character => character is < 'A' or > 'Z'))
        {
            return false;
        }

        index = value.Length == 1
            ? value[0] - 'A'
            : (value[0] - 'A' + 1) * 26 + (value[1] - 'A');
        return true;
    }

    public static string Letter(int index)
    {
        var number = index + 1;
        var letters = string.Empty;
        while (number > 0)
        {
            number--;
            letters = (char)('A' + number % 26) + letters;
            number /= 26;
        }

        return letters;
    }

    public static string Cell(string sheetName, int row, int column)
    {
        var letter = Letter(column);
        var address = $"{letter}{row.ToString(CultureInfo.InvariantCulture)}";
        return string.IsNullOrWhiteSpace(sheetName)
            ? address
            : $"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'!{address}";
    }
}
