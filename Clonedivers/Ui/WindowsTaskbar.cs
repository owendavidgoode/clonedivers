using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Clonedivers.Ui;

/// <summary>Native Windows dark title bar, taskbar progress (ITaskbarList3) and attention flash. Safe no-op on Linux/macOS.</summary>
public static class WindowsTaskbar
{
    static readonly object Gate = new();
    static ITaskbarList3? taskbar;
    static bool taskbarInitFailed;

    public static void ApplyDarkTitleBar(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        var hwnd = TryGetHwnd(window);
        if (hwnd == IntPtr.Zero) return;
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
            int on = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref on, 4);
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
            int caption = 0x00170C08; // #080C17 in COLORREF 0x00BBGGRR
            int text = 0x00FCF6F3;    // #F3F6FC
            int border = 0x004D3022;  // #22304D
            DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
            DwmSetWindowAttribute(hwnd, 36, ref text, 4);
            DwmSetWindowAttribute(hwnd, 34, ref border, 4);
        }
        catch { }
    }

    public static void SetProgress(Window window, OperationProgress? progress)
    {
        if (!OperatingSystem.IsWindows()) return;
        var hwnd = TryGetHwnd(window);
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var tb = GetTaskbar();
            if (tb == null) return;
            if (progress == null)
            {
                tb.SetProgressState(hwnd, TbpFlag.NoProgress);
                return;
            }
            if (progress.IsIndeterminate || progress.Fraction == null)
            {
                tb.SetProgressState(hwnd, TbpFlag.Indeterminate);
            }
            else
            {
                tb.SetProgressState(hwnd, TbpFlag.Normal);
                tb.SetProgressValue(hwnd, (ulong)Math.Clamp(progress.Fraction.Value * 1000d, 0, 1000), 1000);
            }
        }
        catch { }
    }

    public static void FlashAttention(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        var hwnd = TryGetHwnd(window);
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var info = new FlashWInfo
            {
                cbSize = (uint)Marshal.SizeOf<FlashWInfo>(),
                hwnd = hwnd,
                dwFlags = 0x00000003 | 0x0000000C, // FLASHW_ALL | FLASHW_TIMERNOFG
                uCount = 3,
                dwTimeout = 0
            };
            FlashWindowEx(ref info);
        }
        catch { }
    }

    public static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        if (!OperatingSystem.IsWindows()) return;
        var hwnd = TryGetHwnd(window);
        if (hwnd == IntPtr.Zero) return;
        try
        {
            if (IsIconic(hwnd)) ShowWindow(hwnd, 9);
            SetForegroundWindow(hwnd);
        }
        catch { }
    }

    static IntPtr TryGetHwnd(Window window) =>
        window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static ITaskbarList3? GetTaskbar()
    {
        lock (Gate)
        {
            if (taskbar != null || taskbarInitFailed) return taskbar;
            try
            {
                var type = Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11d0-958A-006097C9A090"));
                if (type == null) { taskbarInitFailed = true; return null; }
                var instance = (ITaskbarList3)Activator.CreateInstance(type)!;
                instance.HrInit();
                taskbar = instance;
                return taskbar;
            }
            catch
            {
                taskbarInitFailed = true;
                return null;
            }
        }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool FlashWindowEx(ref FlashWInfo pwfi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    struct FlashWInfo
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    enum TbpFlag
    {
        NoProgress = 0,
        Indeterminate = 1,
        Normal = 2,
        Error = 4,
        Paused = 8
    }

    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ITaskbarList3
    {
        [PreserveSig] void HrInit();
        [PreserveSig] void AddTab(IntPtr hwnd);
        [PreserveSig] void DeleteTab(IntPtr hwnd);
        [PreserveSig] void ActivateTab(IntPtr hwnd);
        [PreserveSig] void SetActiveAlt(IntPtr hwnd);
        [PreserveSig] void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        [PreserveSig] void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        [PreserveSig] void SetProgressState(IntPtr hwnd, TbpFlag flags);
    }
}
