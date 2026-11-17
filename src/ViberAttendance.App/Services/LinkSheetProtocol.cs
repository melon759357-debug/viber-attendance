using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Sheets;

namespace ViberAttendance.App.Services;

public static partial class LinkSheetProtocol
{
    public const string RejectedMessage = "Google 拒绝了未登录写入。只填链接改不了表格。";
    public const string UnreadableMessage = "表格链接无法直接读取。";

    public static bool TryParseSpreadsheetId(string url, out string spreadsheetId)
    {
        var match = SpreadsheetIdPattern().Match(url);
        spreadsheetId = match.Success ? match.Groups[1].Value : string.Empty;
        return match.Success;
    }

    public static string ReadAddress(string spreadsheetId, string sheetName)
    {
        var address = string.Create(
            CultureInfo.InvariantCulture,
            $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/gviz/tq?tqx=out:csv&headers=0");
        return string.IsNullOrWhiteSpace(sheetName)
            ? address
            : address + "&sheet=" + Uri.EscapeDataString(sheetName.Trim());
    }

    public static string ValueAddress(string spreadsheetId) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"https://sheets.googleapis.com/v4/spreadsheets/{spreadsheetId}/values:batchUpdate");

    public static string CreateValueBody(string sheetName, IReadOnlyList<SheetCell> cells) =>
        JsonSerializer.Serialize(new
        {
            valueInputOption = "RAW",
            data = cells.Select(cell => new
            {
                range = SheetColumns.Cell(sheetName, cell.Row, cell.Column),
                values = new[] { new[] { cell.Value } }
            })
        });

    public static bool LooksLikeGrid(string body)
    {
        var text = body.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (text.StartsWith('<') || text.Contains("<html", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !text.Contains("accounts.google.com", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<IReadOnlyList<string>> ParseCsv(string text)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        cell.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    cell.Append(character);
                }

                continue;
            }

            if (character == '"')
            {
                quoted = true;
                continue;
            }

            if (character == ',')
            {
                row.Add(cell.ToString());
                cell.Clear();
                continue;
            }

            if (character == '\r')
            {
                continue;
            }

            if (character == '\n')
            {
                row.Add(cell.ToString());
                cell.Clear();
                rows.Add(row.ToArray());
                row = [];
                continue;
            }

            cell.Append(character);
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row.ToArray());
        }

        return rows;
    }

    public static string Describe(bool opened, int writeStatus) => writeStatus switch
    {
        >= 200 and < 300 => opened
            ? "链接能打开，Google 也接受了未登录写入。"
            : "Google 接受了未登录写入。",
        401 or 403 => opened
            ? "链接能打开，但 Google 拒绝了未登录写入。只填链接改不了表格。"
            : RejectedMessage,
        _ => opened
            ? "链接能打开，但这次试写没有成功。"
            : "试写没有成功。"
    };

    public static string WriteFailure(int status) => status is 401 or 403
        ? RejectedMessage
        : "只填链接写入没有成功。";

    [GeneratedRegex(@"/spreadsheets/d/([A-Za-z0-9_-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex SpreadsheetIdPattern();
}
