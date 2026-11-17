using ViberAttendance.App.Services;
using ViberAttendance.App.ViewModels;
using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Contracts;
using ViberAttendance.Core.Models;

namespace ViberAttendance.Tests;

public sealed class MonitoringConnectionStateTests
{
    [Fact]
    public async Task WhenViberNotRunning_StateIsViberNotRunning()
    {
        var locator = new FakeWindowLocator(new ViberWindowDiscoveryResult(0, 0, 0, [], [], [], null, null));
        await using var coordinator = CreateCoordinator(locator);

        await coordinator.StartAsync();
        await WaitForAsync(() => coordinator.ConnectionState == ConnectionState.ViberNotRunning);

        Assert.Equal(ConnectionState.ViberNotRunning, coordinator.ConnectionState);
        Assert.Equal(UserFacingStatuses.ViberNotRunning, UserFacingStatuses.GetConnectionStatus(coordinator.ConnectionState));
    }

    [Fact]
    public async Task WhenProcessFound_DoesNotShowNotConnected()
    {
        var process = new ViberProcessInfo(1, "Viber", @"C:\Viber\Viber.exe", "Viber", "Viber", 0, "", ["ProcessName"]);
        var locator = new FakeWindowLocator(new ViberWindowDiscoveryResult(1, 0, 0, [process], [], [], null, null));
        await using var coordinator = CreateCoordinator(locator);

        await coordinator.StartAsync();
        await WaitForAsync(() => coordinator.ConnectionState == ConnectionState.ViberProcessFound);

        Assert.NotEqual(UserFacingStatuses.ViberNotRunning, UserFacingStatuses.GetConnectionStatus(coordinator.ConnectionState));
        Assert.Equal(UserFacingStatuses.ViberProcessFound, UserFacingStatuses.GetConnectionStatus(coordinator.ConnectionState));
    }

    [Fact]
    public async Task WhenWindowFoundWithoutCall_ShowsConnectedNoCall()
    {
        var process = new ViberProcessInfo(1, "Viber", @"C:\Viber\Viber.exe", "Viber", "Viber", 0, "Viber", ["ProcessName"]);
        var window = CreateWindow(10, callSignals: 0);
        var locator = new FakeWindowLocator(new ViberWindowDiscoveryResult(
            1, 1, 1, [process], [window], [window], window, null));
        await using var coordinator = CreateCoordinator(locator);

        await coordinator.StartAsync();
        await WaitForAsync(() => coordinator.ConnectionState == ConnectionState.CallNotDetected);

        Assert.Equal(UserFacingStatuses.CallNotDetected, UserFacingStatuses.GetConnectionStatus(coordinator.ConnectionState));
        Assert.Contains("已连接", UserFacingStatuses.GetConnectionStatus(coordinator.ConnectionState), StringComparison.Ordinal);
        Assert.NotEqual(ConnectionState.ViberNotRunning, coordinator.ConnectionState);
    }

    [Fact]
    public async Task WhenCallWithoutParticipants_IsParticipantPanelNotFound()
    {
        var process = new ViberProcessInfo(1, "Viber", @"C:\Viber\Viber.exe", "Viber", "Viber", 0, "Viber", ["ProcessName"]);
        var window = CreateWindow(10, callSignals: 3);
        var locator = new FakeWindowLocator(new ViberWindowDiscoveryResult(
            1, 1, 1, [process], [window], [window], window, window));
        var detector = new FakeParticipantDetector(new ParticipantDetectionResult(false, []));
        await using var coordinator = CreateCoordinator(locator, detector);

        await coordinator.StartAsync();
        await WaitForAsync(() => coordinator.ConnectionState == ConnectionState.ParticipantPanelNotFound);

        Assert.Equal(ConnectionState.ParticipantPanelNotFound, coordinator.ConnectionState);
        Assert.NotEqual(ConnectionState.ViberNotRunning, coordinator.ConnectionState);
    }

