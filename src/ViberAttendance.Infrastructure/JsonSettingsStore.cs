using System.Text.Json;
using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Contracts;

namespace ViberAttendance.Infrastructure;

public sealed class JsonSettingsStore(ApplicationPaths paths) : ISettingsStore, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureDirectories();
            if (!File.Exists(paths.SettingsFile))
            {
                return CreateDefaults();
            }

            try
            {
                await using var stream = File.OpenRead(paths.SettingsFile);
                var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, _jsonOptions, cancellationToken)
                    .ConfigureAwait(false) ?? CreateDefaults();
                settings.Normalize(paths.DefaultDatabaseFile, paths.DefaultExportFolder);
                return settings;
            }
            catch (JsonException)
            {
                var backup = $"{paths.SettingsFile}.corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
                File.Copy(paths.SettingsFile, backup, overwrite: false);
                return CreateDefaults();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureDirectories();
            settings.Normalize(paths.DefaultDatabaseFile, paths.DefaultExportFolder);
            var temporaryPath = $"{paths.SettingsFile}.{Guid.NewGuid():N}.tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, settings, _jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, paths.SettingsFile, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private AppSettings CreateDefaults()
    {
        var settings = new AppSettings();
        settings.Normalize(paths.DefaultDatabaseFile, paths.DefaultExportFolder);
        return settings;
    }

    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
