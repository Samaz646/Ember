using System;
using System.Linq;
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

    public static void Start()
    {
        if (instance != null) return;
        instance = new DisplayOutput();
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
            // WPF window coordinates are DIPs. Use the current HwndSource
            // transform to convert physical screen pixels correctly.
            var source = System.Windows.PresentationSource.FromVisual(this);
            var matrix = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var tl = matrix.Transform(new Point(b.Left, b.Top));
            var br = matrix.Transform(new Point(b.Right, b.Bottom));
            Left = tl.X;
            Top = tl.Y;
            Width = Math.Max(1, br.X - tl.X);
            Height = Math.Max(1, br.Y - tl.Y);
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
