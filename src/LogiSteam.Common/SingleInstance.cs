namespace LogiSteam.Common;

/// <summary>
/// LogiSteam lives in the tray, so launching it again (the desktop shortcut, the Start menu)
/// should bring the running copy's window forward rather than start a second copy fighting the
/// first for the same network ports.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\LogiSteam.SingleInstance";
    private const string ShowEventName = @"Local\LogiSteam.ShowWindow";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly CancellationTokenSource _cts = new();

    private SingleInstance(Mutex mutex, EventWaitHandle showEvent)
    {
        _mutex = mutex;
        _showEvent = showEvent;
    }

    /// <summary>
    /// Returns the handle for the first instance. For any later instance, asks the first one to
    /// show its window and returns null -- the caller should then exit.
    /// </summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (createdNew)
        {
            return new SingleInstance(mutex, showEvent);
        }

        showEvent.Set();
        showEvent.Dispose();
        mutex.Dispose();
        return null;
    }

    /// <summary>Calls <paramref name="onShowRequested"/> (on a background thread) each time another launch asks for the window.</summary>
    public void ListenForShowRequests(Action onShowRequested)
    {
        var token = _cts.Token;
        var thread = new Thread(() =>
        {
            while (WaitHandle.WaitAny([_showEvent, token.WaitHandle]) == 0)
            {
                onShowRequested();
            }
        })
        {
            IsBackground = true,
            Name = "LogiSteam single-instance listener",
        };
        thread.Start();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _showEvent.Dispose();
        _mutex.Dispose();
        _cts.Dispose();
    }
}
