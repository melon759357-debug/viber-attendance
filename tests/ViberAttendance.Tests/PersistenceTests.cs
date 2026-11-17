using Microsoft.Data.Sqlite;
using ViberAttendance.Core.Attendance;
using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Models;
using ViberAttendance.Infrastructure;
using ViberAttendance.Persistence;

namespace ViberAttendance.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task InitializeCreatesRequiredTablesAndWalDatabase()
    {
        var fixture = CreateFixture();
        try
        {
            var initializer = new SqliteDatabaseInitializer(fixture.Settings, fixture.Logger);
            await initializer.InitializeAsync();

            await using var connection = new SqliteConnection($"Data Source={fixture.Settings.DatabasePath}");
            await connection.OpenAsync();
            await using var tableCommand = connection.CreateCommand();
            tableCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
            var names = new List<string>();
            await using (var reader = await tableCommand.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    names.Add(reader.GetString(0));
                }
            }

            Assert.Contains("calls", names);
            Assert.Contains("members", names);
            Assert.Contains("attendance_intervals", names);
            Assert.Contains("detection_events", names);
            Assert.Contains("app_sessions", names);

            await using var journalCommand = connection.CreateCommand();
            journalCommand.CommandText = "PRAGMA journal_mode;";
            Assert.Equal("wal", (string?)await journalCommand.ExecuteScalarAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task AppSessionNormalStopWritesEndedAtAndShutdownType()
    {
        var fixture = CreateFixture();
        try
        {
            await new SqliteDatabaseInitializer(fixture.Settings, fixture.Logger).InitializeAsync();
            await using var service = new AppSessionService(fixture.Settings, fixture.Clock, fixture.Logger);
            await service.StartAsync();
            Assert.Empty(service.InterruptedSessionIds);

            await service.StopAsync("Normal");

            await using var connection = new SqliteConnection($"Data Source={fixture.Settings.DatabasePath}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT shutdown_type, ended_at_utc FROM app_sessions LIMIT 1;";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("Normal", reader.GetString(0));
            Assert.False(reader.IsDBNull(1));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task StopMonitoringSnapshotsAreSavedAsClosedIntervals()
    {
        var fixture = CreateFixture();
        try
        {
            await new SqliteDatabaseInitializer(fixture.Settings, fixture.Logger).InitializeAsync();
            var repository = new AttendanceRepository(fixture.Settings);
            var started = DateTimeOffset.Parse("2026-07-17T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
            var callId = await repository.StartCallAsync(started, "测试通话", "0x1234");
            var observation = new ParticipantObservation(
                "测试成员",
                "uia-1",
                null,
                DetectionSource.UiAutomation,
                0.9,
                started);
            var member = await repository.ResolveMemberAsync(observation);
            var tracker = new ParticipantPresenceTracker(
                callId,
                2,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5));
            var resolved = new ResolvedParticipantObservation(
                member.MemberId,
                member.DisplayName,
                DetectionSource.UiAutomation,
                0.9,
                started,
                "uia-1");
            tracker.Apply([resolved], started);
            var online = tracker.Apply([resolved with { ObservedAtUtc = started.AddSeconds(2) }], started.AddSeconds(2));
            foreach (var snapshot in online.ChangedParticipants)
            {
                await repository.SaveIntervalAsync(snapshot);
            }

            var stopped = tracker.Stop(started.AddSeconds(12));
            foreach (var snapshot in stopped.ChangedParticipants)
            {
                await repository.SaveIntervalAsync(snapshot);
            }

            await repository.EndCallAsync(callId, started.AddSeconds(12), "ManualStop");
            await repository.FlushAsync();

            var intervals = await repository.GetCallIntervalsAsync(callId);
            var saved = Assert.Single(intervals);
            Assert.Equal("uia-1", saved.StableUiIdentity);
            Assert.Equal("uia-1", Assert.Single(online.Participants).StableUiIdentity);
            Assert.NotNull(saved.OfflineAtUtc);
            Assert.Equal(TimeSpan.FromSeconds(10), saved.Duration);
            var history = Assert.Single(await repository.GetCallsAsync());
            Assert.NotNull(history.EndedAtUtc);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task InterruptedCallIsClosedAtLastSeenTime()
    {
        var fixture = CreateFixture();
        try
        {
            await new SqliteDatabaseInitializer(fixture.Settings, fixture.Logger).InitializeAsync();
            var repository = new AttendanceRepository(fixture.Settings);
            var started = DateTimeOffset.Parse("2026-11-17T18:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
            var callId = await repository.StartCallAsync(started, "测试通话", "0x1234");
            var member = await repository.ResolveMemberAsync(new ParticipantObservation(
                "测试成员",
                "abc=",
                null,
                DetectionSource.UiAutomation,
                0.95,
                started));
            var intervalId = Guid.NewGuid();
            await repository.SaveIntervalAsync(new ParticipantPresenceSnapshot(
                intervalId,
                callId,
                member.MemberId,
                member.DisplayName,
                started,
                started.AddSeconds(2),
                started.AddMinutes(10),
                null,
                MemberPresenceState.Online,
                TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(2),
                TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(2),
                DetectionSource.UiAutomation,
                0.95));

            Assert.Equal(1, await repository.CloseInterruptedCallsAsync());
            Assert.Equal(0, await repository.CloseInterruptedCallsAsync());

            var saved = Assert.Single(await repository.GetCallIntervalsAsync(callId));
            Assert.Equal(started.AddMinutes(10), saved.OfflineAtUtc);
            Assert.Equal(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(2), saved.Duration);
            Assert.Equal(UserFacingStatuses.InterruptedStatus, saved.Status);

            var call = Assert.Single(await repository.GetCallsAsync());
            Assert.Equal(started.AddMinutes(10), call.EndedAtUtc);
            Assert.Equal(UserFacingStatuses.InterruptedStatus, call.Status);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task HistoryExportImportsAgainAndCanDeleteOneIntervalOrAllCalls()
    {
        var fixture = CreateFixture();
        try
        {
            await new SqliteDatabaseInitializer(fixture.Settings, fixture.Logger).InitializeAsync();
            var repository = new AttendanceRepository(fixture.Settings);
            var started = new DateTimeOffset(2026, 11, 17, 18, 0, 0, TimeSpan.Zero);
            var callId = await repository.StartCallAsync(started, "测试通话", "window");
            var member = await repository.ResolveMemberAsync(new ParticipantObservation(
                "测试成员",
                "abc=",
                null,
                DetectionSource.UiAutomation,
                0.95,
                started));
            var intervalId = Guid.NewGuid();
            await repository.SaveIntervalAsync(new ParticipantPresenceSnapshot(
                intervalId,
                callId,
                member.MemberId,
                member.DisplayName,
                started,
                started,
                started.AddMinutes(5),
                started.AddMinutes(5),
                MemberPresenceState.Offline,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5),
                DetectionSource.UiAutomation,
                0.95));
            await repository.EndCallAsync(callId, started.AddMinutes(5), "ManualStop");

            var archive = await repository.ExportHistoryAsync();
            Assert.Equal("viber-attendance-history", archive.Format);
            var exported = Assert.Single(archive.Calls);
            Assert.Equal("测试成员", Assert.Single(exported.Intervals).DisplayName);

            var again = await repository.ImportHistoryAsync(archive);
            Assert.Equal(0, again.ImportedCalls);
            Assert.Equal(1, again.SkippedCalls);

            await repository.DeleteCallAsync(callId);
            var imported = await repository.ImportHistoryAsync(archive);
            Assert.Equal(1, imported.ImportedCalls);
            Assert.Equal("测试成员", Assert.Single(await repository.GetCallIntervalsAsync(callId)).DisplayName);

            await repository.DeleteIntervalAsync(intervalId);
            Assert.Empty(await repository.GetCallIntervalsAsync(callId));

            Assert.Equal(1, await repository.DeleteAllHistoryAsync(null));
            Assert.Empty(await repository.GetCallsAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    private static Fixture CreateFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "ViberAttendance.Tests", Guid.NewGuid().ToString("N"));
        var paths = ApplicationPaths.ForRoot(root);
        paths.EnsureDirectories();
        var settings = new AppSettings();
        settings.Normalize(paths.DefaultDatabaseFile, paths.DefaultExportFolder);
        var clock = new SystemClock();
        var logger = new NullDiagnosticLogger();
        return new Fixture(root, settings, clock, logger);
    }

    private sealed record Fixture(
        string Root,
        AppSettings Settings,
        SystemClock Clock,
        NullDiagnosticLogger Logger);
}
