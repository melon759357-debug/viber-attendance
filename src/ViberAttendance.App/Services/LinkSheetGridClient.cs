using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using ViberAttendance.Core.Contracts;
using ViberAttendance.Core.Sheets;

namespace ViberAttendance.App.Services;

public sealed class LinkSheetGridClient : ISheetGridClient
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public LinkSheetGridClient()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ViberAttendance");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<string> ProbeAsync(string spreadsheetUrl, string sheetName, CancellationToken cancellationToken)
    {
        if (!LinkSheetProtocol.TryParseSpreadsheetId(spreadsheetUrl, out var spreadsheetId))
        {
            return "表格链接不正确。";
        }

        var opened = await CanReadAsync(spreadsheetId, sheetName, cancellationToken).ConfigureAwait(false);
        try
        {
            using var response = await PostAsync(
                LinkSheetProtocol.ValueAddress(spreadsheetId),
                """{"valueInputOption":"RAW","data":[]}""",
                cancellationToken).ConfigureAwait(false);
            return LinkSheetProtocol.Describe(opened, (int)response.StatusCode);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return opened ? "链接能打开，但写入请求没有送到 Google。" : "表格链接打不开。";
        }
    }

    public Task<IReadOnlyList<IReadOnlyList<string>>> ReadAsync(
        string spreadsheetUrl,
        string sheetName,
        int columnCount,
        CancellationToken cancellationToken = default) =>
        ReadAsync(spreadsheetUrl, sheetName, cancellationToken);

    public async Task<IReadOnlyList<IReadOnlyList<string>>> ReadAsync(
        string spreadsheetUrl,
        string sheetName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var spreadsheetId = RequireId(spreadsheetUrl);
            using var response = await _http.GetAsync(
                LinkSheetProtocol.ReadAddress(spreadsheetId, sheetName),
                cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || !LinkSheetProtocol.LooksLikeGrid(body))
            {
                throw new InvalidOperationException(LinkSheetProtocol.UnreadableMessage);
            }

            return LinkSheetProtocol.ParseCsv(body);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not InvalidOperationException)
        {
            throw new InvalidOperationException(LinkSheetProtocol.UnreadableMessage);
        }
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

        try
        {
            var spreadsheetId = RequireId(spreadsheetUrl);
            if (cells.Count == 0)
            {
                throw new InvalidOperationException("只填链接不能改单元格颜色。");
            }

            using var response = await PostAsync(
                LinkSheetProtocol.ValueAddress(spreadsheetId),
                LinkSheetProtocol.CreateValueBody(sheetName, cells),
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(LinkSheetProtocol.WriteFailure((int)response.StatusCode));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not InvalidOperationException)
        {
            throw new InvalidOperationException("只填链接写入没有成功。");
        }
    }

    private async Task<bool> CanReadAsync(string spreadsheetId, string sheetName, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(
                LinkSheetProtocol.ReadAddress(spreadsheetId, sheetName),
                cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode && LinkSheetProtocol.LooksLikeGrid(body);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<HttpResponseMessage> PostAsync(string address, string json, CancellationToken cancellationToken)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _http.PostAsync(address, content, cancellationToken).ConfigureAwait(false);
    }

    private static string RequireId(string spreadsheetUrl) =>
        LinkSheetProtocol.TryParseSpreadsheetId(spreadsheetUrl, out var spreadsheetId)
            ? spreadsheetId
            : throw new InvalidOperationException("表格链接不正确。");
}
