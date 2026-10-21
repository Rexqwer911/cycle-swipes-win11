using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CycleSwipes;

/// <summary>Describes the foreground window for the log: process name and whether it runs elevated.</summary>
internal static class Foreground
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20;

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);

    public static string Describe()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return "no foreground window";
        GetWindowThreadProcessId(hwnd, out uint pid);

        string name;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            name = process.ProcessName;
        }
        catch (Exception)
        {
            name = $"pid {pid}";
        }
        return $"{name} ({Elevation(pid)})";
    }

    private static string Elevation(uint pid)
    {
        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return "elevation unknown";
        try
        {
            // A non-elevated process is not allowed to open an elevated process token: that is a sign in itself.
            if (!OpenProcessToken(process, TOKEN_QUERY, out IntPtr token)) return "elevated (token not accessible)";
            try
            {
                if (!GetTokenInformation(token, TokenElevation, out int elevated, sizeof(int), out _)) return "elevation unknown";
                return elevated != 0 ? "ELEVATED" : "not elevated";
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }
}
