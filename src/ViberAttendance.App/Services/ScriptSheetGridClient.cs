using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Contracts;
using ViberAttendance.Core.Sheets;

namespace ViberAttendance.App.Services;

public sealed class ScriptSheetGridClient(AppSettings settings) : ISheetGridClient
{
    private readonly HttpClient _http = new(new AllowlistedRedirectHandler(
        new SocketsHttpHandler { AllowAutoRedirect = false },
        ["script.google.com", "script.googleusercontent.com"]))
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public Task<IReadOnlyList<IReadOnlyList<string>>> ReadAsync(
        string spreadsheetUrl,
        string sheetName,
        CancellationToken cancellationToken = default) =>
        ReadAsync(spreadsheetUrl, sheetName, 26, cancellationToken);

    public Task<IReadOnlyList<IReadOnlyList<string>>> ReadAsync(
        string spreadsheetUrl,
        string sheetName,
        int columnCount,
        CancellationToken cancellationToken = default) =>
        PostAsync(
            ScriptSheetProtocol.CreateRead(spreadsheetUrl, sheetName, columnCount),
            ScriptSheetProtocol.ReadRows,
            cancellationToken);

    public Task WriteAsync(
        string spreadsheetUrl,
        string sheetName,
        IReadOnlyList<SheetCell> cells,
        IReadOnlyList<SheetColor> colors,
        CancellationToken cancellationToken = default)
    {
        if (cells.Count == 0 && colors.Count == 0)
        {
            return Task.CompletedTask;
        }

        return PostAsync(
            ScriptSheetProtocol.CreateWrite(spreadsheetUrl, sheetName, cells, colors),
            _ => Array.Empty<IReadOnlyList<string>>(),
            cancellationToken);
    }

    private async Task<T> PostAsync<T>(string json, Func<string, T> read, CancellationToken cancellationToken)
    {
        var url = settings.SheetSync.ScriptWebAppUrl.Trim();
        if (!ScriptSheetProtocol.IsScriptExecUrl(url))
        {
            throw new InvalidOperationException("请填写部署后的网页应用网址，以 /exec 结尾。");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode && !body.TrimStart().StartsWith('{'))
        {
            throw new InvalidOperationException("脚本网址无法访问。请重新部署，并允许任何人访问。");
        }

        return read(body);
    }
}
