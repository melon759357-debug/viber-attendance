using Microsoft.Data.Sqlite;
using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Contracts;

namespace ViberAttendance.Persistence;

public sealed class SqliteDatabaseInitializer(
    AppSettings settings,
    IDiagnosticLogger logger) : IDatabaseInitializer
{
    private readonly SqliteConnectionFactory _connectionFactory = new(settings);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var folder = Path.GetDirectoryName(settings.DatabasePath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new InvalidOperationException("数据库路径必须包含目录。");
        }

        Directory.CreateDirectory(folder);
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA synchronous=NORMAL;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA foreign_keys=ON;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA busy_timeout=5000;", cancellationToken).ConfigureAwait(false);

        using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = SchemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        transaction.Commit();

        await logger.WriteAsync(
            DiagnosticLogLevel.Information,
            "database.initialized",
            "SQLite 架构已初始化（WAL + foreign_keys）。",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS schema_info (
            version INTEGER NOT NULL,
            applied_at_utc TEXT NOT NULL
        );

        INSERT INTO schema_info (version, applied_at_utc)
        SELECT 1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
        WHERE NOT EXISTS (SELECT 1 FROM schema_info WHERE version = 1);

        CREATE TABLE IF NOT EXISTS calls (
            id TEXT PRIMARY KEY,
            started_at_utc TEXT NOT NULL,
            ended_at_utc TEXT NULL,
            title TEXT NOT NULL,
            viber_window_identity TEXT NOT NULL,
            status TEXT NOT NULL,
            end_reason TEXT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS members (
            id TEXT PRIMARY KEY,
            display_name TEXT NOT NULL,
            normalized_name TEXT NOT NULL,
            avatar_hash TEXT NULL,
            uia_identity TEXT NULL,
            alias TEXT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS attendance_intervals (
            id TEXT PRIMARY KEY,
            call_id TEXT NOT NULL,
            member_id TEXT NOT NULL,
            first_seen_at_utc TEXT NOT NULL,
            confirmed_online_at_utc TEXT NULL,
            last_seen_at_utc TEXT NOT NULL,
            confirmed_offline_at_utc TEXT NULL,
            duration_ms INTEGER NULL CHECK (duration_ms IS NULL OR duration_ms >= 0),
            start_detection_source TEXT NOT NULL,
            end_detection_source TEXT NULL,
            start_confidence REAL NOT NULL CHECK (start_confidence BETWEEN 0 AND 1),
            end_confidence REAL NULL CHECK (end_confidence IS NULL OR end_confidence BETWEEN 0 AND 1),
            status TEXT NOT NULL,
            interruption_reason TEXT NULL,
            FOREIGN KEY (call_id) REFERENCES calls(id) ON DELETE CASCADE,
            FOREIGN KEY (member_id) REFERENCES members(id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS detection_events (
            id TEXT PRIMARY KEY,
            call_id TEXT NOT NULL,
            member_id TEXT NULL,
            event_time_utc TEXT NOT NULL,
            event_type TEXT NOT NULL,
            source TEXT NOT NULL,
            confidence REAL NOT NULL CHECK (confidence BETWEEN 0 AND 1),
            raw_display_name TEXT NULL,
            diagnostic_message TEXT NULL,
            FOREIGN KEY (call_id) REFERENCES calls(id) ON DELETE CASCADE,
            FOREIGN KEY (member_id) REFERENCES members(id) ON DELETE SET NULL
        );

        CREATE TABLE IF NOT EXISTS app_sessions (
            id TEXT PRIMARY KEY,
            started_at_utc TEXT NOT NULL,
            ended_at_utc TEXT NULL,
            shutdown_type TEXT NULL,
            last_heartbeat_at_utc TEXT NOT NULL
        );

        UPDATE attendance_intervals
        SET confirmed_offline_at_utc = last_seen_at_utc,
            duration_ms = MAX(0, CAST((julianday(last_seen_at_utc) - julianday(confirmed_online_at_utc)) * 86400000 AS INTEGER)),
            status = 'Offline',
            interruption_reason = 'CrashRecovery'
        WHERE confirmed_online_at_utc IS NOT NULL
          AND confirmed_offline_at_utc IS NULL;

        UPDATE calls
        SET ended_at_utc = COALESCE(
                (SELECT MAX(ai.last_seen_at_utc) FROM attendance_intervals ai WHERE ai.call_id = calls.id),
                updated_at),
            status = 'Interrupted',
            end_reason = 'CrashRecovery',
            updated_at = COALESCE(
                (SELECT MAX(ai.last_seen_at_utc) FROM attendance_intervals ai WHERE ai.call_id = calls.id),
                updated_at)
        WHERE status = 'Active'
          AND ended_at_utc IS NULL;

        CREATE INDEX IF NOT EXISTS ix_attendance_call_member
            ON attendance_intervals(call_id, member_id);
        CREATE INDEX IF NOT EXISTS ix_detection_events_call_time
            ON detection_events(call_id, event_time_utc);
        CREATE INDEX IF NOT EXISTS ix_members_normalized_name
            ON members(normalized_name);
        CREATE INDEX IF NOT EXISTS ix_app_sessions_unclosed
            ON app_sessions(ended_at_utc, last_heartbeat_at_utc);
        """;
}
