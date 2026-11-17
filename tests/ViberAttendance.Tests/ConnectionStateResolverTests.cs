using ViberAttendance.Core.Attendance;
using ViberAttendance.Core.Models;

namespace ViberAttendance.Tests;

public sealed class ConnectionStateResolverTests
{
    [Fact]
    public void EmptyParticipantsWithProcessDoesNotBecomeNotRunning()
    {
        var state = ConnectionStateResolver.Resolve(
            processFound: true,
            windowFound: true,
            callDetected: true,
            participantPanelFound: true,
            participantNamesReadable: false,
            readingFailed: true,
            temporaryFailure: false,
            previousState: ConnectionState.CallDetected);

        Assert.Equal(ConnectionState.ParticipantReadingFailed, state);
        Assert.NotEqual(ConnectionState.ViberNotRunning, state);
    }

    [Fact]
    public void WindowWithoutCallIsCallNotDetected()
    {
        var state = ConnectionStateResolver.Resolve(
            processFound: true,
            windowFound: true,
            callDetected: false,
            participantPanelFound: false,
            participantNamesReadable: false,
            readingFailed: false,
            temporaryFailure: false,
            previousState: ConnectionState.ViberNotRunning);

        Assert.Equal(ConnectionState.CallNotDetected, state);
        Assert.Contains("已连接", UserFacingStatuses.GetConnectionStatus(state), StringComparison.Ordinal);
    }

    [Fact]
    public void UiaFailureKeepsConnectedWhenPreviouslyRunning()
    {
        var state = ConnectionStateResolver.Resolve(
            processFound: true,
            windowFound: true,
            callDetected: true,
            participantPanelFound: false,
            participantNamesReadable: false,
            readingFailed: false,
            temporaryFailure: true,
            previousState: ConnectionState.CallNotDetected);

        Assert.Equal(ConnectionState.DetectionTemporarilyFailed, state);
        Assert.NotEqual(ConnectionState.ViberNotRunning, state);
    }

    [Fact]
    public void MissingParticipantPanelIsParticipantPanelNotFound()
    {
        var state = ConnectionStateResolver.Resolve(
            processFound: true,
            windowFound: true,
            callDetected: true,
            participantPanelFound: false,
            participantNamesReadable: false,
            readingFailed: false,
            temporaryFailure: false,
            previousState: ConnectionState.CallDetected);

        Assert.Equal(ConnectionState.ParticipantPanelNotFound, state);
    }

    [Fact]
    public void TemporaryFailureDoesNotClearConnectedStateToNotRunning()
    {
        var state = ConnectionStateResolver.Resolve(
            processFound: false,
            windowFound: false,
            callDetected: false,
            participantPanelFound: false,
            participantNamesReadable: false,
            readingFailed: false,
            temporaryFailure: true,
            previousState: ConnectionState.CallNotDetected);

        Assert.Equal(ConnectionState.DetectionTemporarilyFailed, state);
        Assert.NotEqual(ConnectionState.ViberNotRunning, state);
    }

    [Fact]
    public void ZeroParticipantsNeverMeansViberNotRunning()
    {
        Assert.NotEqual(
            ConnectionState.ViberNotRunning,
            ConnectionStateResolver.Resolve(
                true, true, true, true, false, true, false, ConnectionState.Monitoring));
    }

    [Fact]
    public void MinimizedWindowStillCountsAsConnectedViaDiscoveryHelper()
    {
        var state = ConnectionStateResolver.ResolveFromDiscovery(
            processCount: 1,
            hasWindow: true,
            hasCall: false,
            previousState: ConnectionState.ViberNotRunning);

        Assert.Equal(ConnectionState.CallNotDetected, state);
    }

    [Fact]
    public void PrivacyRedactionUsesFirstCharAndLength()
    {
        var redacted = UserFacingStatuses.RedactDisplayName("Anna");
        Assert.Equal("A***", redacted.Masked);
        Assert.Equal(4, redacted.Length);
    }
}
