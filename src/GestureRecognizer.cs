namespace CycleSwipes;

internal enum GestureEvent
{
    None,
    /// <summary>First frame with the required number of fingers.</summary>
    Started,
    /// <summary>Fingers started lifting; the displacement is in <see cref="GestureRecognizer.Dx"/> / Dy.</summary>
    Completed,
    /// <summary>More fingers than required.</summary>
    Cancelled,
}

/// <summary>
/// Gesture state machine for one touchpad. Pure logic: the input is the finger count and the frame centroid.
/// The displacement is taken from the last frame with the required finger count, because fingers do not lift simultaneously.
/// </summary>
internal sealed class GestureRecognizer(int fingerCount = 4)
{
    private enum State { Idle, Tracking, WaitRelease }

    private State _state;
    private int _startX, _startY;

    public int Dx { get; private set; }
    public int Dy { get; private set; }

    public GestureEvent Feed(int fingers, int centroidX, int centroidY)
    {
        switch (_state)
        {
            case State.Idle:
                if (fingers == fingerCount)
                {
                    _state = State.Tracking;
                    _startX = centroidX;
                    _startY = centroidY;
                    Dx = Dy = 0;
                    return GestureEvent.Started;
                }
                if (fingers > fingerCount) _state = State.WaitRelease;
                return GestureEvent.None;

            case State.Tracking:
                if (fingers == fingerCount)
                {
                    Dx = centroidX - _startX;
                    Dy = centroidY - _startY;
                    return GestureEvent.None;
                }
                // A new gesture only after all fingers are lifted, otherwise 4 -> 3 -> 4 would produce a false second gesture.
                _state = fingers == 0 ? State.Idle : State.WaitRelease;
                return fingers > fingerCount ? GestureEvent.Cancelled : GestureEvent.Completed;

            default:
                if (fingers == 0) _state = State.Idle;
                return GestureEvent.None;
        }
    }
}
