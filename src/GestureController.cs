using System.Diagnostics;
using System.Globalization;

namespace CycleSwipes;

/// <summary>
/// Ties touchpad frames, the recognizer and the desktop state together: on an edge desktop it wraps to the
/// other end of the list; on the other desktops it only compares its own decision with what Windows did.
/// </summary>
internal sealed class GestureController(Config config, Logger log, DesktopSwitcher switcher, Action<string> onSwitchFailed)
{
    private const int FingerCount = 4;
    private const int WatchMs = 1500;
    private const int VerifyMs = 1000;
    private static readonly long VelocityWindowTicks = Stopwatch.Frequency / 20; // 50 ms

    private sealed class DeviceState
    {
        public readonly GestureRecognizer Recognizer = new(FingerCount);
        public bool Tracking;
        public int Id;
        public DesktopState Start;
        public long StartTicks;
        public int StartX;
        public int PeakDx;
        // For the velocity at the end of the gesture: a point 50..100 ms before the last frame.
        public long LastTicks, RefTicks, OlderTicks;
        public int LastX, RefX, OlderX;
    }

    private readonly Dictionary<int, DeviceState> _devices = new();
    private bool _paused;
    private int _gestureCounter;
    private volatile int _latestGesture;

    /// <summary>While paused, frames are ignored; unfinished gestures are dropped.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            _devices.Clear();
        }
    }

    /// <summary>Drops unfinished gestures (after sleep or a suspected input loss).</summary>
    public void Reset() => _devices.Clear();

    public void OnFrame(TouchpadDevice device, List<Contact> frame)
    {
        if (_paused) return;

        int fingers = 0;
        long sumX = 0, sumY = 0;
        foreach (var c in frame)
        {
            if (!c.IsFinger) continue;
            fingers++;
            sumX += c.X;
            sumY += c.Y;
        }
        int cx = fingers > 0 ? (int)(sumX / fingers) : 0;
        int cy = fingers > 0 ? (int)(sumY / fingers) : 0;

        if (!_devices.TryGetValue(device.Index, out var s)) _devices[device.Index] = s = new DeviceState();

        long now = Stopwatch.GetTimestamp();
        switch (s.Recognizer.Feed(fingers, cx, cy))
        {
            case GestureEvent.Started:
                // The desktop index is captured here, before Windows gets a chance to switch the desktop.
                s.Id = _latestGesture = ++_gestureCounter;
                s.Start = ReadState(s.Id, "start");
                s.Tracking = true;
                s.StartTicks = s.LastTicks = s.RefTicks = s.OlderTicks = now;
                s.StartX = s.LastX = s.RefX = s.OlderX = cx;
                s.PeakDx = 0;
                if (log.DebugEnabled) log.Debug($"GESTURE #{s.Id} start: desktop {s.Start}");
                break;

            case GestureEvent.None when s.Tracking && fingers == FingerCount:
                int dx = cx - s.StartX;
                if (Math.Abs(dx) > Math.Abs(s.PeakDx)) s.PeakDx = dx;
                s.LastTicks = now;
                s.LastX = cx;
                if (now - s.RefTicks >= VelocityWindowTicks)
                {
                    (s.OlderTicks, s.OlderX) = (s.RefTicks, s.RefX);
                    (s.RefTicks, s.RefX) = (now, cx);
                }
                break;

            case GestureEvent.Completed:
                s.Tracking = false;
                Complete(device, s, now);
                break;

            case GestureEvent.Cancelled:
                s.Tracking = false;
                log.Debug($"GESTURE #{s.Id} cancelled: more than {FingerCount} fingers");
                break;
        }
    }

    private void Complete(TouchpadDevice device, DeviceState s, long liftTicks)
    {
        int dx = s.Recognizer.Dx, dy = s.Recognizer.Dy;
        double width = device.XMax - device.XMin;
        double height = device.YMax - device.YMin;
        double fx = dx / width;

        double seconds = (s.LastTicks - s.OlderTicks) / (double)Stopwatch.Frequency;
        double velocity = seconds > 0 ? (s.LastX - s.OlderX) / width / seconds : 0;

        // Windows also accepts a short fast swipe, so besides the distance threshold there is a velocity threshold.
        bool longEnough = Math.Abs(fx) > config.SwipeThresholdX;
        bool flick = Math.Abs(fx) > config.FlickMinDistanceX && Math.Abs(velocity) > config.FlickMinVelocity
                     && Math.Sign(velocity) == Math.Sign(dx);
        bool horizontal = (longEnough || flick) && Math.Abs(dx) > config.HorizontalRatio * Math.Abs(dy);
        bool next = (dx < 0) != config.InvertDirection;
        DesktopState start = s.Start;

        int target = -1;
        string decision;
        if (!start.IsValid) decision = $"skipped ({start.Error})";
        else if (!horizontal) decision = "nothing (not a horizontal swipe)";
        else if (start.Count < 2) decision = "nothing (single desktop)";
        else if (next && start.Index == start.Count - 1) { target = 0; decision = "WRAP to the first desktop (1)"; }
        else if (!next && start.Index == 0) { target = start.Count - 1; decision = $"WRAP to the last desktop ({start.Count})"; }
        else decision = "nothing (not an edge desktop, Windows switches)";

        // Race guard: if the desktop is no longer the one from the gesture start, Windows has switched it.
        DesktopState atLift = ReadState(s.Id, "lift");
        if (target >= 0 && (!atLift.IsValid || atLift.Index != start.Index || atLift.Count != start.Count))
        {
            target = -1;
            decision = "nothing (desktop changed during the gesture)";
        }

        bool switched = false;
        long callStartTicks = 0, callEndTicks = 0;
        if (target >= 0)
        {
            if (!switcher.Enabled) decision += $" NOT DONE: {switcher.DisabledReason}";
            else
            {
                callStartTicks = Stopwatch.GetTimestamp();
                switched = switcher.GoTo(target);
                callEndTicks = Stopwatch.GetTimestamp();
                if (!switched) onSwitchFailed(switcher.DisabledReason ?? "unknown error");
            }
        }

        // Edge decisions are rare; log them always, otherwise a failure without debug mode can't be analysed.
        if (!log.DebugEnabled && (target >= 0 || callStartTicks != 0 || decision.Contains("changed during")))
            log.Write($"GESTURE #{s.Id} start {start}, at lift {atLift}: {decision}{(callStartTicks != 0 && !switched ? " CALL FAILED" : "")}");

        if (switched)
        {
            int verifyId = s.Id;
            Task.Run(() => VerifySwitch(verifyId, target, liftTicks));
        }
        if (!log.DebugEnabled) return;

        long durationMs = (liftTicks - s.StartTicks) * 1000 / Stopwatch.Frequency;

        log.Debug(string.Create(CultureInfo.InvariantCulture,
            $"GESTURE #{s.Id} lift: dX={fx:+0.0%;-0.0%;0.0%} dY={dy / height:+0.0%;-0.0%;0.0%} peak dX={s.PeakDx / width:+0.0%;-0.0%;0.0%} " +
            $"velocity={velocity:+0.00;-0.00} widths/s duration={durationMs} ms | " +
            $"swipe {(dx < 0 ? "left" : "right")} => {(next ? "next" : "previous")} | " +
            $"start {start}, at lift {atLift} | decision: {decision}"));
        if (callStartTicks != 0)
            log.Debug(string.Create(CultureInfo.InvariantCulture,
                $"GESTURE #{s.Id} GoToDesktopNumber({target}): {(switched ? "ok" : "FAILED")}, " +
                $"lift to call {Ms(callStartTicks - liftTicks):0.00} ms, call took {Ms(callEndTicks - callStartTicks):0.00} ms"));

        if (start.IsValid && !switched)
        {
            int id = s.Id;
            Task.Run(() => WatchSystem(id, start, liftTicks, horizontal, next, target >= 0));
        }
    }

    /// <summary>
    /// Desktop state from the registry, cross-checked with the DLL. The registry was suspected of lagging behind the
    /// real desktop, so on a mismatch the DLL's number wins, and the mismatch is always logged.
    /// </summary>
    private DesktopState ReadState(int id, string moment)
    {
        DesktopState registry = VirtualDesktops.Read();
        int live = switcher.CurrentDesktop();
        if (live < 0 || (registry.IsValid && live == registry.Index)) return registry;

        if (!registry.IsValid || live >= registry.Count)
        {
            log.Write($"GESTURE #{id} {moment}: registry {registry}, DLL desktop {live + 1}; state unclear, gesture skipped");
            return registry.IsValid ? registry with { Error = "registry and DLL disagree" } : registry;
        }
        log.Write($"GESTURE #{id} {moment}: MISMATCH registry {registry}, DLL desktop {live + 1}/{registry.Count}; using the DLL");
        return registry with { Index = live };
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    /// <summary>After a wrap, checks the result against the registry; if the desktop didn't change, disables wrapping.</summary>
    private void VerifySwitch(int id, int target, long liftTicks)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < VerifyMs)
        {
            DesktopState now = VirtualDesktops.Read();
            if (now.IsValid && now.Index == target)
            {
                log.Debug(string.Create(CultureInfo.InvariantCulture,
                    $"GESTURE #{id} wrap confirmed by the registry: desktop {now}, {Ms(Stopwatch.GetTimestamp() - liftTicks):0} ms after lift"));
                return;
            }
            Thread.Sleep(2);
        }

        switcher.Disable($"desktop did not change within {VerifyMs} ms after GoToDesktopNumber({target}) (now {VirtualDesktops.Read()})");
        onSwitchFailed(switcher.DisabledReason!);
    }

    /// <summary>Watches whether Windows switches the desktop after the lift and compares that with the recognizer's decision.</summary>
    private void WatchSystem(int id, DesktopState start, long liftTicks, bool horizontal, bool next, bool wrap)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < WatchMs)
        {
            // The next gesture has started: a desktop change can no longer be attributed to this one.
            if (_latestGesture != id) return;

            DesktopState now = VirtualDesktops.Read();
            if (now.IsValid && now.Index != start.Index)
            {
                long ms = (Stopwatch.GetTimestamp() - liftTicks) * 1000 / Stopwatch.Frequency;
                bool systemNext = now.Index > start.Index;
                string verdict = !horizontal
                    ? "MISMATCH: Windows switched, the recognizer would not have (threshold too high)"
                    : systemNext != next
                        ? "DIRECTION MISMATCH (flip invertDirection)"
                        : "MATCH";
                log.Debug($"GESTURE #{id} Windows: switched {start.Index + 1} -> {now.Index + 1} " +
                          $"{ms} ms after lift | check: {verdict}");
                return;
            }
            Thread.Sleep(2);
        }

        string result = !horizontal ? "MATCH (no swipe)"
            : wrap ? "edge of the list, Windows is not expected to switch"
            : "MISMATCH: the recognizer would have fired, Windows did not switch (threshold too low)";
        log.Debug($"GESTURE #{id} Windows: no switch within {WatchMs} ms | check: {result}");
    }
}