    [Fact]
    public async Task WhenUiaAccessDenied_KeepsConnected()
    {
        var process = new ViberProcessInfo(1, "Viber", @"C:\Viber\Viber.exe", "Viber", "Viber", 0, "Viber", ["ProcessName"]);
        var window = CreateWindow(10, callSignals: 3);
        var locator = new FakeWindowLocator(new ViberWindowDiscoveryResult(
            1, 1, 1, [process], [window], [window], window, window));
        var detector = new FakeParticipantDetector(new ParticipantDetectionResult(false, [], IsUiAutomationAccessDenied: true));
        await using var coordinator = CreateCoordinator(locator, detector);

        await coordinator.StartAsync();
        await WaitForAsync(() => coordinator.ConnectionState == ConnectionState.ParticipantReadingFailed);

        Assert.Equal(ConnectionState.ParticipantReadingFailed, coordinator.ConnectionState);
        Assert.NotEqual(ConnectionState.ViberNotRunning, coordinator.ConnectionState);
    }

    [Fact]
    public async Task EmptyParticipantList_DoesNotBecomeNotRunning()
    {
        var process = new ViberProcessInfo(1, "Viber", @"C:\Viber\Viber.exe", "Viber", "Viber", 0, "Viber", ["ProcessName"]);
        var window = CreateWindow(10, callSignals: 3);
        var locator = new FakeWindowLocator(new ViberWindowDiscoveryResult(
            1, 1, 1, [process], [window], [window], window, window));
        var detector = new FakeParticipantDetector(new ParticipantDetectionResult(true, []));
        await using var coordinator = CreateCoordinator(locator, detector);

        await coordinator.StartAsync();
        await WaitForAsync(() => coordinator.ConnectionState == ConnectionState.ParticipantReadingFailed);

        Assert.NotEqual(ConnectionState.ViberNotRunning, coordinator.ConnectionState);
    }

    [Fact]
    public async Task DetectionException_RestoresIsDetectingFalseAndKeepsRetryable()
    {
        var locator = new ThrowingWindowLocator();
        await using var coordinator = CreateCoordinator(locator);

        await coordinator.StartAsync();
        await WaitForAsync(() => coordinator.ConnectionState == ConnectionState.DetectionTemporarilyFailed);
        await WaitForAsync(() => !coordinator.IsDetecting);

        Assert.False(coordinator.IsDetecting);
    }

    [Fact]
    public async Task SingleFailureDoesNotClearPreviousConnectedStateImmediately()
    {
        var process = new ViberProcessInfo(1, "Viber", @"C:\Viber\Viber.exe", "Viber", "Viber", 0, "Viber", ["ProcessName"]);
        var window = CreateWindow(10, callSignals: 0);
        var locator = new SequenceWindowLocator(
            new ViberWindowDiscoveryResult(1, 1, 1, [process], [window], [window], window, null),
            new ViberWindowDiscoveryResult(0, 0, 0, [], [], [], null, null));
        await using var coordinator = CreateCoordinator(locator);

        await coordinator.StartAsync();
        await WaitForAsync(() => coordinator.ConnectionState == ConnectionState.CallNotDetected);
        await coordinator.RetryDetectionAsync();
        await WaitForAsync(() =>
            coordinator.ConnectionState is ConnectionState.DetectionTemporarilyFailed
                or ConnectionState.CallNotDetected
                or ConnectionState.ViberNotRunning);

        Assert.NotEqual(ConnectionState.ViberNotRunning, coordinator.ConnectionState);
    }

