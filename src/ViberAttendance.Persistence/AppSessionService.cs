using System.Globalization;
using Microsoft.Data.Sqlite;
using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Contracts;

namespace ViberAttendance.Persistence;

public sealed class AppSessionService(
    AppSettings settings,
    IClock clock,
    IDiagnosticLogger logger) : IAppSessionService
{
    private readonly SqliteConnectionFactory _connectionFactory = new(settings);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly List<Guid> _interruptedSessionIds = [];
    private CancellationTokenSource? _heartbeatCts;
    private Task? _heartbeatTask;
    private Guid? _sessionId;

    public IReadOnlyList<Guid> InterruptedSessionIds => _interruptedSessionIds;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sessionId is not null)
            {
                return;
            }

            var now = clock.UtcNow;
            await LoadInterruptedSessionsAsync(now, cancellationToken).ConfigureAwait(false);

            var id = Guid.NewGuid();
            await using var connection = _connectionFactory.Create();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO app_sessions
                    (id, started_at_utc, ended_at_utc, shutdown_type, last_heartbeat_at_utc)
                VALUES ($id, $started, NULL, NULL, $heartbeat);
                """;
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            command.Parameters.AddWithValue("$started", Format(now));
            command.Parameters.AddWithValue("$heartbeat", Format(now));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _sessionId = id;
            _heartbeatCts = new CancellationTokenSource();
            _heartbeatTask = RunHeartbeatLoopAsync(id, _heartbeatCts.Token);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(string shutdownType, CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sessionId is not Guid id)
            {
                return;
            }

            _heartbeatCts?.Cancel();
            if (_heartbeatTask is not null)
            {
                try
                {
                    await _heartbeatTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 取消是正常关机流程的一部分。
                }
            }

            var now = clock.UtcNow;
            await using var connection = _connectionFactory.Create();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE app_sessions
                SET ended_at_utc = $ended,
                    shutdown_type = $shutdownType,
                    last_heartbeat_at_utc = $heartbeat
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$ended", Format(now));
            command.Parameters.AddWithValue("$shutdownType", shutdownType);
            command.Parameters.AddWithValue("$heartbeat", Format(now));
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _sessionId = null;
            _heartbeatCts?.Dispose();
            _heartbeatCts = null;
            _heartbeatTask = null;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync("UnknownEnd").ConfigureAwait(false);
        _lifecycleGate.Dispose();
    }

    private async Task LoadInterruptedSessionsAsync(DateTimeOffset currentStart, CancellationToken cancellationToken)
    {
        _interruptedSessionIds.Clear();
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id
            FROM app_sessions
            WHERE ended_at_utc IS NULL
              AND last_heartbeat_at_utc < $currentStart;
            """;
        command.Parameters.AddWithValue("$currentStart", Format(currentStart));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Guid.TryParse(reader.GetString(0), out var id))
            {
                _interruptedSessionIds.Add(id);
            }
        }
    }

    private async Task RunHeartbeatLoopAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.HeartbeatSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await WriteHeartbeatAsync(sessionId, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    await logger.WriteAsync(
                        DiagnosticLogLevel.Error,
                        "session.heartbeat.failed",
                        "应用会话心跳写入失败。",
                        exception,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 正常停止。
        }
    }

    private async Task WriteHeartbeatAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE app_sessions SET last_heartbeat_at_utc = $heartbeat WHERE id = $id;";
        command.Parameters.AddWithValue("$heartbeat", Format(clock.UtcNow));
        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}

