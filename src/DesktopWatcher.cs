using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace CycleSwipes;

/// <summary>
/// Reports current desktop changes using registry notifications (RegNotifyChangeKeyValue): no polling, no CPU load.
/// The callback runs on a background thread with the time (Stopwatch) the change was detected.
/// </summary>
internal sealed class DesktopWatcher : IDisposable
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";
    private const int REG_NOTIFY_CHANGE_LAST_SET = 0x4;

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, bool watchSubtree, int filter,
        SafeWaitHandle evt, bool asynchronous);

    private readonly Logger _log;
    private readonly Action<DesktopState, DesktopState, long> _onChanged;
    private readonly ManualResetEvent _stop = new(false);
    private readonly Thread _thread;

    public DesktopWatcher(Logger log, Action<DesktopState, DesktopState, long> onChanged)
    {
        _log = log;
        _onChanged = onChanged;
        _thread = new Thread(Loop) { IsBackground = true, Name = "DesktopWatcher" };
        _thread.Start();
    }

    private void Loop()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key is null)
            {
                _log.Write("Desktop watcher not started: no VirtualDesktops key");
                return;
            }

            using var changed = new AutoResetEvent(false);
            DesktopState previous = VirtualDesktops.Read();
            while (true)
            {
                // The notification is one-shot and must be requested again after each signal.
                int rc = RegNotifyChangeKeyValue(key.Handle, false, REG_NOTIFY_CHANGE_LAST_SET, changed.SafeWaitHandle, true);
                if (rc != 0)
                {
                    _log.Write($"Desktop watcher stopped: RegNotifyChangeKeyValue returned {rc}");
                    return;
                }
                if (WaitHandle.WaitAny([changed, _stop]) == 1) return;

                long ticks = Stopwatch.GetTimestamp();
                DesktopState current = VirtualDesktops.Read();
                if (current.IsValid && previous.IsValid && current.Index != previous.Index)
                    _onChanged(previous, current, ticks);
                previous = current;
            }
        }
        catch (Exception e)
        {
            _log.Write($"Desktop watcher stopped: {e.Message}");
        }
    }

    public void Dispose()
    {
        _stop.Set();
        _thread.Join(1000);
    }
}
