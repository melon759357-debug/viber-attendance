using System.Text.Json;
using System.Text.RegularExpressions;
using ViberAttendance.Core.Sheets;

namespace ViberAttendance.App.Services;

public static partial class ScriptSheetProtocol
{
    public const string AppsScript = """
        function doPost(e) {
          var lock = LockService.getScriptLock();
          lock.waitLock(20000);
          try {
            var body = JSON.parse(e.postData.contents);
            var spreadsheet = SpreadsheetApp.openByUrl(body.spreadsheetUrl);
            var sheet = body.sheetName
              ? spreadsheet.getSheetByName(body.sheetName)
              : spreadsheet.getSheets()[0];
            if (!sheet) {
              return respond_({ ok: false, error: "找不到工作表" });
            }
            if (body.action === "read") {
              return respond_({ ok: true, rows: readRows_(sheet) });
            }
            if (body.action === "write") {
              var cells = body.cells || [];
              for (var i = 0; i < cells.length; i++) {
                var value = cells[i].value == null ? "" : String(cells[i].value);
                sheet.getRange(cells[i].row, cells[i].column + 1).setValue(value);
              }
              var colors = body.colors || [];
              for (var j = 0; j < colors.length; j++) {
                sheet.getRange(colors[j].row, colors[j].column + 1).setBackground(colors[j].hex);
              }
              return respond_({ ok: true });
            }
            return respond_({ ok: false, error: "未知操作" });
          } catch (error) {
            return respond_({ ok: false, error: String(error) });
          } finally {
            lock.releaseLock();
          }
        }

        function readRows_(sheet) {
          var lastRow = sheet.getLastRow();
          if (lastRow < 1) {
            return [];
          }
          return sheet.getRange(1, 1, lastRow, 26).getDisplayValues().map(function (row) {
            return row.map(function (cell) { return String(cell); });
          });
        }

        function respond_(payload) {
          return ContentService
            .createTextOutput(JSON.stringify(payload))
            .setMimeType(ContentService.MimeType.JSON);
        }
        """;

    public static string CreateRead(string spreadsheetUrl, string sheetName) =>
        JsonSerializer.Serialize(new
        {
            action = "read",
            spreadsheetUrl,
            sheetName
        });

    public static string CreateWrite(
        string spreadsheetUrl,
        string sheetName,
        IReadOnlyList<SheetCell> cells,
        IReadOnlyList<SheetColor> colors) =>
        JsonSerializer.Serialize(new
        {
            action = "write",
            spreadsheetUrl,
            sheetName,
            cells = cells.Select(cell => new { row = cell.Row, column = cell.Column, value = cell.Value }),
            colors = colors.Select(color => new { row = color.Row, column = color.Column, hex = color.Hex })
        });

    public static IReadOnlyList<IReadOnlyList<string>> ReadRows(string body)
    {
        using var document = Open(body);
        if (!document.RootElement.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return rows.EnumerateArray()
            .Select(row => (IReadOnlyList<string>)row.EnumerateArray().Select(CellText).ToArray())
            .ToArray();
    }

    private static JsonDocument Open(string body)
    {
        var text = body.Trim();
        if (!text.StartsWith('{'))
        {
            throw new InvalidOperationException("脚本没有返回结果。部署时请选以我的身份运行，并且任何人可以访问。");
        }

        var document = JsonDocument.Parse(text);
        if (document.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
        {
            var error = document.RootElement.TryGetProperty("error", out var value)
                ? value.GetString()
                : "脚本写入失败。";
            document.Dispose();
            throw new InvalidOperationException(Redact(string.IsNullOrWhiteSpace(error) ? "脚本写入失败。" : error));
        }

        return document;
    }

    private static string CellText(JsonElement cell) => cell.ValueKind switch
    {
        JsonValueKind.String => cell.GetString() ?? string.Empty,
        JsonValueKind.Null => string.Empty,
        _ => cell.ToString()
    };

    private static string Redact(string value)
    {
        var text = UrlPattern().Replace(value, "<redacted-url>");
        return text.Length <= 300 ? text : text[..300];
    }

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();
}
