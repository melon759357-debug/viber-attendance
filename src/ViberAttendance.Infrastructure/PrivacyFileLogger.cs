using System.Text.Json;
using ViberAttendance.Core.Contracts;

namespace ViberAttendance.Infrastructure;

public sealed class PrivacyFileLogger(
    ApplicationPaths paths,
    IClock clock,
    ViberAttendance.Core.Configuration.AppSettings settings) : IDiagnosticLogger, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public async Task WriteAsync(
        DiagnosticLogLevel level,
        string eventName,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!settings.SaveDiagnosticLogs)
        {
            return;
        }

        paths.EnsureDirectories();

        var entry = new
        {
            timestampUtc = clock.UtcNow,
            level = level.ToString(),
            eventName = DiagnosticText.Sanitize(eventName),
            message = DiagnosticText.Sanitize(message),
            exceptionType = exception?.GetType().FullName,
            exceptionMessage = exception is null ? null : DiagnosticText.Sanitize(exception.Message)
        };

        var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
        var file = Path.Combine(paths.LogsFolder, $"diagnostic-{clock.UtcNow:yyyyMMdd}.jsonl");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(file, line, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

}

public sealed class NullDiagnosticLogger : IDiagnosticLogger
{
    public Task WriteAsync(
        DiagnosticLogLevel level,
        string eventName,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
