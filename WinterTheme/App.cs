using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using WinterTheme.Native;
using WinterTheme.Sim;
using WinterTheme.UI;

namespace WinterTheme;

public sealed class App : Application
{
    private readonly List<OverlayWindow> _overlays = new();
    private string _screenSig = "";

    public IEnumerable<World> Worlds => _overlays.Select(o => o.World);

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!OperatingSystem.IsMacOS())
            {
                Console.Error.WriteLine("WinterTheme is macOS-only.");
                desktop.Shutdown(1);
                return;
            }

            BuildOverlays();
            Tray.Create(this);

            new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => PollWindows()).Start();
            new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) => CheckScreens()).Start();
            new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => StateStore.Save(Worlds)).Start();
            desktop.Exit += (_, _) => StateStore.Save(Worlds);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static string Signature(List<Cocoa.ScreenInfo> screens) =>
        string.Join("|", screens.Select(s => $"{s.Cg} {s.MenuBarH}"));

    private void BuildOverlays()
    {
        var screens = Cocoa.Screens();
        _screenSig = Signature(screens);
        foreach (var s in screens)
        {
            var w = new OverlayWindow(s);
            StateStore.Load(w.World);
            _overlays.Add(w);
            w.Show();
        }
        PollWindows();
    }

    private void CheckScreens()
    {
        var sig = Signature(Cocoa.Screens());
        if (sig == _screenSig) return;
        StateStore.Save(Worlds);
        foreach (var o in _overlays) o.Close();
        _overlays.Clear();
        BuildOverlays();
    }

    private void PollWindows()
    {
        var wins = Quartz.VisibleWindows();
        foreach (var o in _overlays)
            lock (o.World)
                o.World.UpdateWindows(wins);
    }

    public void ForEachWorld(Action<World> action)
    {
        foreach (var w in Worlds)
            lock (w)
                action(w);
    }

    /// <summary>Runs an event on the display under the mouse cursor (falls back to the main display).</summary>
    public void OnActiveWorld(Action<World> action)
    {
        var w = Worlds.FirstOrDefault(x => x.MouseInside) ?? Worlds.FirstOrDefault();
        if (w == null) return;
        lock (w) action(w);
    }

    public void ApplyWindowSettings()
    {
        foreach (var o in _overlays) o.ApplySettings();
        PollWindows();
    }

    public void Quit()
    {
        StateStore.Save(Worlds);
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