    [Fact]
    public async Task AutoLoopConnectsAfterViberAppears()
    {
        var process = new ViberProcessInfo(1, "Viber", @"C:\Viber\Viber.exe", "Viber", "Viber", 0, "Viber", ["ProcessName"]);
        var window = CreateWindow(10, callSignals: 0);
        var locator = new SequenceWindowLocator(
            new ViberWindowDiscoveryResult(0, 0, 0, [], [], [], null, null),
            new ViberWindowDiscoveryResult(1, 1, 1, [process], [window], [window], window, null));
        await using var coordinator = CreateCoordinator(locator);

        await coordinator.StartAsync();
        await WaitForAsync(() => coordinator.ConnectionState == ConnectionState.ViberNotRunning);
        await WaitForAsync(() => coordinator.ConnectionState == ConnectionState.CallNotDetected, TimeSpan.FromSeconds(8));

        Assert.Equal(ConnectionState.CallNotDetected, coordinator.ConnectionState);
    }

    [Fact]
    public async Task ConcurrentRetryDoesNotRunInParallel()
    {
        var locator = new SlowWindowLocator(TimeSpan.FromMilliseconds(500));
        await using var coordinator = CreateCoordinator(locator);

        await coordinator.StartAsync();
        await WaitForAsync(() => locator.CallCount >= 1);

        var first = coordinator.RetryDetectionAsync();
        var second = coordinator.RetryDetectionAsync();
        await Task.WhenAll(first, second);
        await WaitForAsync(() => !coordinator.IsDetecting);

        Assert.Equal(1, locator.MaxConcurrency);
    }

    [Fact]
    public async Task DetectionException_RetryCommandRemainsExecutable()
    {
        var locator = new ThrowingWindowLocator();
        await using var coordinator = CreateCoordinator(locator);
        var viewModel = new MainViewModel(coordinator, new FakeConfirmationDialog());

        await viewModel.RetryDetectionCommand.ExecuteAsyncIfPossible();
        await WaitForAsync(() => !viewModel.IsDetecting && coordinator.ConnectionState == ConnectionState.DetectionTemporarilyFailed);

        Assert.True(viewModel.RetryDetectionCommand.CanExecute(null));
    }

    [Fact]
    public void RetryDetectionCommand_CanExecuteWhenNotDetecting()
    {
        var locator = new FakeWindowLocator(new ViberWindowDiscoveryResult(0, 0, 0, [], [], [], null, null));
        var coordinator = CreateCoordinator(locator);
        var viewModel = new MainViewModel(coordinator, new FakeConfirmationDialog());

        Assert.True(viewModel.RetryDetectionCommand.CanExecute(null));
    }

    [Fact]
    public void FullDiagnosticFileName_UsesExpectedPrefix()
    {
        var name = FullDiagnosticFileNames.CreateJsonFileName(new DateTimeOffset(2026, 7, 18, 1, 2, 3, TimeSpan.Zero));
        Assert.Equal("viber-full-diagnostic-20260718-010203.json", name);
    }

    private static MonitoringCoordinator CreateCoordinator(
        IViberWindowLocator locator,
        IUiAutomationParticipantDetector? detector = null)
    {
        var settings = new AppSettings
        {
            FastScanSeconds = 1,
            OfflineWaitSeconds = 5,
            ExportFolder = Path.Combine(Path.GetTempPath(), "ViberAttendance.Tests", Guid.NewGuid().ToString("N"))
        };
        return new MonitoringCoordinator(
            settings,
            locator,
            detector ?? new FakeParticipantDetector(new ParticipantDetectionResult(false, [])),
            new FakeAttendanceRepository(),
            new FakeClock(),
            new FakeLogger());
    }

