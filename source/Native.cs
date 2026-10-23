using System.Runtime.InteropServices;

namespace SideDock;
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public bool Contains(Point p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom; }
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(Point p, uint flags);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [StructLayout(LayoutKind.Sequential)] internal struct RawDevice { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }
    [StructLayout(LayoutKind.Sequential)] internal struct RawHeader { public uint Type, Size; public IntPtr Device, WParam; }
    [DllImport("user32.dll")] static extern bool RegisterRawInputDevices([In] RawDevice[] devices, uint count, uint size);
    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
    internal static bool RegisterMouse(IntPtr hwnd) => RegisterRawInputDevices(new[] { new RawDevice { UsagePage = 1, Usage = 2, Flags = 0x100, Target = hwnd } }, 1, (uint)Marshal.SizeOf<RawDevice>());
    internal static bool MouseMotion(IntPtr input, out int dx, out int dy)
    {
        dx = dy = 0; uint size = 0, header = (uint)Marshal.SizeOf<RawHeader>();
        if (GetRawInputData(input, 0x10000003, IntPtr.Zero, ref size, header) == uint.MaxValue || size < header + 24 || size > 1024) return false;
        IntPtr data = Marshal.AllocHGlobal((int)size);
        try {
            if (GetRawInputData(input, 0x10000003, data, ref size, header) == uint.MaxValue || Marshal.ReadInt32(data) != 0) return false;
            int offset = (int)header;
            if ((Marshal.ReadInt16(data, offset) & 1) != 0) return false;
            dx = Marshal.ReadInt32(data, offset + 12); dy = Marshal.ReadInt32(data, offset + 16); return true;
        } finally { Marshal.FreeHGlobal(data); }
    }
    internal static double Scale(Point p) { GetDpiForMonitor(MonitorFromPoint(p, 2), 0, out uint x, out _); return x > 0 ? x / 96d : 1; }
    internal static bool AtEdge(Point p, System.Drawing.Rectangle bounds, string edge) => (edge == "left" ? p.X <= bounds.Left + 1 : p.X >= bounds.Right - 2) && p.Y > bounds.Top + bounds.Height * .15 && p.Y < bounds.Bottom - bounds.Height * .15;
}
