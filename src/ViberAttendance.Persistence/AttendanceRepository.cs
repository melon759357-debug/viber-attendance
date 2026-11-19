using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Contracts;
using ViberAttendance.Core.Models;

namespace ViberAttendance.Persistence;

public sealed class AttendanceRepository(AppSettings settings) : IAttendanceRepository
{
    private const string InterruptedStatus = UserFacingStatuses.InterruptedStatus;

    private readonly SqliteConnectionFactory _connectionFactory = new(settings);

    public async Task<Guid> StartCallAsync(
        DateTimeOffset startedAtUtc,
        string title,
        string viberWindowIdentity,
        CancellationToken cancellationToken = default)
    {
        var callId = Guid.NewGuid();
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO calls
                (id, started_at_utc, ended_at_utc, title, viber_window_identity, status,
                 end_reason, created_at, updated_at)
            VALUES
                ($id, $started, NULL, $title, $window, 'Active', NULL, $now, $now);
            """;
        command.Parameters.AddWithValue("$id", callId.ToString("D"));
        command.Parameters.AddWithValue("$started", Format(startedAtUtc));
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$window", viberWindowIdentity);
        command.Parameters.AddWithValue("$now", Format(startedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return callId;
    }

    public async Task<ParticipantIdentity> ResolveMemberAsync(
        ParticipantObservation observation,
        CancellationToken cancellationToken = default)
    {
        var displayName = observation.RawDisplayName.Trim();
        var normalizedName = NormalizeName(displayName);
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var find = connection.CreateCommand())
        {
            find.CommandText = """
                SELECT id, display_name
                FROM members
                WHERE ($uia <> '' AND uia_identity = $uia)
                   OR normalized_name = $normalized
                ORDER BY CASE WHEN uia_identity = $uia THEN 0 ELSE 1 END
                LIMIT 1;
                """;
            find.Parameters.AddWithValue("$uia", observation.StableUiIdentity ?? string.Empty);
            find.Parameters.AddWithValue("$normalized", normalizedName);
            await using var reader = await find.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var memberId = Guid.Parse(reader.GetString(0));
                return new ParticipantIdentity(memberId, displayName, false);
            }
        }

        var id = Guid.NewGuid();
        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO members
                (id, display_name, normalized_name, avatar_hash, uia_identity, alias, created_at, updated_at)
            VALUES
                ($id, $display, $normalized, $avatar, $uia, NULL, $now, $now);
            """;
        insert.Parameters.AddWithValue("$id", id.ToString("D"));
        insert.Parameters.AddWithValue("$display", displayName);
        insert.Parameters.AddWithValue("$normalized", normalizedName);
        insert.Parameters.AddWithValue("$avatar", (object?)observation.AvatarHash ?? DBNull.Value);
        insert.Parameters.AddWithValue("$uia", (object?)observation.StableUiIdentity ?? DBNull.Value);
        insert.Parameters.AddWithValue("$now", Format(observation.ObservedAtUtc));
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return new ParticipantIdentity(id, displayName, true);
    }

