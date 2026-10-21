using Microsoft.Win32;

namespace CycleSwipes;

/// <summary>Current desktop index and desktop count; <see cref="Error"/> is not null if reading failed.</summary>
internal readonly record struct DesktopState(int Index, int Count, string? Error)
{
    public bool IsValid => Error is null;
    public override string ToString() => IsValid ? $"{Index + 1}/{Count}" : $"error: {Error}";
}

/// <summary>Virtual desktop state from the registry (read only, no COM).</summary>
internal static class VirtualDesktops
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";
    private const int GuidSize = 16;

    public static DesktopState Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key is null) return Fail("no VirtualDesktops key");
            if (key.GetValue("VirtualDesktopIDs") is not byte[] ids) return Fail("no VirtualDesktopIDs value");
            if (key.GetValue("CurrentVirtualDesktop") is not byte[] current) return Fail("no CurrentVirtualDesktop value");
            if (ids.Length == 0 || ids.Length % GuidSize != 0) return Fail($"VirtualDesktopIDs length = {ids.Length}");
            if (current.Length != GuidSize) return Fail($"CurrentVirtualDesktop length = {current.Length}");

            int count = ids.Length / GuidSize;
            for (int i = 0; i < count; i++)
            {
                if (ids.AsSpan(i * GuidSize, GuidSize).SequenceEqual(current))
                    return new DesktopState(i, count, null);
            }
            return Fail("current desktop not found in the list");
        }
        catch (Exception e)
        {
            return Fail(e.Message);
        }
    }

    private static DesktopState Fail(string error) => new(-1, 0, error);
}
