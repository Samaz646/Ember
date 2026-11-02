using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Ember;

/// <summary>
/// Optional window output using the existing 480x320 Skia frame.
/// Runs independently of the Turing USB panel. Does not change monitor settings.
/// </summary>
public sealed class DisplayOutput : Window
{
    const int W = TuringLcd.Width, H = TuringLcd.Height;
    static DisplayOutput? instance;
    readonly WriteableBitmap bitmap = new(W, H, 96, 96, PixelFormats.Pbgra32, null);
    readonly byte[] buffer = new byte[W * H * 4];
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    int seen = -1;
    string lastBounds = "";
    const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int width, int height, uint flags);

    DisplayOutput()
    {
        Title = "Ember Dashboard";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;
        Background = Brushes.Black;
        Content = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };
        Loaded += (_, _) => { App.Engine.DisplayWanted = true; timer.Start(); Tick(); };
        Closed += (_, _) => { timer.Stop(); App.Engine.DisplayWanted = false; instance = null; };
        timer.Tick += (_, _) => Tick();
    }

    public static void Stop()
    {
        instance?.Close();
    }

    public static void Refresh()
    {
        if (instance != null) instance.lastBounds = "";
    }

    public static void Start()
    {
        if (instance != null) return;
        instance = new DisplayOutput();
        // Create the HWND without showing a dashboard on the primary screen.
        // A missing configured target remains hidden until it reconnects.
        instance.WindowStartupLocation = WindowStartupLocation.Manual;
        instance.Left = -32000;
        instance.Top = -32000;
        instance.Show();
    }

    void Tick()
    {
        // A disconnected virtual monitor must never cause the dashboard to
        // appear on the gaming monitor.
        var target = FindTarget();
        if (target == null)
        {
            if (Visibility != Visibility.Hidden) Hide();
            lastBounds = "";
            return;
        }

        var b = target.Bounds;
        var key = target.DeviceName + ":" + b;
        if (key != lastBounds)
        {
            // Win32 monitor bounds are physical pixels. Position the HWND in
            // physical pixels rather than converting with the wrong monitor DPI.
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, IntPtr.Zero, b.Left, b.Top, b.Width, b.Height,
                    SWP_NOZORDER | SWP_NOACTIVATE);
            lastBounds = key;
        }

        if (!IsVisible) Show();
        if (App.Engine.CopyPreview(buffer, ref seen))
            bitmap.WritePixels(new Int32Rect(0, 0, W, H), buffer, W * 4, 0);
    }

    Forms.Screen? FindTarget()
    {
        var id = App.Cfg.DisplayDevice;
        if (string.IsNullOrWhiteSpace(id))
            return Forms.Screen.PrimaryScreen;
        return Forms.Screen.AllScreens.FirstOrDefault(s =>
            string.Equals(s.DeviceName, id, StringComparison.OrdinalIgnoreCase));
    }
}