    public async Task SaveIntervalAsync(
        ParticipantPresenceSnapshot participant,
        CancellationToken cancellationToken = default)
    {
        if (participant.OnlineAtUtc is null)
        {
            return;
        }

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO attendance_intervals
                (id, call_id, member_id, first_seen_at_utc, confirmed_online_at_utc,
                 last_seen_at_utc, confirmed_offline_at_utc, duration_ms,
                 start_detection_source, end_detection_source, start_confidence,
                 end_confidence, status, interruption_reason)
            VALUES
                ($id, $call, $member, $firstSeen, $online, $lastSeen, $offline, $duration,
                 $source, $endSource, $confidence, $endConfidence, $status, NULL)
            ON CONFLICT(id) DO UPDATE SET
                last_seen_at_utc = excluded.last_seen_at_utc,
                confirmed_offline_at_utc = excluded.confirmed_offline_at_utc,
                duration_ms = excluded.duration_ms,
                end_detection_source = excluded.end_detection_source,
                end_confidence = excluded.end_confidence,
                status = excluded.status;
            """;
        command.Parameters.AddWithValue("$id", participant.IntervalId.ToString("D"));
        command.Parameters.AddWithValue("$call", participant.CallId.ToString("D"));
        command.Parameters.AddWithValue("$member", participant.MemberId.ToString("D"));
        command.Parameters.AddWithValue("$firstSeen", Format(participant.FirstSeenAtUtc));
        command.Parameters.AddWithValue("$online", Format(participant.OnlineAtUtc.Value));
        command.Parameters.AddWithValue("$lastSeen", Format(participant.LastSeenAtUtc));
        command.Parameters.AddWithValue("$offline", participant.OfflineAtUtc is DateTimeOffset offline
            ? Format(offline)
            : DBNull.Value);
        command.Parameters.AddWithValue("$duration", (long)Math.Max(0, participant.CurrentDuration.TotalMilliseconds));
        command.Parameters.AddWithValue("$source", participant.StartSource.ToString());
        command.Parameters.AddWithValue("$endSource", participant.OfflineAtUtc is null
            ? DBNull.Value
            : participant.StartSource.ToString());
        command.Parameters.AddWithValue("$confidence", participant.StartConfidence);
        command.Parameters.AddWithValue("$endConfidence", participant.OfflineAtUtc is null
            ? DBNull.Value
            : participant.StartConfidence);
        command.Parameters.AddWithValue("$status", participant.State.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task EndCallAsync(
        Guid callId,
        DateTimeOffset endedAtUtc,
        string reason,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE calls
            SET ended_at_utc = $ended,
                status = 'Completed',
                end_reason = $reason,
                updated_at = $ended
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$ended", Format(endedAtUtc));
        command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$id", callId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CloseInterruptedCallsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var calls = new List<(string Id, DateTimeOffset StartedAt)>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT id, started_at_utc FROM calls WHERE status = 'Active';";
            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                calls.Add((reader.GetString(0), Parse(reader.GetString(1))));
            }
        }

        foreach (var (callId, startedAt) in calls)
        {
            var openIntervals = new List<(string Id, DateTimeOffset OnlineAt, DateTimeOffset LastSeen)>();
            var endedAt = startedAt;
            await using (var intervals = connection.CreateCommand())
            {
                intervals.Transaction = transaction;
                intervals.CommandText = """
                    SELECT id, confirmed_online_at_utc, last_seen_at_utc, confirmed_offline_at_utc
                    FROM attendance_intervals
                    WHERE call_id = $call;
                    """;
                intervals.Parameters.AddWithValue("$call", callId);
                await using var reader = await intervals.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var lastSeen = Parse(reader.GetString(2));
                    var offline = reader.IsDBNull(3) ? (DateTimeOffset?)null : Parse(reader.GetString(3));
                    var latest = offline is DateTimeOffset value && value > lastSeen ? value : lastSeen;
                    if (latest > endedAt)
                    {
                        endedAt = latest;
                    }

                    if (offline is null && !reader.IsDBNull(1))
                    {
                        openIntervals.Add((reader.GetString(0), Parse(reader.GetString(1)), lastSeen));
                    }
                }
            }

            foreach (var interval in openIntervals)
            {
                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE attendance_intervals
                    SET confirmed_offline_at_utc = $offline,
                        duration_ms = $duration,
                        end_detection_source = 'AppInterrupted',
                        status = $status,
                        interruption_reason = 'AppInterrupted'
                    WHERE id = $id;
                    """;
                update.Parameters.AddWithValue("$offline", Format(interval.LastSeen));
                update.Parameters.AddWithValue(
                    "$duration",
                    (long)Math.Max(0, (interval.LastSeen - interval.OnlineAt).TotalMilliseconds));
                update.Parameters.AddWithValue("$status", InterruptedStatus);
                update.Parameters.AddWithValue("$id", interval.Id);
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var endCall = connection.CreateCommand();
            endCall.Transaction = transaction;
            endCall.CommandText = """
                UPDATE calls
                SET ended_at_utc = $ended,
                    status = $status,
                    end_reason = 'AppInterrupted',
                    updated_at = $ended
                WHERE id = $id;
                """;
            endCall.Parameters.AddWithValue("$ended", Format(endedAt));
            endCall.Parameters.AddWithValue("$status", InterruptedStatus);
            endCall.Parameters.AddWithValue("$id", callId);
            await endCall.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return calls.Count;
    }

