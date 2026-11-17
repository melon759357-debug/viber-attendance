namespace ViberAttendance.Infrastructure;

public sealed record ApplicationPaths(
    string RootFolder,
    string SettingsFile,
    string LogsFolder,
    string DefaultDatabaseFile,
    string DefaultExportFolder)
{
    public static ApplicationPaths CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var root = Path.Combine(localAppData, "ViberAttendance");
        var export = string.IsNullOrWhiteSpace(documents)
            ? Path.Combine(root, "Exports")
            : Path.Combine(documents, "ViberAttendance", "Exports");
        return ForRoot(root, export);
    }

    public static ApplicationPaths ForRoot(string rootFolder, string? exportFolder = null)
    {
        var root = Path.GetFullPath(rootFolder);
        var export = Path.GetFullPath(exportFolder ?? Path.Combine(root, "Exports"));
        return new ApplicationPaths(
            root,
            Path.Combine(root, "settings.json"),
            Path.Combine(root, "Logs"),
            Path.Combine(root, "Data", "attendance.db"),
            export);
    }

    public ApplicationPaths WithDataFolder(string? dataFolder)
    {
        if (string.IsNullOrWhiteSpace(dataFolder))
        {
            return this;
        }

        var custom = ForRoot(dataFolder);
        return new ApplicationPaths(
            custom.RootFolder,
            SettingsFile,
            custom.LogsFolder,
            custom.DefaultDatabaseFile,
            custom.DefaultExportFolder);
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootFolder);
        Directory.CreateDirectory(LogsFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(DefaultDatabaseFile)!);
        Directory.CreateDirectory(DefaultExportFolder);
    }
}
