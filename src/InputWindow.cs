using System.Diagnostics;
using System.Runtime.InteropServices;
using static CycleSwipes.Native;

namespace CycleSwipes;

/// <summary>Hidden window that receives Raw Input from touchpads and hands out assembled frames.</summary>
internal sealed class InputWindow : NativeWindow, IDisposable
{
    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();

    private readonly Logger _log;
    private readonly Action<TouchpadDevice, List<Contact>> _onFrame;
    // A null value means the device was already checked and rejected; don't parse it again.
    private readonly Dictionary<IntPtr, TouchpadDevice?> _devices = new();
    private IntPtr _buffer;
    private uint _bufferSize;
    private int _nextIndex = 1;
    private long _lastInputTicks;

    /// <summary>Time (Stopwatch) of the last touchpad report; 0 if there were none yet.</summary>
    public long LastInputTicks => Volatile.Read(ref _lastInputTicks);

    /// <summary>The system resumed from sleep; input has already been re-registered.</summary>
    public event Action? Resumed;

    public InputWindow(Logger log, Action<TouchpadDevice, List<Contact>> onFrame)
    {
        _log = log;
        _onFrame = onFrame;
        CreateHandle(new CreateParams());
        Register("startup");
    }

    /// <summary>Registers (or re-registers) for touchpad Raw Input. Calling it again is harmless.</summary>
    public void Register(string reason)
    {
        var devices = new[]
        {
            new RAWINPUTDEVICE
            {
                usUsagePage = PageDigitizer,
                usUsage = UsageTouchPad,
                dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY,
                hwndTarget = Handle,
            },
        };
        if (!RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            _log.Write($"ERROR: RegisterRawInputDevices failed, win32={Marshal.GetLastWin32Error()}");
        else
            _log.Write($"Raw Input registered (0x0D:0x05, INPUTSINK | DEVNOTIFY), reason: {reason}");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_INPUT)
            OnInput(m.LParam);
        else if (m.Msg == WM_POWERBROADCAST && (m.WParam == PBT_APMRESUMEAUTOMATIC || m.WParam == PBT_APMRESUMESUSPEND))
        {
            Register(m.WParam == PBT_APMRESUMEAUTOMATIC ? "resume from sleep" : "resume from sleep (user)");
            Resumed?.Invoke();
        }
        else if (m.Msg == WM_INPUT_DEVICE_CHANGE && m.WParam == GIDC_REMOVAL && _devices.Remove(m.LParam, out var removed))
        {
            _log.Write($"Device removed: dev={removed?.Index.ToString() ?? "-"}");
            removed?.Dispose();
        }

        base.WndProc(ref m);
    }

    private void OnInput(IntPtr hRawInput)
    {
        uint size = 0;
        GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, HeaderSize);
        if (size == 0) return;
        if (size > _bufferSize)
        {
            _buffer = _buffer == IntPtr.Zero ? Marshal.AllocHGlobal((int)size) : Marshal.ReAllocHGlobal(_buffer, (IntPtr)size);
            _bufferSize = size;
        }
        if (GetRawInputData(hRawInput, RID_INPUT, _buffer, ref size, HeaderSize) == uint.MaxValue) return;

        var header = Marshal.PtrToStructure<RAWINPUTHEADER>(_buffer);
        if (header.dwType != RIM_TYPEHID) return;
        Volatile.Write(ref _lastInputTicks, Stopwatch.GetTimestamp());

        if (!_devices.TryGetValue(header.hDevice, out var device))
        {
            device = TouchpadDevice.Create(header.hDevice, _nextIndex, out string? error);
            _devices[header.hDevice] = device;
            if (device is null)
                _log.Write($"Device skipped: {error}");
            else
            {
                _nextIndex++;
                _log.Write($"Device dev={device.Index}: X={device.XMin}..{device.XMax}, Y={device.YMin}..{device.YMax}, " +
                           $"slots per report={device.SlotCount}, {device.Name}");
            }
        }
        if (device is null) return;

        // RAWHID: dwSizeHid, dwCount, then dwCount reports of dwSizeHid bytes each.
        uint reportSize = (uint)Marshal.ReadInt32(_buffer, (int)HeaderSize);
        uint reportCount = (uint)Marshal.ReadInt32(_buffer, (int)HeaderSize + 4);
        IntPtr data = _buffer + (int)HeaderSize + 8;
        for (uint i = 0; i < reportCount; i++)
        {
            var frame = device.ProcessReport(data + (int)(i * reportSize), reportSize);
            if (frame is not null) _onFrame(device, frame);
        }
    }

    public void Dispose()
    {
        foreach (var device in _devices.Values) device?.Dispose();
        _devices.Clear();
        DestroyHandle();
        if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
        _buffer = IntPtr.Zero;
    }
}
