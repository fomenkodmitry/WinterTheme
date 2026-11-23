using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WinterTheme.Native;
using WinterTheme.Sim;

namespace WinterTheme.UI;

/// <summary>Transparent, borderless, click-through window covering one display.</summary>
public sealed class OverlayWindow : Window
{
    public readonly World World;
    private IntPtr _ns;
    private bool _interactive;
    private IntPtr _prevApp;
    private float _reassert;

    public OverlayWindow(Cocoa.ScreenInfo screen)
    {
        World = new World(screen);
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        ShowInTaskbar = false;
        CanResize = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = screen.Cg.W;
        Height = screen.Cg.H;
        Position = new PixelPoint((int)screen.Cg.X, (int)screen.Cg.Y);
        Content = new SceneControl(World, this);
        Opened += (_, _) => ConfigureNative();
    }

    private void ConfigureNative()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var h = TryGetPlatformHandle();
        if (h == null) return;
        _ns = h.Handle;
        if (h.HandleDescriptor == "NSView") _ns = ObjC.Get(_ns, "window");
        Cocoa.ConfigureOverlay(_ns, World.Screen.CocoaFrame, Settings.Current.ShowInScreenshots);
    }

    public void ApplySettings()
    {
        if (_ns != IntPtr.Zero) Cocoa.ApplyLevel(_ns, Settings.Current.ShowInScreenshots);
    }

    /// <summary>Called once per simulation frame on the UI thread.</summary>
    public void Tick()
    {
        if (_ns == IntPtr.Zero) return;

        bool want = World.HoverInteractive;
        if (want != _interactive)
        {
            _interactive = want;
            if (want)
            {
                Cocoa.Release(_prevApp);
                _prevApp = Cocoa.FrontmostApp();
            }
            Cocoa.SetIgnoresMouse(_ns, !want);
            Cursor = want && World.HoverBall ? new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) : Avalonia.Input.Cursor.Default;
        }

        if (World.ClickReleased)
        {
            World.ClickReleased = false;
            Cocoa.RestoreFront(_prevApp);
        }

        _reassert -= 1 / 60f;
        if (_reassert <= 0)
        {
            _reassert = 3;
            Cocoa.ApplyLevel(_ns, Settings.Current.ShowInScreenshots);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Cocoa.Release(_prevApp);
        _prevApp = IntPtr.Zero;
        base.OnClosed(e);
    }
}
