using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ViberAttendance.App.Services;

public enum StartupHandoff
{
    Continue,
    Relaunched,
    NeedsClose
}

public static class AppUpdater
{
    public const long MaxUpdateBytes = 400L * 1024 * 1024;

    private static readonly string[] UpdateHosts =
    [
        "api.github.com",
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
        "github-releases.githubusercontent.com"
    ];
    public static StartupHandoff HandoffToInstalledCopy(string processPath)
    {
        if (AppRelease.IsDevelopmentBuild(processPath) || AppRelease.IsRunningFromInstall(processPath))
        {
            return StartupHandoff.Continue;
        }

        var installedVersion = ReadFileVersion(AppRelease.InstallPath);
        var incomingVersion = ReadFileVersion(processPath);
        if (string.IsNullOrWhiteSpace(incomingVersion))
        {
            incomingVersion = AppRelease.Version;
        }

        try
        {
            Directory.CreateDirectory(AppRelease.InstallDirectory);
            if (AppRelease.ShouldInstall(installedVersion, incomingVersion) || !File.Exists(AppRelease.InstallPath))
            {
                File.Copy(processPath, AppRelease.InstallPath, overwrite: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return StartupHandoff.NeedsClose;
        }

        Start(AppRelease.InstallPath);
        return StartupHandoff.Relaunched;
    }

    public static async Task<bool> TryUpdateFromGitHubAsync(string processPath, CancellationToken cancellationToken = default)
    {
        if (!AppRelease.IsRunningFromInstall(processPath))
        {
            return false;
        }

        try
        {
            using var http = new HttpClient(new AllowlistedRedirectHandler(
                new SocketsHttpHandler { AllowAutoRedirect = false },
                UpdateHosts))
            {
                Timeout = TimeSpan.FromMinutes(3)
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ViberAttendance");
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var apiTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            apiTimeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var response = await http.GetAsync(AppRelease.RepositoryLatest, apiTimeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagValue) ? tagValue.GetString() : null;
            if (!AppRelease.IsNewer(AppRelease.Version, tag) || !root.TryGetProperty("assets", out var assets))
            {
                return false;
            }

            var downloads = assets.EnumerateArray().Select(asset => (
                Name: asset.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                Url: asset.TryGetProperty("browser_download_url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
                Digest: asset.TryGetProperty("digest", out var digest) ? digest.GetString() : null));
            var download = AppRelease.ChooseDownload(downloads);
            if (download is null)
            {
                return false;
            }

            var downloaded = Path.Combine(Path.GetTempPath(), $"viber-update-{Guid.NewGuid():N}");
            if (!await SaveLimitedAsync(http, download.Value.Url, downloaded, cancellationToken).ConfigureAwait(false) ||
                !DigestMatches(downloaded, download.Value.Digest))
            {
                TryDelete(downloaded);
                return false;
            }

            var staged = AppRelease.InstallPath + ".new";
            if (download.Value.Url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryExtractExe(downloaded, staged))
                {
                    File.Delete(downloaded);
                    return false;
                }

                File.Delete(downloaded);
            }
            else
            {
                File.Move(downloaded, staged, overwrite: true);
            }

            ScheduleReplace(Environment.ProcessId, staged, AppRelease.InstallPath);
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or TaskCanceledException or InvalidOperationException)
        {
            return false;
        }
    }

    public static string? ReadFileVersion(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var info = FileVersionInfo.GetVersionInfo(path);
        return string.IsNullOrWhiteSpace(info.FileVersion) ? null : info.FileVersion;
    }

    private static bool TryExtractExe(string zipPath, string destination)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(item =>
            item.FullName.Equals(AppRelease.FileName, StringComparison.Ordinal) &&
            item.Length is > 0 and <= MaxUpdateBytes);
        if (entry is null)
        {
            return false;
        }

        entry.ExtractToFile(destination, overwrite: true);
        return true;
    }

    private static async Task<bool> SaveLimitedAsync(
        HttpClient http,
        string url,
        string destination,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode ||
            response.Content.Headers.ContentLength is long advertised && advertised > MaxUpdateBytes)
        {
            return false;
        }

        await using var network = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var file = File.Create(destination);
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await network.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaxUpdateBytes)
            {
                return false;
            }

            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return total > 0;
    }

    private static bool DigestMatches(string path, string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return false;
        }

        const string prefix = "sha256:";
        if (!File.Exists(path) ||
            !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            digest.Length != prefix.Length + 64)
        {
            return false;
        }

        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        return actual.Equals(digest[prefix.Length..], StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void ScheduleReplace(int processId, string newPath, string targetPath)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"viber-update-{processId}.ps1");
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(newPath + "\n" + targetPath));
        File.WriteAllText(scriptPath, """
            param([int]$ProcessId, [string]$Payload)
            $parts = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Payload)) -split "`n", 2
            Wait-Process -Id $ProcessId -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 500
            Move-Item -LiteralPath $parts[0] -Destination $parts[1] -Force
            Start-Process -FilePath $parts[1]
            Remove-Item -LiteralPath $PSCommandPath -Force
            """, Encoding.ASCII);
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -ProcessId {processId} -Payload {payload}",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static void Start(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
