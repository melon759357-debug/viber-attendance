using System.IO;

namespace ViberAttendance.App.Services;

public static class AppRelease
{
    public const string Version = "1.1.0";
    public const string FileName = "Viber在线记录.exe";
    public const string RepositoryLatest =
        "https://api.github.com/repos/melon759357-debug/viber-attendance/releases/latest";

    public static string InstallDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ViberAttendance",
            "App");

    public static string InstallPath => Path.Combine(InstallDirectory, FileName);

    public static bool IsDevelopmentBuild(string processPath)
    {
        var full = Path.GetFullPath(processPath);
        var bin = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        return full.Contains(bin, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRunningFromInstall(string processPath) =>
        string.Equals(Path.GetFullPath(processPath), Path.GetFullPath(InstallPath), StringComparison.OrdinalIgnoreCase);

    public static bool IsNewer(string? current, string? candidate)
    {
        return TryParse(candidate, out var next) && TryParse(current, out var now) && next > now;
    }

    public static bool ShouldInstall(string? installedVersion, string incomingVersion) =>
        string.IsNullOrWhiteSpace(installedVersion) || IsNewer(installedVersion, incomingVersion);

    public static bool TryParse(string? text, out System.Version version)
    {
        version = new System.Version(0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var dash = value.IndexOf('-', StringComparison.Ordinal);
        if (dash > 0)
        {
            value = value[..dash];
        }

        return System.Version.TryParse(value, out version!);
    }

    public static string? ChooseDownload(IEnumerable<(string Name, string Url)> assets)
    {
        string? zip = null;
        string? exe = null;
        foreach (var asset in assets)
        {
            if (!IsGitHubDownload(asset.Url))
            {
                continue;
            }

            if (zip is null && asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                zip = asset.Url;
            }
            else if (exe is null && asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                exe = asset.Url;
            }
        }

        return zip ?? exe;
    }

    public static bool IsGitHubDownload(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase));
}
