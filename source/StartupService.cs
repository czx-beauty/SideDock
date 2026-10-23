using Microsoft.Win32;

namespace SideDock;
internal static class StartupService
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool Enabled { get { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue("SideDock") != null; } }
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue("SideDock", Command(Environment.ProcessPath ?? throw new InvalidOperationException("无法定位程序")));
        else key.DeleteValue("SideDock", false);
    }
    internal static string Command(string executable) => "\"" + executable + "\" --startup";
}
