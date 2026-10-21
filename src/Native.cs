using System.Runtime.InteropServices;

namespace CycleSwipes;

internal static class Native
{
    public const int WM_INPUT = 0x00FF;
    public const int WM_INPUT_DEVICE_CHANGE = 0x00FE;
    public const int GIDC_REMOVAL = 2;
    public const int WM_POWERBROADCAST = 0x0218;
    public const int PBT_APMRESUMESUSPEND = 0x0007;
    public const int PBT_APMRESUMEAUTOMATIC = 0x0012;

    public const uint RIDEV_INPUTSINK = 0x00000100;
    public const uint RIDEV_DEVNOTIFY = 0x00002000;
    public const uint RID_INPUT = 0x10000003;
    public const uint RIM_TYPEHID = 2;
    public const uint RIDI_PREPARSEDDATA = 0x20000005;
    public const uint RIDI_DEVICENAME = 0x20000007;

    public const int HidP_Input = 0;
    public const int HIDP_STATUS_SUCCESS = 0x00110000;

    public const ushort PageGeneric = 0x01;
    public const ushort PageDigitizer = 0x0D;
    public const ushort UsageTouchPad = 0x05;
    public const ushort UsageX = 0x30;
    public const ushort UsageY = 0x31;
    public const ushort UsageTipSwitch = 0x42;
    public const ushort UsageConfidence = 0x47;
    public const ushort UsageContactId = 0x51;
    public const ushort UsageContactCount = 0x54;

    // Sizes and offsets of HIDP_VALUE_CAPS / HIDP_BUTTON_CAPS (hidpi.h); both structures are 72 bytes.
    public const int CapsSize = 72;
    public const int CapsUsagePage = 0;
    public const int CapsLinkCollection = 6;
    public const int CapsIsRange = 12;
    public const int ValueCapsLogicalMin = 40;
    public const int ValueCapsLogicalMax = 44;
    public const int CapsUsageMin = 56;
    public const int CapsUsageMax = 58;

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct HIDP_CAPS
    {
        [FieldOffset(0)] public ushort Usage;
        [FieldOffset(2)] public ushort UsagePage;
        [FieldOffset(4)] public ushort InputReportByteLength;
        [FieldOffset(44)] public ushort NumberLinkCollectionNodes;
        [FieldOffset(46)] public ushort NumberInputButtonCaps;
        [FieldOffset(48)] public ushort NumberInputValueCaps;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetRawInputDeviceInfoW")]
    public static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint command, IntPtr data, ref uint size);

    [DllImport("hid.dll")]
    public static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS caps);

    [DllImport("hid.dll")]
    public static extern int HidP_GetValueCaps(int reportType, IntPtr valueCaps, ref ushort length, IntPtr preparsedData);

    [DllImport("hid.dll")]
    public static extern int HidP_GetButtonCaps(int reportType, IntPtr buttonCaps, ref ushort length, IntPtr preparsedData);

    [DllImport("hid.dll")]
    public static extern int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection, ushort usage,
        out uint value, IntPtr preparsedData, IntPtr report, uint reportLength);

    [DllImport("hid.dll")]
    public static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection,
        [In, Out] ushort[] usageList, ref uint usageLength, IntPtr preparsedData, IntPtr report, uint reportLength);
}
