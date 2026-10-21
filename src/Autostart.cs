using Microsoft.Win32;

namespace CycleSwipes;

/// <summary>Autostart via HKCU\...\Run: does not require administrator rights.</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CycleSwipes";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    /// <summary>Enabled and pointing at this exe (after moving the exe, autostart has to be enabled again).</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