    public async Task<IReadOnlyList<CallHistoryRecord>> GetCallsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<CallHistoryRecord>();
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.id, c.started_at_utc, c.ended_at_utc, c.status,
                   COUNT(DISTINCT ai.member_id)
            FROM calls c
            LEFT JOIN attendance_intervals ai ON ai.call_id = c.id
            GROUP BY c.id, c.started_at_utc, c.ended_at_utc, c.status
            ORDER BY c.started_at_utc DESC;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var started = Parse(reader.GetString(1));
            DateTimeOffset? ended = reader.IsDBNull(2) ? null : Parse(reader.GetString(2));
            result.Add(new CallHistoryRecord(
                Guid.Parse(reader.GetString(0)),
                started,
                ended,
                reader.GetInt32(4),
                (ended ?? DateTimeOffset.UtcNow) - started,
                reader.GetString(3)));
        }

        return result;
    }

    public async Task<IReadOnlyList<AttendanceIntervalRecord>> GetCallIntervalsAsync(
        Guid callId,
        CancellationToken cancellationToken = default)
    {
        var result = new List<AttendanceIntervalRecord>();
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ai.id, ai.member_id, m.display_name, ai.first_seen_at_utc,
                   ai.confirmed_online_at_utc, ai.last_seen_at_utc,
                   ai.confirmed_offline_at_utc, COALESCE(ai.duration_ms, 0), ai.status,
                   m.uia_identity
            FROM attendance_intervals ai
            INNER JOIN members m ON m.id = ai.member_id
            WHERE ai.call_id = $call
            ORDER BY ai.confirmed_online_at_utc, m.display_name;
            """;
        command.Parameters.AddWithValue("$call", callId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var onlineAt = reader.IsDBNull(4) ? (DateTimeOffset?)null : Parse(reader.GetString(4));
            var offlineAt = reader.IsDBNull(6) ? (DateTimeOffset?)null : Parse(reader.GetString(6));
            var duration = offlineAt is null && onlineAt is DateTimeOffset online
                ? DateTimeOffset.UtcNow - online
                : TimeSpan.FromMilliseconds(reader.GetInt64(7));
            if (duration < TimeSpan.Zero)
            {
                duration = TimeSpan.Zero;
            }

            result.Add(new AttendanceIntervalRecord(
                Guid.Parse(reader.GetString(0)),
                callId,
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                Parse(reader.GetString(3)),
                onlineAt,
                Parse(reader.GetString(5)),
                offlineAt,
                duration,
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return result;
    }

    public async Task DeleteCallAsync(Guid callId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM calls WHERE id = $id;";
        command.Parameters.AddWithValue("$id", callId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await DeleteUnusedMembersAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteIntervalAsync(Guid intervalId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM attendance_intervals WHERE id = $id;";
        command.Parameters.AddWithValue("$id", intervalId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await DeleteUnusedMembersAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> DeleteAllHistoryAsync(Guid? exceptCallId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = exceptCallId is null
            ? "DELETE FROM calls;"
            : "DELETE FROM calls WHERE id <> $keep;";
        if (exceptCallId is Guid keep)
        {
            command.Parameters.AddWithValue("$keep", keep.ToString("D"));
        }

        var deleted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await DeleteUnusedMembersAsync(connection, cancellationToken).ConfigureAwait(false);
        return deleted;
    }

    public async Task<HistoryArchive> ExportHistoryAsync(CancellationToken cancellationToken = default)
    {
        var archive = new HistoryArchive();
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.id, c.started_at_utc, c.ended_at_utc, c.status, c.title,
                   ai.id, ai.member_id, m.display_name, m.uia_identity,
                   ai.first_seen_at_utc, ai.confirmed_online_at_utc, ai.last_seen_at_utc,
                   ai.confirmed_offline_at_utc, COALESCE(ai.duration_ms, 0), ai.status
            FROM calls c
            LEFT JOIN attendance_intervals ai ON ai.call_id = c.id
            LEFT JOIN members m ON m.id = ai.member_id
            ORDER BY c.started_at_utc, ai.confirmed_online_at_utc;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var calls = new Dictionary<string, HistoryArchiveCall>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var callId = reader.GetString(0);
            if (!calls.TryGetValue(callId, out var call))
            {
                call = new HistoryArchiveCall
                {
                    Id = Guid.Parse(callId),
                    StartedAtUtc = Parse(reader.GetString(1)),
                    EndedAtUtc = reader.IsDBNull(2) ? null : Parse(reader.GetString(2)),
                    Status = reader.GetString(3),
                    Title = reader.GetString(4)
                };
                calls[callId] = call;
                archive.Calls.Add(call);
            }

            if (reader.IsDBNull(5))
            {
                continue;
            }

            call.Intervals.Add(new HistoryArchiveInterval
            {
                Id = Guid.Parse(reader.GetString(5)),
                MemberId = Guid.Parse(reader.GetString(6)),
                DisplayName = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                StableUiIdentity = reader.IsDBNull(8) ? null : reader.GetString(8),
                FirstSeenAtUtc = Parse(reader.GetString(9)),
                OnlineAtUtc = reader.IsDBNull(10) ? null : Parse(reader.GetString(10)),
                LastSeenAtUtc = Parse(reader.GetString(11)),
                OfflineAtUtc = reader.IsDBNull(12) ? null : Parse(reader.GetString(12)),
                DurationMs = reader.GetInt64(13),
                Status = reader.GetString(14)
            });
        }

        return archive;
    }

    public async Task<HistoryImportResult> ImportHistoryAsync(
        HistoryArchive archive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (!string.Equals(archive.Format, "viber-attendance-history", StringComparison.Ordinal) || archive.Version != 1)
        {
            throw new InvalidOperationException("这不是本软件的历史记录文件。");
        }

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var imported = 0;
        var skipped = 0;
        foreach (var call in archive.Calls)
        {
            await using var exists = connection.CreateCommand();
            exists.Transaction = transaction;
            exists.CommandText = "SELECT 1 FROM calls WHERE id = $id;";
            exists.Parameters.AddWithValue("$id", call.Id.ToString("D"));
            if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                skipped++;
                continue;
            }

            var now = Format(call.StartedAtUtc);
            await using var insertCall = connection.CreateCommand();
            insertCall.Transaction = transaction;
            insertCall.CommandText = """
                INSERT INTO calls
                    (id, started_at_utc, ended_at_utc, title, viber_window_identity, status,
                     end_reason, created_at, updated_at)
                VALUES
                    ($id, $started, $ended, $title, 'imported', $status, NULL, $now, $now);
                """;
            insertCall.Parameters.AddWithValue("$id", call.Id.ToString("D"));
            insertCall.Parameters.AddWithValue("$started", Format(call.StartedAtUtc));
            insertCall.Parameters.AddWithValue("$ended", call.EndedAtUtc is DateTimeOffset ended ? Format(ended) : DBNull.Value);
            insertCall.Parameters.AddWithValue("$title", string.IsNullOrWhiteSpace(call.Title) ? "Viber在线记录" : call.Title);
            insertCall.Parameters.AddWithValue("$status", string.IsNullOrWhiteSpace(call.Status) ? "Completed" : call.Status);
            insertCall.Parameters.AddWithValue("$now", now);
            await insertCall.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            foreach (var interval in call.Intervals)
            {
                await EnsureMemberAsync(connection, transaction, interval, cancellationToken).ConfigureAwait(false);
                await using var insertInterval = connection.CreateCommand();
                insertInterval.Transaction = transaction;
                insertInterval.CommandText = """
                    INSERT INTO attendance_intervals
                        (id, call_id, member_id, first_seen_at_utc, confirmed_online_at_utc,
                         last_seen_at_utc, confirmed_offline_at_utc, duration_ms,
                         start_detection_source, end_detection_source, start_confidence,
                         end_confidence, status, interruption_reason)
                    VALUES
                        ($id, $call, $member, $firstSeen, $online, $lastSeen, $offline, $duration,
                         'UiAutomation', $endSource, 0.9, $endConfidence, $status, NULL);
                    """;
                insertInterval.Parameters.AddWithValue("$id", interval.Id.ToString("D"));
                insertInterval.Parameters.AddWithValue("$call", call.Id.ToString("D"));
                insertInterval.Parameters.AddWithValue("$member", interval.MemberId.ToString("D"));
                insertInterval.Parameters.AddWithValue("$firstSeen", Format(interval.FirstSeenAtUtc));
                insertInterval.Parameters.AddWithValue("$online", interval.OnlineAtUtc is DateTimeOffset online ? Format(online) : DBNull.Value);
                insertInterval.Parameters.AddWithValue("$lastSeen", Format(interval.LastSeenAtUtc));
                insertInterval.Parameters.AddWithValue("$offline", interval.OfflineAtUtc is DateTimeOffset offline ? Format(offline) : DBNull.Value);
                insertInterval.Parameters.AddWithValue("$duration", Math.Max(0, interval.DurationMs));
                insertInterval.Parameters.AddWithValue("$endSource", interval.OfflineAtUtc is null ? DBNull.Value : "UiAutomation");
                insertInterval.Parameters.AddWithValue("$endConfidence", interval.OfflineAtUtc is null ? DBNull.Value : 0.9);
                insertInterval.Parameters.AddWithValue("$status", string.IsNullOrWhiteSpace(interval.Status) ? "Offline" : interval.Status);
                await insertInterval.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            imported++;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new HistoryImportResult(imported, skipped);
    }

    private static async Task EnsureMemberAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        HistoryArchiveInterval interval,
        CancellationToken cancellationToken)
    {
        await using var exists = connection.CreateCommand();
        exists.Transaction = transaction;
        exists.CommandText = "SELECT 1 FROM members WHERE id = $id;";
        exists.Parameters.AddWithValue("$id", interval.MemberId.ToString("D"));
        if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        var now = Format(interval.FirstSeenAtUtc);
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO members
                (id, display_name, normalized_name, avatar_hash, uia_identity, alias, created_at, updated_at)
            VALUES
                ($id, $display, $normalized, NULL, $uia, NULL, $now, $now);
            """;
        insert.Parameters.AddWithValue("$id", interval.MemberId.ToString("D"));
        insert.Parameters.AddWithValue("$display", interval.DisplayName.Trim());
        insert.Parameters.AddWithValue("$normalized", NormalizeName(interval.DisplayName));
        insert.Parameters.AddWithValue("$uia", (object?)interval.StableUiIdentity ?? DBNull.Value);
        insert.Parameters.AddWithValue("$now", now);
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeleteUnusedMembersAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM members
            WHERE id NOT IN (SELECT member_id FROM attendance_intervals);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeName(string displayName) =>
        displayName.Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
