namespace ViberAttendance.Core.Configuration;

public sealed class AppSettings
{
    public int SettingsSchemaVersion { get; set; }
    public double FastScanSeconds { get; set; } = 2;
    public double FullScanSeconds { get; set; } = 30;
    public double AutoSaveSeconds { get; set; } = 5;
    public double HeartbeatSeconds { get; set; } = 10;
    public int OnlineConfirmationCount { get; set; } = 2;
    public int OfflineConfirmationCount { get; set; } = 3;
    public double OfflineWaitSeconds { get; set; } = 5;
    public bool StartWithWindows { get; set; }
    public bool AutoMonitorAfterViberStarts { get; set; } = true;
    public bool EnableLocalOcrFallback { get; set; }
    public bool SaveDiagnosticLogs { get; set; } = true;
    public bool SaveRedactedDiagnosticScreenshots { get; set; }
    public bool IncludeSensitiveUiTextInExport { get; set; }
    public int UiaScanTimeoutSeconds { get; set; } = 30;
    public int UiaMaxNodes { get; set; } = 25_000;
    public int UiaMaxDepth { get; set; } = 80;
    public int WindowMinimumWidth { get; set; } = 300;
    public int WindowMinimumHeight { get; set; } = 200;
    public int WindowDiscoveryUiaMaxNodes { get; set; } = 500;
    public string DatabasePath { get; set; } = string.Empty;
    public string ExportFolder { get; set; } = string.Empty;
    public string UiLanguage { get; set; } = "zh-CN";
    public string UiTheme { get; set; } = "Light";
    public SheetSyncSettings SheetSync { get; set; } = new();

    public void Normalize(string defaultDatabasePath, string defaultExportFolder)
    {
        if (SettingsSchemaVersion < 2)
        {
            AutoMonitorAfterViberStarts = true;
            SettingsSchemaVersion = 2;
        }

        FastScanSeconds = Math.Clamp(FastScanSeconds, 0.5, 300);
        FullScanSeconds = Math.Clamp(FullScanSeconds, 5, 3_600);
        AutoSaveSeconds = Math.Clamp(AutoSaveSeconds, 1, 300);
        HeartbeatSeconds = Math.Clamp(HeartbeatSeconds, 2, 300);
        OnlineConfirmationCount = Math.Clamp(OnlineConfirmationCount, 1, 20);
        OfflineConfirmationCount = Math.Clamp(OfflineConfirmationCount, 1, 20);
        OfflineWaitSeconds = Math.Clamp(OfflineWaitSeconds, 1, 300);
        UiaScanTimeoutSeconds = Math.Clamp(UiaScanTimeoutSeconds, 5, 300);
        UiaMaxNodes = Math.Clamp(UiaMaxNodes, 100, 100_000);
        UiaMaxDepth = Math.Clamp(UiaMaxDepth, 5, 200);
        WindowMinimumWidth = Math.Clamp(WindowMinimumWidth, 100, 4_000);
        WindowMinimumHeight = Math.Clamp(WindowMinimumHeight, 100, 4_000);
        WindowDiscoveryUiaMaxNodes = Math.Clamp(WindowDiscoveryUiaMaxNodes, 50, 5_000);
        DatabasePath = string.IsNullOrWhiteSpace(DatabasePath) ? defaultDatabasePath : DatabasePath;
        ExportFolder = string.IsNullOrWhiteSpace(ExportFolder) ? defaultExportFolder : ExportFolder;
        UiLanguage = string.IsNullOrWhiteSpace(UiLanguage) ? "zh-CN" : UiLanguage;
        UiTheme = UiTheme switch
        {
            var value when value.Equals("Dark", StringComparison.OrdinalIgnoreCase) || value == "深色" => "Dark",
            var value when value.Equals("System", StringComparison.OrdinalIgnoreCase) || value == "跟随系统" => "System",
            _ => "Light"
        };
        SheetSync ??= new SheetSyncSettings();
        SheetSync.Normalize();
    }
}
