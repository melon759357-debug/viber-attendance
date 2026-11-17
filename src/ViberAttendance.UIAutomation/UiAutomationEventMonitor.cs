using System.Windows.Automation;
using ViberAttendance.Core.Contracts;
using ViberAttendance.Core.Models;

namespace ViberAttendance.UIAutomation;

public sealed class UiAutomationEventMonitor : IUiAutomationEventMonitor
{
    private readonly IClock _clock;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly StructureChangedEventHandler _structureHandler;
    private readonly AutomationPropertyChangedEventHandler _propertyHandler;
    private AutomationElement? _root;
    private long _windowHandle;
    private long _eventCount;
    private bool _disposed;

    public UiAutomationEventMonitor(IClock clock)
    {
        _clock = clock;
        _structureHandler = OnStructureChanged;
        _propertyHandler = OnPropertyChanged;
    }

    public event EventHandler<UiAutomationEventArgs>? EventReceived;

    public long EventCount => Interlocked.Read(ref _eventCount);

    public async Task StartAsync(ViberWindowCandidate window, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_root is not null && _windowHandle == window.WindowHandle)
            {
                return;
            }

            await StaTaskRunner.RunAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                RemoveHandlersCore();
                var root = AutomationElement.FromHandle(new nint(window.WindowHandle));
                Automation.AddStructureChangedEventHandler(root, TreeScope.Subtree, _structureHandler);
                try
                {
                    Automation.AddAutomationPropertyChangedEventHandler(
                        root,
                        TreeScope.Subtree,
                        _propertyHandler,
                        AutomationElement.NameProperty,
                        AutomationElement.IsOffscreenProperty,
                        AutomationElement.BoundingRectangleProperty);
                }
                catch
                {
                    Automation.RemoveStructureChangedEventHandler(root, _structureHandler);
                    throw;
                }
                _root = root;
                _windowHandle = window.WindowHandle;
                return true;
            }).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StaTaskRunner.RunAsync(() =>
            {
                RemoveHandlersCore();
                return true;
            }).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _disposed = true;
        _lifecycleGate.Dispose();
    }

    private void OnStructureChanged(object sender, StructureChangedEventArgs args)
    {
        Raise("StructureChanged", sender, args.StructureChangeType.ToString());
    }

    private void OnPropertyChanged(object sender, AutomationPropertyChangedEventArgs args)
    {
        // 不记录 NewValue，防止名称变化事件把参与者姓名或聊天内容写入日志。
        Raise("PropertyChanged", sender, args.Property.ProgrammaticName);
    }

    private void Raise(string eventType, object sender, string detail)
    {
        var runtimeId = sender is AutomationElement element
            ? SafeRuntimeId(element)
            : string.Empty;
        Interlocked.Increment(ref _eventCount);
        EventReceived?.Invoke(this, new UiAutomationEventArgs(_clock.UtcNow, eventType, runtimeId, detail));
    }

    private void RemoveHandlersCore()
    {
        if (_root is null)
        {
            return;
        }

        var root = _root;
        try
        {
            Automation.RemoveStructureChangedEventHandler(root, _structureHandler);
        }
        catch (ElementNotAvailableException)
        {
            // Viber 窗口已关闭时无需再移除其事件源。
        }

        try
        {
            Automation.RemoveAutomationPropertyChangedEventHandler(root, _propertyHandler);
        }
        catch (ElementNotAvailableException)
        {
            // 同上；继续清理本地引用。
        }

        _root = null;
        _windowHandle = 0;
    }

    private static string SafeRuntimeId(AutomationElement element)
    {
        try
        {
            return string.Join('.', element.GetRuntimeId());
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
    }

}