    private static ViberWindowCandidate CreateWindow(long hwnd, int callSignals) =>
        new(
            hwnd,
            1,
            "Viber",
            "Viber",
            "Qt5152QWindowIcon",
            true,
            true,
            false,
            false,
            false,
            false,
            false,
            1000,
            700,
            new PixelRect(0, 0, 1000, 700),
            1000,
            700,
            true,
            "Viber",
            "Qt5152QWindowIcon",
            "ControlType.Window",
            (int)hwnd,
            10,
            2,
            callSignals,
            false,
            callSignals > 0 ? ViberWindowCandidateType.CallWindow : ViberWindowCandidateType.MainWindow,
            50,
            ViberWindowRejectionReasons.Accepted);

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.True(condition(), "Condition was not met before timeout.");
    }

    private sealed class FakeWindowLocator(ViberWindowDiscoveryResult result) : IViberWindowLocator
    {
        public Task<ViberWindowDiscoveryResult> FindAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class SequenceWindowLocator(params ViberWindowDiscoveryResult[] results) : IViberWindowLocator
    {
        private int _index;

        public Task<ViberWindowDiscoveryResult> FindAsync(CancellationToken cancellationToken = default)
        {
            var result = results[Math.Min(_index, results.Length - 1)];
            _index++;
            return Task.FromResult(result);
        }
    }

    private sealed class SlowWindowLocator(TimeSpan delay) : IViberWindowLocator
    {
        private int _inFlight;

        public int CallCount { get; private set; }
        public int MaxConcurrency { get; private set; }

        public async Task<ViberWindowDiscoveryResult> FindAsync(CancellationToken cancellationToken = default)
        {
            var current = Interlocked.Increment(ref _inFlight);
            MaxConcurrency = Math.Max(MaxConcurrency, current);
            CallCount++;
            try
            {
                await Task.Delay(delay, cancellationToken);
                return new ViberWindowDiscoveryResult(0, 0, 0, [], [], [], null, null);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    private sealed class ThrowingWindowLocator : IViberWindowLocator
    {
        public Task<ViberWindowDiscoveryResult> FindAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<ViberWindowDiscoveryResult>(new InvalidOperationException("boom"));
    }

    private sealed class FakeParticipantDetector(ParticipantDetectionResult result) : IUiAutomationParticipantDetector
    {
        public Task<ParticipantDetectionResult> DetectAsync(
            ViberWindowCandidate window,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class FakeAttendanceRepository : IAttendanceRepository
    {
        public Task<Guid> StartCallAsync(DateTimeOffset startedAtUtc, string title, string viberWindowIdentity, CancellationToken cancellationToken = default) =>
            Task.FromResult(Guid.NewGuid());

        public Task<ParticipantIdentity> ResolveMemberAsync(ParticipantObservation observation, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ParticipantIdentity(Guid.NewGuid(), observation.RawDisplayName, true));

        public Task SaveIntervalAsync(ParticipantPresenceSnapshot participant, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task EndCallAsync(Guid callId, DateTimeOffset endedAtUtc, string reason, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<int> CloseInterruptedCallsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<CallHistoryRecord>> GetCallsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CallHistoryRecord>>([]);

        public Task<IReadOnlyList<AttendanceIntervalRecord>> GetCallIntervalsAsync(Guid callId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AttendanceIntervalRecord>>([]);

        public Task DeleteCallAsync(Guid callId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteIntervalAsync(Guid intervalId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<int> DeleteAllHistoryAsync(Guid? exceptCallId, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<HistoryArchive> ExportHistoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HistoryArchive());

        public Task<HistoryImportResult> ImportHistoryAsync(HistoryArchive archive, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HistoryImportResult(0, 0));

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UtcNow;
        public long GetMonotonicTimestamp() => UtcNow.UtcTicks;
        public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp) =>
            TimeSpan.FromTicks(endTimestamp - startTimestamp);
    }

    private sealed class FakeLogger : IDiagnosticLogger
    {
        public Task WriteAsync(
            DiagnosticLogLevel level,
            string eventName,
            string message,
            Exception? exception = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeConfirmationDialog : IConfirmationDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message, string confirmText) =>
            Task.FromResult(false);
    }
}

internal static class CommandTestExtensions
{
    public static async Task ExecuteAsyncIfPossible(this System.Windows.Input.ICommand command)
    {
        Assert.True(command.CanExecute(null));
        command.Execute(null);
        await Task.Delay(300);
    }
}
