using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ViberAttendance.Core.Contracts;
using ViberAttendance.Core.Models;

namespace ViberAttendance.UIAutomation;

public sealed class UiAutomationTreeExporter : IUiAutomationTreeExporter
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public async Task<UiAutomationExportResult> ExportAsync(
        UiAutomationScanResult scan,
        string jsonPath,
        bool includeSensitiveUiText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);

        jsonPath = Path.GetFullPath(jsonPath);
        if (!string.Equals(Path.GetExtension(jsonPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            jsonPath += ".json";
        }

        var textPath = Path.ChangeExtension(jsonPath, ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);

        var document = new ExportDocument
        {
            SchemaVersion = 1,
            CapturedAtUtc = scan.CapturedAtUtc,
            PrivacyMode = includeSensitiveUiText ? "SensitiveTextIncluded" : "TextRedacted",
            Window = new ExportWindow
            {
                ProcessId = scan.Window.ProcessId,
                ProcessName = scan.Window.ProcessName,
                WindowHandle = scan.Window.WindowHandle,
                HandleHex = scan.Window.HandleHex,
                Title = Protect(scan.Window.Title, includeSensitiveUiText),
                ClassName = scan.Window.ClassName,
                Bounds = scan.Window.Bounds,
                IsVisible = scan.Window.IsVisible,
                IsMinimized = scan.Window.IsMinimized
            },
            NodeCount = scan.NodeCount,
            WasTruncated = scan.WasTruncated,
            DurationMilliseconds = scan.Duration.TotalMilliseconds,
            Errors = scan.Errors,
            Root = scan.Root is null ? null : ConvertNode(scan.Root, includeSensitiveUiText)
        };

        var json = JsonSerializer.Serialize(document, _jsonOptions);
        var text = BuildText(document);
        await WriteAtomicAsync(jsonPath, json, cancellationToken).ConfigureAwait(false);
        await WriteAtomicAsync(textPath, text, cancellationToken).ConfigureAwait(false);
        return new UiAutomationExportResult(jsonPath, textPath);
    }

    private static ExportNode ConvertNode(UiAutomationNode node, bool includeSensitiveText) => new()
    {
        Path = node.Path,
        RuntimeId = node.RuntimeId,
        AutomationId = node.AutomationId,
        Name = Protect(node.Name, includeSensitiveText),
        ControlType = node.ControlType,
        LocalizedControlType = node.LocalizedControlType,
        ClassName = node.ClassName,
        FrameworkId = node.FrameworkId,
        HelpText = Protect(node.HelpText, includeSensitiveText),
        AccessKey = node.AccessKey,
        AcceleratorKey = node.AcceleratorKey,
        ProcessId = node.ProcessId,
        NativeWindowHandle = node.NativeWindowHandle,
        IsEnabled = node.IsEnabled,
        IsOffscreen = node.IsOffscreen,
        IsKeyboardFocusable = node.IsKeyboardFocusable,
        HasKeyboardFocus = node.HasKeyboardFocus,
        BoundingRectangle = node.BoundingRectangle,
        SupportedPatterns = node.SupportedPatterns,
        Children = node.Children.Select(child => ConvertNode(child, includeSensitiveText)).ToList()
    };

    private static string BuildText(ExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ViberAttendance UI Automation Tree");
        builder.AppendLine(CultureInfo.InvariantCulture, $"SchemaVersion: {document.SchemaVersion}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"CapturedAtUtc: {document.CapturedAtUtc:O}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"PrivacyMode: {document.PrivacyMode}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Window: PID={document.Window.ProcessId} HWND={document.Window.HandleHex} Class={document.Window.ClassName} Title={document.Window.Title}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Bounds: {document.Window.Bounds} Visible={document.Window.IsVisible} Minimized={document.Window.IsMinimized}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Nodes: {document.NodeCount} Truncated={document.WasTruncated} DurationMs={document.DurationMilliseconds:0.##}");

        foreach (var error in document.Errors)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"Error: {error}");
        }

        builder.AppendLine();
        if (document.Root is not null)
        {
            AppendNode(builder, document.Root, 0);
        }

        return builder.ToString();
    }

    private static void AppendNode(StringBuilder builder, ExportNode node, int depth)
    {
        builder.Append(' ', depth * 2);
        builder.Append(node.Path);
        builder.Append(" | ").Append(node.ControlType);
        builder.Append(" | Name=").Append(node.Name);
        builder.Append(" | AutomationId=").Append(node.AutomationId);
        builder.Append(" | Class=").Append(node.ClassName);
        builder.Append(" | RuntimeId=").Append(node.RuntimeId);
        builder.Append(" | Bounds=").Append(node.BoundingRectangle?.ToString() ?? "<empty>");
        builder.Append(" | Offscreen=").Append(node.IsOffscreen);
        builder.Append(" | Patterns=").Append(string.Join(',', node.SupportedPatterns));
        builder.AppendLine();

        foreach (var child in node.Children)
        {
            AppendNode(builder, child, depth + 1);
        }
    }

    private static string Protect(string value, bool includeSensitiveText)
    {
        if (includeSensitiveText || string.IsNullOrEmpty(value))
        {
            return value;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"<redacted sha256={Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant()} length={value.Length}>";
    }

    private static async Task WriteAtomicAsync(string path, string contents, CancellationToken cancellationToken)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporaryPath, contents, new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private sealed class ExportDocument
    {
        public int SchemaVersion { get; init; }
        public DateTimeOffset CapturedAtUtc { get; init; }
        public required string PrivacyMode { get; init; }
        public required ExportWindow Window { get; init; }
        public int NodeCount { get; init; }
        public bool WasTruncated { get; init; }
        public double DurationMilliseconds { get; init; }
        public required IReadOnlyList<string> Errors { get; init; }
        public ExportNode? Root { get; init; }
    }

    private sealed class ExportWindow
    {
        public int ProcessId { get; init; }
        public required string ProcessName { get; init; }
        public long WindowHandle { get; init; }
        public required string HandleHex { get; init; }
        public required string Title { get; init; }
        public required string ClassName { get; init; }
        public required PixelRect Bounds { get; init; }
        public bool IsVisible { get; init; }
        public bool IsMinimized { get; init; }
    }

    private sealed class ExportNode
    {
        public required string Path { get; init; }
        public required string RuntimeId { get; init; }
        public required string AutomationId { get; init; }
        public required string Name { get; init; }
        public required string ControlType { get; init; }
        public required string LocalizedControlType { get; init; }
        public required string ClassName { get; init; }
        public required string FrameworkId { get; init; }
        public required string HelpText { get; init; }
        public required string AccessKey { get; init; }
        public required string AcceleratorKey { get; init; }
        public int ProcessId { get; init; }
        public int NativeWindowHandle { get; init; }
        public bool IsEnabled { get; init; }
        public bool IsOffscreen { get; init; }
        public bool IsKeyboardFocusable { get; init; }
        public bool HasKeyboardFocus { get; init; }
        public PixelRect? BoundingRectangle { get; init; }
        public required IReadOnlyList<string> SupportedPatterns { get; init; }
        public required List<ExportNode> Children { get; init; }
    }
}
