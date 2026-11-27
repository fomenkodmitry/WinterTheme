using Avalonia;
using Avalonia.Controls;

namespace WinterTheme;

internal static class Program
{
    private static FileStream? _instanceLock;

    [STAThread]
    public static int Main(string[] args)
    {
        // build helper: render the .icns source images
        if (args.Length == 2 && args[0] == "--export-iconset")
        {
            UI.IconFactory.ExportIconset(args[1]);
            return 0;
        }

        // README screenshots, rendered offscreen with the real simulation and painter
        if (args.Length == 2 && args[0] == "--render-screens")
        {
            Demo.DemoRenderer.Run(args[1]);
            return 0;
        }

        // promo video frames (encoded to mp4 separately)
        if (args.Length == 2 && args[0] == "--render-video")
        {
            Demo.DemoRenderer.RenderPromo(args[1]);
            return 0;
        }

        // one snow layer is enough: quit if another copy is already running
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            _instanceLock = new FileStream(Path.Combine(Settings.Dir, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return 0;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .With(new AvaloniaNativePlatformOptions
            {
                RenderingMode = new[]
                {
                    AvaloniaNativeRenderingMode.Metal,
                    AvaloniaNativeRenderingMode.OpenGl,
                    AvaloniaNativeRenderingMode.Software,
                },
            })
            .LogToTrace();
}
