using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using SheetColorValue = Google.Apis.Sheets.v4.Data.Color;
using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Contracts;
using ViberAttendance.Core.Sheets;

namespace ViberAttendance.App.Services;

public sealed class GoogleSheetGridClient(AppSettings settings) : ISheetGridClient
{
    private static readonly Regex SpreadsheetIdPattern = new(
        @"/spreadsheets/d/([A-Za-z0-9_-]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private SheetsService? _service;
    private string? _servicePath;

    public async Task<IReadOnlyList<IReadOnlyList<string>>> ReadAsync(
        string spreadsheetUrl,
        string sheetName,
        CancellationToken cancellationToken = default)
    {
        var service = CreateService();
        var spreadsheetId = ParseSpreadsheetId(spreadsheetUrl);
        var title = await ResolveTitleAsync(service, spreadsheetId, sheetName, cancellationToken).ConfigureAwait(false);
        var response = await service.Spreadsheets.Values
            .Get(spreadsheetId, $"{Quote(title)}!A:Z")
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
        return response.Values?
            .Select(row => (IReadOnlyList<string>)row.Select(value => value?.ToString() ?? string.Empty).ToArray())
            .ToArray() ?? [];
    }

    public async Task WriteAsync(
        string spreadsheetUrl,
        string sheetName,
        IReadOnlyList<SheetCell> cells,
        IReadOnlyList<SheetColor> colors,
        CancellationToken cancellationToken = default)
    {
        if (cells.Count == 0 && colors.Count == 0)
        {
            return;
        }

        var service = CreateService();
        var spreadsheetId = ParseSpreadsheetId(spreadsheetUrl);
        var title = await ResolveTitleAsync(service, spreadsheetId, sheetName, cancellationToken).ConfigureAwait(false);
        if (cells.Count > 0)
        {
            var body = new BatchUpdateValuesRequest
            {
                ValueInputOption = "RAW",
                Data = cells.Select(cell => new ValueRange
                {
                    Range = SheetColumns.Cell(title, cell.Row, cell.Column),
                    Values = new List<IList<object>> { new List<object> { cell.Value } }
                }).ToList()
            };
            await service.Spreadsheets.Values
                .BatchUpdate(body, spreadsheetId)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (colors.Count == 0)
        {
            return;
        }

        var metadata = await service.Spreadsheets.Get(spreadsheetId).ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var sheetId = metadata.Sheets
            .First(sheet => sheet.Properties.Title.Equals(title, StringComparison.Ordinal))
            .Properties.SheetId;
        var requests = colors.Select(color => new Request
        {
            RepeatCell = new RepeatCellRequest
            {
                Range = new GridRange
                {
                    SheetId = sheetId,
                    StartRowIndex = color.Row - 1,
                    EndRowIndex = color.Row,
                    StartColumnIndex = color.Column,
                    EndColumnIndex = color.Column + 1
                },
                Cell = new CellData
                {
                    UserEnteredFormat = new CellFormat
                    {
                        BackgroundColor = ParseColor(color.Hex)
                    }
                },
                Fields = "userEnteredFormat.backgroundColor"
            }
        }).ToList();
        await service.Spreadsheets
            .BatchUpdate(new BatchUpdateSpreadsheetRequest { Requests = requests }, spreadsheetId)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private SheetsService CreateService()
    {
        var path = settings.SheetSync.ServiceAccountJsonPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new InvalidOperationException("未配置可用的服务账号密钥。");
        }

        if (_service is not null && string.Equals(_servicePath, path, StringComparison.OrdinalIgnoreCase))
        {
            return _service;
        }

        var credential = GoogleCredential.FromFile(path).CreateScoped(SheetsService.Scope.Spreadsheets);
        _service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "ViberAttendance"
        });
        _servicePath = path;
        return _service;
    }

    private static async Task<string> ResolveTitleAsync(
        SheetsService service,
        string spreadsheetId,
        string sheetName,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(sheetName))
        {
            return sheetName;
        }

        var metadata = await service.Spreadsheets.Get(spreadsheetId).ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return metadata.Sheets.First().Properties.Title;
    }

    private static string ParseSpreadsheetId(string url)
    {
        var match = SpreadsheetIdPattern.Match(url);
        if (!match.Success)
        {
            throw new InvalidOperationException("表格链接不正确。");
        }

        return match.Groups[1].Value;
    }

    private static string Quote(string sheetName) =>
        $"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'";

    private static SheetColorValue ParseColor(string hex)
    {
        var text = hex.Trim().TrimStart('#');
        if (text.Length != 6 || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
        {
            packed = 0xE53935;
        }

        return new SheetColorValue
        {
            Red = ((packed >> 16) & 0xFF) / 255f,
            Green = ((packed >> 8) & 0xFF) / 255f,
            Blue = (packed & 0xFF) / 255f
        };
    }
}
