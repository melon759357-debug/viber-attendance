namespace ViberAttendance.Core.Concurrency;

/// <summary>确保扫描任务不会重入；繁忙时调用方应跳过或合并下一次扫描。</summary>
public sealed class SingleFlightGate
{
    private int _isEntered;

    public bool IsEntered => Volatile.Read(ref _isEntered) == 1;

    public bool TryEnter(out IDisposable? lease)
    {
        if (Interlocked.CompareExchange(ref _isEntered, 1, 0) != 0)
        {
            lease = null;
            return false;
        }

        lease = new Lease(this);
        return true;
    }

    private void Exit() => Volatile.Write(ref _isEntered, 0);

    private sealed class Lease(SingleFlightGate owner) : IDisposable
    {
        private SingleFlightGate? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Exit();
    }
}
