using System.Runtime.InteropServices;
using static CycleSwipes.Native;

namespace CycleSwipes;

internal struct Contact
{
    public uint Id;
    public int X;
    public int Y;
    public bool Tip;
    public bool Confident;

    /// <summary>A finger on the touchpad (touching and not rejected as a palm).</summary>
    public readonly bool IsFinger => Tip && Confident;
}

/// <summary>
/// One touchpad: cached HID report description and assembly of frames from reports.
/// In hybrid mode a frame arrives in several reports: the first carries Contact Count, the rest have it at 0.
/// </summary>
internal sealed class TouchpadDevice : IDisposable
{
    private readonly struct Slot(ushort link, bool hasConfidence)
    {
        public readonly ushort Link = link;
        public readonly bool HasConfidence = hasConfidence;
    }

    private readonly IntPtr _preparsed;
    private readonly Slot[] _slots;
    private readonly ushort _countLink;
    private readonly ushort[] _usages = new ushort[16];
    private readonly List<Contact> _frame = new(10);
    private int _remaining;

    public int Index { get; }
    public string Name { get; }
    public int XMin { get; }
    public int XMax { get; }
    public int YMin { get; }
    public int YMax { get; }
    public int SlotCount => _slots.Length;

    private TouchpadDevice(int index, string name, IntPtr preparsed, Slot[] slots, ushort countLink,
        int xMin, int xMax, int yMin, int yMax)
    {
        Index = index;
        Name = name;
        _preparsed = preparsed;
        _slots = slots;
        _countLink = countLink;
        XMin = xMin;
        XMax = xMax;
        YMin = yMin;
        YMax = yMax;
    }

