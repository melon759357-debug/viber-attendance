using ViberAttendance.Core.Concurrency;

namespace ViberAttendance.Tests;

public sealed class SingleFlightGateTests
{
    [Fact]
    public void TryEnterSkipsReentrantScanAndAllowsNextScanAfterRelease()
    {
        var gate = new SingleFlightGate();

        Assert.True(gate.TryEnter(out var firstLease));
        Assert.True(gate.IsEntered);
        Assert.False(gate.TryEnter(out var rejectedLease));
        Assert.Null(rejectedLease);

        firstLease!.Dispose();

        Assert.False(gate.IsEntered);
        Assert.True(gate.TryEnter(out var nextLease));
        nextLease!.Dispose();
    }
}
