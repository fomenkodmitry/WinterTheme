using SkiaSharp;
using WinterTheme.Native;
using WinterTheme.Render;
using WinterTheme.Sim;

namespace WinterTheme.Demo;

/// <summary>
/// Renders README screenshots offscreen: the real simulation and painter on top of a mock desktop.
/// Run with: WinterTheme --render-screens &lt;dir&gt;
/// </summary>
public static class DemoRenderer
{
    private const float W = 1280, H = 800, MenuH = 25, Scale = 2;
    private const float Dt = 1 / 30f;

    private sealed record MockWindow(int Id, SKRect Rect, bool Dark, string Title);

    private static readonly MockWindow[] Windows =
    {
        new(2, new SKRect(90, 170, 640, 560), true, "Terminal"),
        new(1, new SKRect(520, 110, 1170, 520), false, "WinterTheme — World.cs"),
    };

    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        var s = Settings.Current; // in-memory only, never saved here
        s.AutoTractor = s.RandomGrinch = s.RandomSanta = false;
        s.SnowOnWindows = s.Garland = true;
        s.Melt = s.Paused = false;

        Hero(dir);
        Tractor(dir);
        Grinch(dir);
        Building(dir);
    }

    private static World NewWorld(bool withWindows = true)
    {
        var screen = new Cocoa.ScreenInfo(0, new CGRect(0, 0, W, H), new CGRect(0, 0, W, H), MenuH, 0);
        var w = new World(screen);
        w.UpdateWindows(withWindows
            ? Windows.Select(m => new Quartz.WinInfo(m.Id, new CGRect(m.Rect.Left, m.Rect.Top, m.Rect.Width, m.Rect.Height))).ToList()
            : new List<Quartz.WinInfo>());
        return w;
    }

    private static void Simulate(World w, float seconds)
    {
        for (float t = 0; t < seconds; t += Dt) w.Update(Dt);
    }

    /// <summary>Snow for a while in a blizzard, then calm down to normal snowfall.</summary>
    private static void Accumulate(World w, float blizzard)
    {
        Settings.Current.Intensity = 2;
        Simulate(w, blizzard);
        Settings.Current.Intensity = 1;
        Simulate(w, 25);
    }

    private static Ball Stack(World w, float x, params float[] radii)
    {
        Ball? below = null, root = null;
        foreach (var r in radii)
        {
            var b = new Ball { X = x, R = r, Y = w.Ground.SurfaceY(x) - r, Below = below };
            if (below != null) below.Above = b;
            root ??= b;
            below = b;
            w.Balls.Add(b);
        }
        return root!;
    }

    // ------------------------------------------------------------------ shots

    private static void Hero(string dir)
    {
        var w = NewWorld();
        Accumulate(w, 200);

        var man = Stack(w, 930, 46, 33, 23);
        w.MakeSnowman(man, hat: 3, scarf: 0xFFDC2626);
        Stack(w, 1090, 30, 21);
        Stack(w, 300, 18);
        w.Presents.Add(new Present { X = 1180, Y = H - 30, Size = 22, Landed = true, Box = new SKColor(37, 99, 235), Ribbon = new SKColor(250, 204, 21) });

        w.StartSanta();
        w.Santa.Dir = -1;
        w.Santa.X = 760;
        w.Santa.Y0 = 70;
        w.Santa.Dropped = true;
        Simulate(w, 1.6f);
        w.Bubbles.Clear();

        Save(w, Path.Combine(dir, "hero.jpg"));
    }

    private static void Tractor(string dir)
    {
        var w = NewWorld();
        Accumulate(w, 150);
        // a deep drift for the plow to chew through
        for (int i = 0; i < w.Ground.N; i++)
            w.Ground.H[i] = Math.Max(w.Ground.H[i], 38 + 10 * MathF.Sin(i * 0.07f) + 5 * MathF.Sin(i * 0.31f));

        w.StartTractor();
        w.Tractor.Dir = 1;
        w.Tractor.X = -170;
        w.Bubbles.Clear();
        while (w.Tractor.X < W * 0.52f) w.Update(Dt);
        w.Say("Бип-бип! Чищу!", () => new SKPoint(W * 0.52f, H - 135), 3);

        Save(w, Path.Combine(dir, "tractor.jpg"));
    }

    private static void Grinch(string dir)
    {
        var w = NewWorld();
        Accumulate(w, 160);
        var man = Stack(w, 760, 44, 31, 22);
        w.MakeSnowman(man, hat: 0, scarf: 0xFF2563EB);
        w.Bubbles.Clear();

        w.StartGrinch();
        int guard = 0;
        while (w.Grinch.State != GrinchState.Escape && guard++ < 5000) w.Update(Dt);
        Simulate(w, 0.35f);

        Save(w, Path.Combine(dir, "grinch.jpg"));
    }

    private static void Building(string dir)
    {
        var w = NewWorld();
        Accumulate(w, 220);
        var body = Stack(w, 1000, 42, 30);
        w.Bubbles.Clear();

        // roll a ball across the drift: same code path as a real drag
        w.MouseInside = true;
        w.OptionDown = true;
        w.Mx = 260;
        w.My = w.Ground.SurfaceY(260) - 6;
        w.Update(Dt);
        w.MouseDown = true;
        w.Update(Dt); // previous frame was interactive -> press creates a ball
        for (int k = 0; k < 140; k++)
        {
            w.Mx += 3.2f;
            w.My = w.Ground.SurfaceY(w.Mx) - 6;
            w.Update(Dt);
        }
        w.OptionDown = false;
        w.Say("Ещё немного — и будет низ снеговика", () => new SKPoint(w.Held?.X ?? 600, (w.Held?.Y ?? 700) - (w.Held?.R ?? 20) - 30), 3);
        _ = body;

        Save(w, Path.Combine(dir, "building.jpg"));
    }

    // ------------------------------------------------------------------ output

    private static void Save(World w, string path)
    {
        var info = new SKImageInfo((int)(W * Scale), (int)(H * Scale), SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var c = surface.Canvas;
        c.Scale(Scale);
        DrawDesktop(c);
        c.SaveLayer();
        new Painter().Draw(c, w);
        c.Restore();
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, 88);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine($"wrote {path}");
    }

    private static void DrawDesktop(SKCanvas c)
    {
        using var p = new SKPaint { IsAntialias = true };

        // wallpaper: winter dusk
        using (var sky = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, H),
                   new[] { new SKColor(20, 30, 70), new SKColor(70, 80, 140), new SKColor(170, 120, 150) }, new[] { 0f, 0.6f, 1f },
                   SKShaderTileMode.Clamp))
        {
            p.Shader = sky;
            c.DrawRect(0, 0, W, H, p);
            p.Shader = null;
        }
        p.Color = new SKColor(40, 50, 95);
        using (var hills = new SKPath())
        {
            hills.MoveTo(0, 640);
            hills.QuadTo(260, 560, 520, 630);
            hills.QuadTo(820, 700, 1050, 600);
            hills.QuadTo(1180, 560, W, 610);
            hills.LineTo(W, H);
            hills.LineTo(0, H);
            hills.Close();
            c.DrawPath(hills, p);
        }

        // windows, back to front
        foreach (var m in Windows.Reverse()) DrawWindow(c, m);

        // menu bar
        p.Color = new SKColor(20, 22, 35, 170);
        c.DrawRect(0, 0, W, MenuH, p);
        using var font = new SKFont(SKTypeface.FromFamilyName("Helvetica Neue", SKFontStyle.Bold), 13);
        using var thin = new SKFont(SKTypeface.FromFamilyName("Helvetica Neue", SKFontStyle.Normal), 13);
        p.Color = SKColors.White;
        c.DrawText("Finder", 16, 17, font, p);
        string[] menus = { "File", "Edit", "View", "Go", "Window", "Help" };
        float x = 76;
        foreach (var t in menus)
        {
            c.DrawText(t, x, 17, thin, p);
            x += t.Length * 7.5f + 18;
        }
        c.DrawText("Mon 22 Dec  18:42", W - 135, 17, thin, p);
        DrawFlakeIcon(c, W - 160, 12.5f);

        // dock
        var dock = new SKRect(W / 2 - 250, H - 68, W / 2 + 250, H - 6);
        p.Color = new SKColor(255, 255, 255, 60);
        c.DrawRoundRect(dock, 18, 18, p);
        SKColor[] icons =
        {
            new(80, 160, 255), new(255, 90, 80), new(60, 200, 120), new(250, 200, 60), new(150, 100, 230),
            new(40, 40, 50), new(255, 140, 60), new(90, 210, 220),
        };
        for (int i = 0; i < icons.Length; i++)
        {
            p.Color = icons[i];
            c.DrawRoundRect(dock.Left + 18 + i * 59, dock.Top + 8, 48, 48, 11, 11, p);
        }
    }

    private static void DrawFlakeIcon(SKCanvas c, float cx, float cy)
    {
        using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, Color = SKColors.White, StrokeCap = SKStrokeCap.Round };
        for (int k = 0; k < 3; k++)
        {
            float a = k * MathF.PI / 3;
            c.DrawLine(cx - MathF.Cos(a) * 7, cy - MathF.Sin(a) * 7, cx + MathF.Cos(a) * 7, cy + MathF.Sin(a) * 7, p);
        }
    }

    private static void DrawWindow(SKCanvas c, MockWindow m)
    {
        var r = m.Rect;
        using var p = new SKPaint { IsAntialias = true };
        using (var shadow = SKImageFilter.CreateDropShadow(0, 10, 18, 18, new SKColor(0, 0, 0, 120)))
        {
            p.ImageFilter = shadow;
            p.Color = m.Dark ? new SKColor(30, 30, 36) : SKColors.White;
            c.DrawRoundRect(r, 10, 10, p);
            p.ImageFilter = null;
        }

        // title bar
        c.Save();
        c.ClipRoundRect(new SKRoundRect(r, 10, 10), antialias: true);
        p.Color = m.Dark ? new SKColor(48, 48, 56) : new SKColor(236, 236, 240);
        c.DrawRect(r.Left, r.Top, r.Width, 30, p);
        c.Restore();
        SKColor[] lights = { new(255, 95, 87), new(254, 188, 46), new(40, 200, 64) };
        for (int i = 0; i < 3; i++)
        {
            p.Color = lights[i];
            c.DrawCircle(r.Left + 18 + i * 20, r.Top + 15, 6, p);
        }
        using var font = new SKFont(SKTypeface.FromFamilyName("Helvetica Neue", SKFontStyle.Bold), 12.5f);
        p.Color = m.Dark ? new SKColor(200, 200, 210) : new SKColor(70, 70, 80);
        using var mp = new SKPaint { Typeface = font.Typeface, TextSize = font.Size };
        float tw = mp.MeasureText(m.Title);
        c.DrawText(m.Title, r.MidX - tw / 2, r.Top + 19, font, p);

        // content: fake code / shell lines
        var rng = new Random(m.Id * 17);
        using var mono = new SKFont(SKTypeface.FromFamilyName("Menlo", SKFontStyle.Normal), 12);
        float y = r.Top + 54;
        if (m.Dark)
        {
            string[] lines =
            {
                "$ ./build-app.sh", "==> publish osx-arm64", "==> bundle", "==> sign (ad-hoc)", "==> dmg",
                "Built dist/WinterTheme-1.0.0-arm64.dmg", "$ open dist/WinterTheme.app", "$ █",
            };
            foreach (var line in lines)
            {
                p.Color = line.StartsWith('$') ? new SKColor(120, 220, 140) : new SKColor(210, 210, 220);
                c.DrawText(line, r.Left + 16, y, mono, p);
                y += 20;
            }
            return;
        }

        SKColor[] cols = { new(175, 82, 222), new(0, 122, 255), new(52, 199, 89), new(255, 149, 0), new(142, 142, 147) };
        int n = 0;
        while (y < r.Bottom - 14)
        {
            p.Color = new SKColor(180, 180, 190);
            c.DrawText((++n).ToString().PadLeft(2), r.Left + 12, y, mono, p);
            float x = r.Left + 48 + rng.Next(0, 4) * 18;
            int words = rng.Next(1, 5);
            for (int k = 0; k < words && x < r.Right - 60; k++)
            {
                float len = rng.Next(30, 120);
                p.Color = cols[rng.Next(cols.Length)].WithAlpha(150);
                c.DrawRoundRect(x, y - 9, len, 10, 3, 3, p);
                x += len + 8;
            }
            y += 20;
        }
    }
}