    /// <summary>Returns null with the reason in <paramref name="error"/> if the device does not look like a Precision Touchpad.</summary>
    public static TouchpadDevice? Create(IntPtr hDevice, int index, out string? error)
    {
        error = null;
        string name = ReadName(hDevice);

        uint size = 0;
        GetRawInputDeviceInfo(hDevice, RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
        if (size == 0)
        {
            error = $"no preparsed data ({name})";
            return null;
        }

        IntPtr preparsed = Marshal.AllocHGlobal((int)size);
        bool ok = false;
        try
        {
            if (GetRawInputDeviceInfo(hDevice, RIDI_PREPARSEDDATA, preparsed, ref size) == uint.MaxValue)
            {
                error = $"GetRawInputDeviceInfo(PREPARSEDDATA) failed, win32={Marshal.GetLastWin32Error()} ({name})";
                return null;
            }

            int status = HidP_GetCaps(preparsed, out HIDP_CAPS caps);
            if (status != HIDP_STATUS_SUCCESS)
            {
                error = $"HidP_GetCaps returned 0x{status:X8} ({name})";
                return null;
            }

            var fingers = new SortedDictionary<ushort, SlotInfo>();
            int countLink = -1;
            int xMin = 0, xMax = 0, yMin = 0, yMax = 0;

            ForEachCap(preparsed, caps.NumberInputValueCaps, values: true, cap =>
            {
                ushort link = (ushort)Marshal.ReadInt16(cap, CapsLinkCollection);
                if (Matches(cap, PageDigitizer, UsageContactCount)) countLink = link;
                else if (Matches(cap, PageDigitizer, UsageContactId)) Get(fingers, link).HasId = true;
                else if (Matches(cap, PageGeneric, UsageX))
                {
                    Get(fingers, link).HasX = true;
                    xMin = Marshal.ReadInt32(cap, ValueCapsLogicalMin);
                    xMax = Marshal.ReadInt32(cap, ValueCapsLogicalMax);
                }
                else if (Matches(cap, PageGeneric, UsageY))
                {
                    Get(fingers, link).HasY = true;
                    yMin = Marshal.ReadInt32(cap, ValueCapsLogicalMin);
                    yMax = Marshal.ReadInt32(cap, ValueCapsLogicalMax);
                }
            });

            ForEachCap(preparsed, caps.NumberInputButtonCaps, values: false, cap =>
            {
                ushort link = (ushort)Marshal.ReadInt16(cap, CapsLinkCollection);
                if (Matches(cap, PageDigitizer, UsageTipSwitch)) Get(fingers, link).HasTip = true;
                if (Matches(cap, PageDigitizer, UsageConfidence)) Get(fingers, link).HasConfidence = true;
            });

            var slots = fingers
                .Where(f => f.Value.HasId && f.Value.HasX && f.Value.HasY && f.Value.HasTip)
                .Select(f => new Slot(f.Key, f.Value.HasConfidence))
                .ToArray();

            if (countLink < 0 || slots.Length == 0 || xMax <= xMin || yMax <= yMin)
            {
                error = $"no contact description found: contactCount={(countLink >= 0)}, slots={slots.Length}, " +
                        $"X={xMin}..{xMax}, Y={yMin}..{yMax} ({name})";
                return null;
            }

            ok = true;
            return new TouchpadDevice(index, name, preparsed, slots, (ushort)countLink, xMin, xMax, yMin, yMax);
        }
        finally
        {
            if (!ok) Marshal.FreeHGlobal(preparsed);
        }
    }

    /// <summary>
    /// Parses one HID report. Returns the assembled frame once all of its contacts have arrived, otherwise null.
    /// The list is reused: it is valid only until the next call.
    /// </summary>
    public List<Contact>? ProcessReport(IntPtr report, uint length)
    {
        if (HidP_GetUsageValue(HidP_Input, PageDigitizer, _countLink, UsageContactCount, out uint count,
                _preparsed, report, length) != HIDP_STATUS_SUCCESS)
            return null; // a report with another Report ID (buttons etc.)

        if (count > 0)
        {
            _frame.Clear();
            _remaining = (int)count;
        }
        else if (_remaining == 0)
        {
            // Not a frame continuation: the touchpad reports that there are no contacts.
            _frame.Clear();
            return _frame;
        }

        int take = Math.Min(_remaining, _slots.Length);
        for (int i = 0; i < take; i++)
        {
            Slot slot = _slots[i];
            if (HidP_GetUsageValue(HidP_Input, PageDigitizer, slot.Link, UsageContactId, out uint id,
                    _preparsed, report, length) != HIDP_STATUS_SUCCESS) continue;
            HidP_GetUsageValue(HidP_Input, PageGeneric, slot.Link, UsageX, out uint x, _preparsed, report, length);
            HidP_GetUsageValue(HidP_Input, PageGeneric, slot.Link, UsageY, out uint y, _preparsed, report, length);

            bool tip = false, confident = !slot.HasConfidence;
            uint usageCount = (uint)_usages.Length;
            if (HidP_GetUsages(HidP_Input, PageDigitizer, slot.Link, _usages, ref usageCount,
                    _preparsed, report, length) == HIDP_STATUS_SUCCESS)
            {
                for (int u = 0; u < usageCount; u++)
                {
                    if (_usages[u] == UsageTipSwitch) tip = true;
                    else if (_usages[u] == UsageConfidence) confident = true;
                }
            }

            _frame.Add(new Contact { Id = id, X = (int)x, Y = (int)y, Tip = tip, Confident = confident });
        }

        _remaining -= take;
        return _remaining == 0 ? _frame : null;
    }

    public void Dispose() => Marshal.FreeHGlobal(_preparsed);

    private sealed class SlotInfo
    {
        public bool HasId, HasX, HasY, HasTip, HasConfidence;
    }

    private static SlotInfo Get(SortedDictionary<ushort, SlotInfo> map, ushort link)
    {
        if (!map.TryGetValue(link, out var info)) map[link] = info = new SlotInfo();
        return info;
    }

    private static bool Matches(IntPtr cap, ushort page, ushort usage)
    {
        if ((ushort)Marshal.ReadInt16(cap, CapsUsagePage) != page) return false;
        ushort min = (ushort)Marshal.ReadInt16(cap, CapsUsageMin);
        if (Marshal.ReadByte(cap, CapsIsRange) == 0) return min == usage;
        return min <= usage && usage <= (ushort)Marshal.ReadInt16(cap, CapsUsageMax);
    }

    /// <summary>Calls <paramref name="visit"/> for each HIDP_VALUE_CAPS / HIDP_BUTTON_CAPS of the input reports.</summary>
    private static void ForEachCap(IntPtr preparsed, ushort count, bool values, Action<IntPtr> visit)
    {
        if (count == 0) return;

        IntPtr buffer = Marshal.AllocHGlobal(count * CapsSize);
        try
        {
            ushort length = count;
            int status = values
                ? HidP_GetValueCaps(HidP_Input, buffer, ref length, preparsed)
                : HidP_GetButtonCaps(HidP_Input, buffer, ref length, preparsed);
            if (status != HIDP_STATUS_SUCCESS) return;

            for (int i = 0; i < length; i++) visit(buffer + i * CapsSize);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string ReadName(IntPtr hDevice)
    {
        uint chars = 0;
        GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref chars);
        if (chars == 0) return "?";
        IntPtr buffer = Marshal.AllocHGlobal((int)chars * 2);
        try
        {
            return GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, buffer, ref chars) == uint.MaxValue
                ? "?"
                : Marshal.PtrToStringUni(buffer) ?? "?";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
