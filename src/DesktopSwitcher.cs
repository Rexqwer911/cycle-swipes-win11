using System.Runtime.InteropServices;

namespace CycleSwipes;

/// <summary>
/// Desktop switching via VirtualDesktopAccessor.dll (undocumented shell COM, tied to the Windows build).
/// On any error or mismatch with the registry, wrapping is disabled; there is no fallback switching method.
/// </summary>
internal sealed class DesktopSwitcher
{
    private const string Dll = "VirtualDesktopAccessor.dll";

    [DllImport(Dll)] private static extern int GetDesktopCount();
    [DllImport(Dll)] private static extern int GetCurrentDesktopNumber();
    [DllImport(Dll)] private static extern int GoToDesktopNumber(int number);

    private readonly Logger _log;
    private volatile string? _disabledReason;

    public bool Enabled => _disabledReason is null;
    public string? DisabledReason => _disabledReason;

    public DesktopSwitcher(Logger log)
    {
        _log = log;
        Probe();
    }

    /// <summary>Check using read-only functions only: their answers must match the registry.</summary>
    private void Probe()
    {
        string path = Path.Combine(AppContext.BaseDirectory, Dll);
        if (!File.Exists(path))
        {
            Disable($"{Dll} not found next to the exe");
            return;
        }

        try
        {
            NativeLibrary.Load(path);
            DesktopState registry = VirtualDesktops.Read();
            int count = GetDesktopCount();
            int current = GetCurrentDesktopNumber();
            _log.Write($"DLL: desktops={count}, current={current + 1}; registry: {registry}");

            if (!registry.IsValid) Disable($"registry unreadable ({registry.Error}), nothing to check the DLL against");
            else if (count != registry.Count || current != registry.Index)
                Disable("DLL answers do not match the registry; the DLL is incompatible with this Windows build");
            else _log.Write("DLL checked, wrapping enabled");
        }
        catch (Exception e)
        {
            Disable($"DLL does not work: {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>Current desktop number from the live shell state (zero-based), -1 on error. UI thread only:
    /// this DLL version is not safe for concurrent calls from different threads.</summary>
    public int CurrentDesktop()
    {
        if (!Enabled) return -1;
        try
        {
            return GetCurrentDesktopNumber();
        }
        catch (Exception)
        {
            return -1;
        }
    }

    /// <summary>Instant switch to the desktop with index <paramref name="index"/> (zero-based).</summary>
    public bool GoTo(int index)
    {
        if (!Enabled) return false;
        try
        {
            int result = GoToDesktopNumber(index);
            if (result >= 0) return true;
            Disable($"GoToDesktopNumber({index}) returned {result}");
        }
        catch (Exception e)
        {
            Disable($"GoToDesktopNumber({index}): {e.GetType().Name}: {e.Message}");
        }
        return false;
    }

    public void Disable(string reason)
    {
        if (_disabledReason is not null) return;
        _disabledReason = reason;
        _log.Write($"ERROR: wrapping disabled: {reason}");
    }
}
