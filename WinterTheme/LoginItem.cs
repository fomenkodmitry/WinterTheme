namespace WinterTheme;

/// <summary>"Open at login" through a per-user LaunchAgent (works for ad-hoc signed apps, unlike SMAppService).</summary>
public static class LoginItem
{
    private const string Label = "local.wintertheme";

    private static readonly string PlistPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", Label + ".plist");

    /// <summary>Path of the running .app bundle, or null when started with `dotnet run`.</summary>
    public static string? BundlePath
    {
        get
        {
            var exe = Environment.ProcessPath;
            if (exe == null) return null;
            int i = exe.IndexOf(".app/Contents/MacOS/", StringComparison.Ordinal);
            return i < 0 ? null : exe[..(i + 4)];
        }
    }

    public static bool Available => BundlePath != null;

    public static bool Enabled => File.Exists(PlistPath);

    public static void Set(bool on)
    {
        try
        {
            if (!on)
            {
                if (File.Exists(PlistPath)) File.Delete(PlistPath);
                return;
            }
            var bundle = BundlePath;
            if (bundle == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
            File.WriteAllText(PlistPath, $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                  <key>Label</key><string>{Label}</string>
                  <key>ProgramArguments</key>
                  <array>
                    <string>/usr/bin/open</string>
                    <string>-a</string>
                    <string>{System.Security.SecurityElement.Escape(bundle)}</string>
                  </array>
                  <key>RunAtLoad</key><true/>
                </dict>
                </plist>
                """);
        }
        catch
        {
            // non-critical
        }
    }
}
