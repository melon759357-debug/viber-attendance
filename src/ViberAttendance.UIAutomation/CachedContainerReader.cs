namespace ViberAttendance.UIAutomation;

/// <summary>
/// 按窗口句柄记住上次找到的名单容器，下一轮只读容器子树。
/// 容器失效、读取报错或读不到人时，回退到全量查找并重新记住。
/// </summary>
internal sealed class CachedContainerReader<TContainer, TResult>
    where TContainer : class
    where TResult : class
{
    private readonly Dictionary<long, TContainer> _containers = [];
    private readonly object _gate = new();

    internal int CachedWindowCount
    {
        get
        {
            lock (_gate)
            {
                return _containers.Count;
            }
        }
    }

    internal TResult? Read(
        long windowHandle,
        Func<TContainer, TResult?> readContainer,
        Func<(TContainer? Container, TResult? Result)> readFull)
    {
        TContainer? cached;
        lock (_gate)
        {
            _containers.TryGetValue(windowHandle, out cached);
        }

        if (cached is not null)
        {
            TResult? fromCache = null;
            try
            {
                fromCache = readContainer(cached);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                fromCache = null;
            }

            if (fromCache is not null)
            {
                return fromCache;
            }

            Forget(windowHandle);
        }

        var (container, result) = readFull();
        if (container is not null && result is not null)
        {
            lock (_gate)
            {
                _containers[windowHandle] = container;
            }
        }

        return result;
    }

    internal void Forget(long windowHandle)
    {
        lock (_gate)
        {
            _containers.Remove(windowHandle);
        }
    }
}
