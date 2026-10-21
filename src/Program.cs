using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace CycleSwipes;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Mutex mutex;
        try
        {
            mutex = new Mutex(true, "CycleSwipes.SingleInstance", out bool isFirst);
            if (!isFirst)
            {
                // On a restart from the menu the old instance is still shutting down: wait until it releases the mutex.
                if (!args.Contains("--restart", StringComparer.OrdinalIgnoreCase)) return;
                try
                {
                    if (!mutex.WaitOne(TimeSpan.FromSeconds(10))) return;
                }
                catch (AbandonedMutexException)
                {
                    // The old process exited without releasing the mutex; ownership passed to us anyway.
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // The mutex was created by an elevated instance: it is already running.
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext(debug: args.Contains("--debug", StringComparer.OrdinalIgnoreCase)));

        // VirtualDesktopAccessor.dll hangs the process on a normal shutdown (after "Exit" it stayed alive with a
        // single thread). The log is already flushed at this point, so terminate forcibly.
        mutex.ReleaseMutex();
        Process.GetCurrentProcess().Kill();
    }
}

/// <summary>The tray icon and the glue between all parts: touchpad input, recognizer, desktop switching.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly Logger _log;
    private readonly Config _config;
    private readonly DesktopSwitcher _switcher;
    private readonly SynchronizationContext _ui;
    private readonly GestureController _controller;
    private readonly InputWindow _input;
    private readonly DesktopWatcher _watcher;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly StringBuilder _line = new(256);
    private readonly bool _developer;

    public TrayContext(bool debug)
    {
        _developer = debug;
        _log = new Logger(Path.Combine(AppContext.BaseDirectory, "cycle-swipes.log"), enabled: debug) { DebugEnabled = debug };
        _log.Write($"=== Start, Windows {Environment.OSVersion.Version}, debug={(debug ? "on" : "off")}, " +
                   $"rights={(Environment.IsPrivilegedProcess ? "administrator" : "standard: swipes over elevated windows are not visible")} ===");
        _config = Config.Load(Path.Combine(AppContext.BaseDirectory, "config.json"), _log);
        _log.Write($"Desktops at startup: {VirtualDesktops.Read()}");
        _switcher = new DesktopSwitcher(_log);
        _ui = new WindowsFormsSynchronizationContext();
        _controller = new GestureController(_config, _log, _switcher, NotifySwitchFailed);
        _input = new InputWindow(_log, OnFrame);
        _input.Resumed += () =>
        {
            _controller.Reset();
            _log.Write("Resumed from sleep: unfinished gestures dropped");
        };
        _watcher = new DesktopWatcher(_log, (from, to, ticks) => _ui.Post(_ => OnDesktopChanged(from, to, ticks), null));

        _pauseItem = new ToolStripMenuItem("Pause", null, (_, _) => TogglePause());
        var debugItem = new ToolStripMenuItem("Debug mode (log)") { Checked = debug, CheckOnClick = true };
        debugItem.CheckedChanged += (_, _) =>
        {
            _log.DebugEnabled = debugItem.Checked;
            _log.Write($"Debug mode: {(debugItem.Checked ? "on" : "off")}");
        };
        var autostartItem = new ToolStripMenuItem("Start with Windows") { Checked = Autostart.IsEnabled() };
        autostartItem.Click += (_, _) => ToggleAutostart(autostartItem);

        bool elevated = Environment.IsPrivilegedProcess;
        var restartItem = new ToolStripMenuItem(elevated ? "Restart" : "Restart as administrator",
            elevated ? null : SystemIcons.Shield.ToBitmap(), (_, _) => Restart(elevate: !elevated));

        _menu = new ContextMenuStrip();
        var menu = _menu;
        menu.Items.Add(restartItem);
        if (debug)
        {
            // Developer items: only when started with --debug.
            menu.Items.Add(_pauseItem);
            menu.Items.Add(debugItem);
            menu.Items.Add("Open log", null, (_, _) =>
                Process.Start(new ProcessStartInfo(_log.FilePath) { UseShellExecute = true }));
        }
        menu.Items.Add(autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        ApplyTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        UpdateTrayText();
        if (!_switcher.Enabled) NotifySwitchFailed(_switcher.DisabledReason!);
    }

    /// <summary>
    /// Starts a new instance (with a UAC prompt if <paramref name="elevate"/>) and exits the current one.
    /// When elevated, the app also sees swipes over windows that run as administrator.
    /// </summary>
    private void Restart(bool elevate)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!, "--restart" + (_developer ? " --debug" : ""))
        {
            UseShellExecute = true,
            Verb = elevate ? "runas" : "",
        };
        try
        {
            Process.Start(start);
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            _log.Write("Restart cancelled in the UAC prompt");
            return;
        }
        catch (Exception e)
        {
            _log.Write($"ERROR: restart failed: {e.Message}");
            _tray.ShowBalloonTip(10000, "CycleSwipes", $"Restart failed: {e.Message}", ToolTipIcon.Error);
            return;
        }

        _log.Write(elevate ? "Restarting as administrator" : "Restarting");
        ExitThread();
    }

    private void TogglePause()
    {
        _controller.Paused = !_controller.Paused;
        _pauseItem.Text = _controller.Paused ? "Resume" : "Pause";
        _log.Write(_controller.Paused ? "Paused" : "Resumed");
        ApplyTheme();
        UpdateTrayText();
    }

    private void ToggleAutostart(ToolStripMenuItem item)
    {
        try
        {
            Autostart.Set(!item.Checked);
            item.Checked = Autostart.IsEnabled();
            _log.Write($"Start with Windows: {(item.Checked ? "on" : "off")}");
        }
        catch (Exception e)
        {
            _log.Write($"ERROR: autostart not changed: {e.Message}");
            _tray.ShowBalloonTip(10000, "CycleSwipes", $"Could not change autostart: {e.Message}", ToolTipIcon.Error);
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General) ApplyTheme();
    }

    /// <summary>Redraws the icon and the menu for the current Windows theme and pause state.</summary>
    private void ApplyTheme()
    {
        Icon? old = _tray.Icon;
        _tray.Icon = TrayIcon.Create(_controller.Paused);
        old?.Dispose();
        _menu.Renderer = new TrayMenuRenderer(SystemTheme.AppsAreDark);
    }

    private void UpdateTrayText() =>
        _tray.Text = !_switcher.Enabled ? "CycleSwipes: wrapping disabled, see log"
            : _controller.Paused ? "CycleSwipes (paused)"
            : "CycleSwipes";

    /// <summary>May be called from a background thread.</summary>
    private void NotifySwitchFailed(string reason) => _ui.Post(_ =>
    {
        UpdateTrayText();
        _tray.ShowBalloonTip(10000, "CycleSwipes: wrapping disabled", reason, ToolTipIcon.Error);
    }, null);

    /// <summary>
    /// The desktop changed. If no touchpad report arrived in the last second, the swipe most likely bypassed the
    /// app: re-register input. False positive: switching with the keyboard or from Task View.
    /// </summary>
    private void OnDesktopChanged(DesktopState from, DesktopState to, long ticks)
    {
        if (_controller.Paused) return;
        long last = _input.LastInputTicks;
        double gapMs = last == 0 ? double.PositiveInfinity : (ticks - last) * 1000.0 / Stopwatch.Frequency;
        if (gapMs < 1000) return;

        string gap = double.IsInfinity(gapMs) ? "none since startup" : $"none for {gapMs / 1000:0.0} s";
        _log.Write($"INPUT LOST? Desktop changed {from.Index + 1} -> {to.Index + 1}, touchpad reports: {gap} " +
                   $"(or the desktop was switched with the keyboard/mouse). Foreground window: {Foreground.Describe()}. " +
                   "Input re-registered, gestures dropped");
        _input.Register("suspected input loss");
        _controller.Reset();
    }

    private void OnFrame(TouchpadDevice device, List<Contact> frame)
    {
        if (_config.LogFrames && _log.DebugEnabled) LogFrame(device, frame);
        _controller.OnFrame(device, frame);
    }

    private void LogFrame(TouchpadDevice device, List<Contact> frame)
    {
        _line.Clear().Append("F dev=").Append(device.Index).Append(" [");
        foreach (var c in frame)
        {
            // Marks: ^ finger lifted (Tip=0), ? rejected by Confidence (palm).
            _line.Append(c.Id).Append(':').Append(c.X).Append(',').Append(c.Y);
            if (!c.Tip) _line.Append('^');
            if (!c.Confident) _line.Append('?');
            _line.Append(' ');
        }
        _line.Append(']');
        _log.Write(_line.ToString());
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _watcher.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _input.Dispose();
        _log.Write("=== Exit ===");
        _log.Dispose();
        base.ExitThreadCore();
    }
}
