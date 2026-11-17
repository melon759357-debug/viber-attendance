using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Contracts;
using ViberAttendance.Core.Sheets;

namespace ViberAttendance.App.Services;

public sealed class SheetGridClientRouter(
    AppSettings settings,
    GoogleSheetGridClient keyedClient,
    ScriptSheetGridClient scriptClient,
    LinkSheetGridClient linkClient) : ISheetGridClient
{
    public Task<IReadOnlyList<IReadOnlyList<string>>> ReadAsync(
        string spreadsheetUrl,
        string sheetName,
        CancellationToken cancellationToken = default) =>
        Client.ReadAsync(spreadsheetUrl, sheetName, cancellationToken);

    public Task WriteAsync(
        string spreadsheetUrl,
        string sheetName,
        IReadOnlyList<SheetCell> cells,
        IReadOnlyList<SheetColor> colors,
        CancellationToken cancellationToken = default) =>
        Client.WriteAsync(spreadsheetUrl, sheetName, cells, colors, cancellationToken);

    private ISheetGridClient Client => settings.SheetSync.WriteMode.ToUpperInvariant() switch
    {
        "SCRIPT" => scriptClient,
        "LINK" => linkClient,
        _ => keyedClient
    };
}
