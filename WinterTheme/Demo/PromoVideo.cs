using SkiaSharp;
using WinterTheme.Native;
using WinterTheme.Render;
using WinterTheme.Sim;

namespace WinterTheme.Demo;

/// <summary>
/// A scripted horizontal promo clip (1920x1080, 30 fps) rendered offscreen with the real simulation and painter.
/// Run with: WinterTheme --render-video &lt;dir&gt;, then encode the JPEG frames, e.g.
/// ffmpeg -framerate 30 -i &lt;dir&gt;/f%05d.jpg -c:v libx264 -pix_fmt yuv420p -crf 18 promo.mp4
/// </summary>
public static partial class DemoRenderer
{
    private const int VideoW = 1920, VideoH = 1080;

    private static World _w = null!;
    private static Painter _painter = null!;
    private static SKSurface _surface = null!;
    private static string _dir = "";
    private static int _frame;

    // scripted cursor and captions
    private static float _cx = 640, _cy = 380;
    private static bool _down, _option;
    private static string? _title, _sub;
    private static int _captionFrame;
    private static float _keyFade;

    public static void RenderPromo(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir, "f*.jpg")) File.Delete(f);
        _dir = dir;

        var s = Settings.Current; // in-memory only, never saved here
        s.AutoTractor = s.RandomGrinch = s.RandomSanta = false;
        s.Chatter = s.Birds = s.Aurora = s.ShootingStars = s.NewYearShow = false;
        s.SnowOnWindows = s.Garland = s.Icicles = true;
        s.Melt = s.Paused = false;
        s.CursorPush = true;

        W = 1280;
        H = 720;
        Scale = VideoW / W;
        _surface = SKSurface.Create(new SKImageInfo(VideoW, VideoH, SKColorType.Rgba8888, SKAlphaType.Premul));
        _painter = new Painter();

        _w = NewWorld();
        Accumulate(_w, 200);
        foreach (var p in _w.PileOrder)
            for (int i = 0; i < p.N; i++) p.H[i] = Math.Max(p.H[i], 6);
        Simulate(_w, 120);
        _w.Particles.Clear();
        _w.Bubbles.Clear();

        // a small crowd for later: the Snow King on the left and a tiny one on the right
        var king = Stack(_w, 190, 66, 46, 31);
        _w.MakeSnowman(king, hat: 4, scarf: 0xFFEC4899, name: "Снежный Король");
        var mini = Stack(_w, 1190, 13, 9, 6);
        _w.MakeSnowman(mini, hat: 2, scarf: 0xFFDC2626);
        // the base of the snowman we build on camera
        var baseStack = Stack(_w, 760, 46, 33);
        Simulate(_w, 2);
        _w.Bubbles.Clear();

        Intro();
        var man = BuildSnowman(baseStack);
        SantaScene();
        GrinchScene(man);
        TractorScene();
        WindowScene();
        NightScene(man, mini, king);
        Finale();

        Console.WriteLine($"promo: {_frame} frames ({_frame / 30f:F1} s) in {dir}");
    }

    // ------------------------------------------------------------------ scenes

    private static void Intro()
    {
        Caption("Зимняя тема для macOS", "снег, снеговики, Санта и Гринч прямо на рабочем столе");
        Hold(3.6f);
    }

    private static Ball BuildSnowman(Ball baseStack)
    {
        Caption("Лепи снеговиков", "зажми Option и катай ком по сугробу");
        float gx = 330;
        MoveTo(gx, _w.Ground.SurfaceY(gx) - 6, 0.9f);
        _option = true;
        Hold(0.3f);
        _down = true;
        Hold(0.1f);
        // roll along the drift
        for (float t = 0; t < 3f && (_w.Held == null || _w.Held.R < 25 || t < 1.2f) && (_w.Held == null || _w.Held.R < 30); t += Dt)
        {
            _cx += 110 * Dt;
            _cy = _w.Ground.SurfaceY(_cx) - 6;
            Frame();
        }
        _option = false;
        var ball = _w.Held;
        Console.WriteLine($"promo: rolled ball r={ball?.R:F1}");
        // carry it onto the base and drop
        float tx = baseStack.X, ty = baseStack.Above!.Y - baseStack.Above.R - (ball?.R ?? 20) - 20;
        // a quick lift off the drift (a slow one keeps rolling it), then carry it over
        MoveTo(_cx + 10, _cy - 110, 0.15f);
        MoveTo(tx, ty, 1.1f, ease: true);
        _down = false;
        Hold(0.2f);
        Console.WriteLine($"promo: snowman made={baseStack.Man != null}");
        Hold(2.2f);
        MoveTo(760, 330, 0.7f);
        return baseStack;
    }

    private static void SantaScene()
    {
        Caption("Санта дарит подарки", "кликни по подарку, чтобы открыть");
        _w.StartSanta();
        var s = _w.Santa;
        s.Dir = -1;
        s.X = 1150;
        s.Y0 = 110;
        s.DropX = 640;
        s.Dropped = false;
        _w.Bubbles.Clear();
        // wait for the present to land
        for (int k = 0; k < 30 * 6 && !_w.Presents.Any(p => p.Landed); k++) Frame();
        var present = _w.Presents.FirstOrDefault(p => p.Landed);
        Hold(0.4f);
        if (present != null)
        {
            MoveTo(present.X, present.Y - 2, 0.9f, ease: true);
            Hold(0.2f);
            _w.NextPresentPlain = true;
            Click();
            var bubble = _w.Bubbles.LastOrDefault();
            if (bubble != null) bubble.Text = "Прод не упадёт до 9 января!";
        }
        Hold(2.4f);
    }

    private static void GrinchScene(Ball man)
    {
        Caption("Гринч ворует морковку", "закидай его снежками");
        MoveTo(990, 330, 0.6f);
        _w.Presents.Clear();
        _w.StartGrinch();
        var g = _w.Grinch;
        g.Target = man;
        g.Dir = 1;
        g.X = 470;
        _w.Bubbles.Clear();
        for (int k = 0; k < 30 * 4 && g.State != GrinchState.Escape && !g.HasCarrot; k++) Frame();
        Hold(0.3f);

        // three snowballs from the cursor
        int throws = 0;
        // throw once he is past the snowman, so the snowballs fly clear of it
        for (int k = 0; k < 30 * 5 && g.Active && !g.Reformed; k++)
        {
            if (g.X > man.X + 70 && k % 9 == 0 && throws < 7)
            {
                throws++;
                Flick(g);
            }
            Frame();
        }
        if (g.Reformed) Caption("Три снежка — и Гринч исправился", "а нос вернулся к снеговику");
        Hold(3.4f);
        Console.WriteLine($"promo: grinch reformed={g.Reformed} hits={g.Hits} nose={man.Man?.HasNose}");
        g.Active = false;
        _w.Presents.Clear();
        _w.Bubbles.Clear();
    }

    /// <summary>A quick flick of the cursor that launches a snowball at the Grinch.</summary>
    private static void Flick(Grinch g)
    {
        float feet = _w.Ground.SurfaceY(g.X);
        float x0 = _cx + 10, y0 = _cy - 10;
        const float t = 0.42f;
        float speed = g.State switch { GrinchState.Escape => g.Reformed ? 90 : 280, GrinchState.Run => 170, _ => 0 };
        float tx = g.X + g.Dir * speed * t, ty = feet - 55;
        _w.Balls.Add(new Ball { X = x0, Y = y0, R = 7, Flying = true, VX = (tx - x0) / t, VY = (ty - y0 - 0.5f * 900 * t * t) / t });
        _cx += 14;
        _cy -= 10;
    }

    private static void TractorScene()
    {
        Caption("Трактор сам чистит сугробы", null);
        MoveTo(640, 300, 0.5f);
        for (int i = 0; i < _w.Ground.N; i++)
            _w.Ground.H[i] = Math.Max(_w.Ground.H[i], 34 + 8 * MathF.Sin(i * 0.07f) + 4 * MathF.Sin(i * 0.31f));
        _w.StartTractor();
        _w.Tractor.Dir = 1;
        _w.Tractor.X = -120;
        _w.Tractor.Speed = 170;
        Hold(5.2f);
        _w.Tractor.Active = false;
        _w.Ground.Wall = -1;
        _w.Bubbles.Clear();
    }

    private static void WindowScene()
    {
        Caption("Ставь снеговиков на окна", "они едут вместе с окном");
        int idx = Array.FindIndex(Windows, m => m.Id == 1);
        var start = Windows[idx].Rect;
        var rider = Stack(_w, start.Left + 330, 22, 16, 11);
        _w.MakeSnowman(rider, hat: 3, scarf: 0xFFEAB308, name: "Пассажир");
        foreach (var b in rider.StackFromRoot()) b.Y -= 90 + b.R;
        rider.Y = start.Top - 70;
        _w.Bubbles.Clear();
        MoveTo(start.MidX, start.Top + 12, 1.0f, ease: true);
        Hold(0.4f);

        // drag the window by its title bar: the snowman rides along
        _down = true;
        MoveWindow(idx, start, 110, 40, 1.6f);
        Caption("Ставь снеговиков на окна", "а резко дёрнешь — разобьётся");
        MoveWindow(idx, Windows[idx].Rect, 70, 0, 0.25f);
        MoveWindow(idx, Windows[idx].Rect, -260, -10, 0.25f);
        _down = false;
        Hold(2.6f);
        Console.WriteLine($"promo: rider broken={!_w.Balls.Contains(rider)}");

        // put the window back
        MoveWindow(idx, Windows[idx].Rect, start.Left - Windows[idx].Rect.Left, start.Top - Windows[idx].Rect.Top, 0.01f);
        _w.Bubbles.Clear();
    }

    private static void MoveWindow(int idx, SKRect from, float dx, float dy, float seconds)
    {
        int n = Math.Max(1, (int)(seconds * 30));
        for (int k = 1; k <= n; k++)
        {
            float t = k / (float)n;
            var r = from;
            r.Offset(dx * t, dy * t);
            Windows[idx] = Windows[idx] with { Rect = r };
            _cx = r.MidX;
            _cy = r.Top + 12;
            Frame();
        }
    }

    private static void NightScene(Ball man, Ball mini, Ball king)
    {
        Caption("Снегири, северное сияние", "и пасхалки: тапни снеговика 5 раз");
        _w.StartAurora(40);
        _w.SpawnFlock();
        _w.SpawnFlock();
        _w.ShootingStar();
        _w.Bubbles.Clear();
        var head = man.Top;
        MoveTo(man.X, man.Above!.Y, 1.4f, ease: true);
        Hold(0.8f);
        for (int k = 0; k < 5; k++)
        {
            Click(2);
            Hold(0.08f);
        }
        MoveTo(man.X + 90, man.Above.Y - 120, 0.6f, ease: true);
        Hold(6.5f);
        _ = head;
        _ = mini;
        _ = king;
    }

    private static void Finale()
    {
        Caption("С Новым годом!", "github.com/fomenkodmitry/WinterTheme");
        _w.StartFireworks(10);
        for (int k = 0; k < 6; k++) _w.Confetti(1280 * (k + 0.5f) / 6, MenuH + 10, 40);
        _w.Bubbles.Clear();
        MoveTo(1260, 700, 1.0f);
        Hold(5.5f);
    }

    // ------------------------------------------------------------------ timeline helpers

    private static void Caption(string title, string? sub)
    {
        _title = title;
        _sub = sub;
        _captionFrame = _frame;
    }

    private static void Hold(float seconds)
    {
        int n = (int)MathF.Round(seconds * 30);
        for (int k = 0; k < n; k++) Frame();
    }

    private static void MoveTo(float x, float y, float seconds, bool ease = false)
    {
        float x0 = _cx, y0 = _cy;
        int n = Math.Max(1, (int)(seconds * 30));
        for (int k = 1; k <= n; k++)
        {
            float t = k / (float)n;
            if (ease) t = t * t * (3 - 2 * t);
            _cx = x0 + (x - x0) * t;
            _cy = y0 + (y - y0) * t;
            Frame();
        }
    }

    private static void Click(int holdFrames = 3)
    {
        _down = true;
        for (int k = 0; k < holdFrames; k++) Frame();
        _down = false;
        Frame();
    }

    private static void Frame()
    {
        _w.FeedInput(_cx, _cy, _down, _option);
        if (_frame % 3 == 0)
            _w.UpdateWindows(Windows.Select(m => new WinInfoFor(m)).Select(x => x.Info).ToList());
        _w.Update(Dt);
        RenderVideoFrame();
        _frame++;
    }

    private readonly record struct WinInfoFor(MockWindow M)
    {
        public Quartz.WinInfo Info => new(M.Id, new CGRect(M.Rect.Left, M.Rect.Top, M.Rect.Width, M.Rect.Height));
    }

    // ------------------------------------------------------------------ drawing

    private static readonly SKTypeface Bold = SKTypeface.FromFamilyName("Helvetica Neue", SKFontStyle.Bold);
    private static readonly SKTypeface Regular = SKTypeface.FromFamilyName("Helvetica Neue", SKFontStyle.Normal);
    private static readonly SKTypeface Symbols = SKFontManager.Default.MatchCharacter('⌥') ?? Regular;

    private static void RenderVideoFrame()
    {
        var c = _surface.Canvas;
        c.ResetMatrix();
        c.Clear(SKColors.Black);
        c.Scale(Scale);
        DrawDesktop(c);
        c.SaveLayer();
        _painter.Draw(c, _w);
        c.Restore();
        DrawCursor(c);
        DrawKeycap(c);
        DrawCaption(c);
        c.Flush();

        using var img = _surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, 92);
        File.WriteAllBytes(Path.Combine(_dir, $"f{_frame:D5}.jpg"), data.ToArray());
    }

    private static void DrawCursor(SKCanvas c)
    {
        using var path = new SKPath();
        path.MoveTo(0, 0);
        path.LineTo(0, 17);
        path.LineTo(4.2f, 13.2f);
        path.LineTo(7, 19.6f);
        path.LineTo(9.6f, 18.5f);
        path.LineTo(6.9f, 12.3f);
        path.LineTo(12.3f, 12.3f);
        path.Close();
        c.Save();
        c.Translate(_cx, _cy);
        float s = _down ? 0.92f : 1f;
        c.Scale(s);
        using var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 70), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.5f) };
        c.Save();
        c.Translate(1, 1.5f);
        c.DrawPath(path, shadow);
        c.Restore();
        using var fill = new SKPaint { IsAntialias = true, Color = SKColors.Black };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.3f, Color = SKColors.White, StrokeJoin = SKStrokeJoin.Round };
        c.DrawPath(path, stroke);
        c.DrawPath(path, fill);
        c.Restore();
        if (_down)
        {
            using var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2, Color = new SKColor(255, 255, 255, 160) };
            c.DrawCircle(_cx, _cy, 9, ring);
        }
    }

    /// <summary>A keycap in the corner while ⌥ is held, so viewers see what the hand does.</summary>
    private static void DrawKeycap(SKCanvas c)
    {
        _keyFade = Math.Clamp(_keyFade + (_option ? 0.2f : -0.2f), 0, 1);
        if (_keyFade <= 0) return;
        float a = _keyFade;
        var rect = SKRect.Create(36, 78, 120, 56);
        using var bg = new SKPaint { IsAntialias = true, Color = new SKColor(250, 250, 252, (byte)(235 * a)) };
        using var edge = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, (byte)(60 * a)) };
        c.DrawRoundRect(SKRect.Create(rect.Left, rect.Top + 4, rect.Width, rect.Height), 10, 10, edge);
        c.DrawRoundRect(rect, 10, 10, bg);
        using var sym = new SKFont(Symbols, 26);
        using var txt = new SKFont(Regular, 15);
        using var ink = new SKPaint { IsAntialias = true, Color = new SKColor(40, 40, 50, (byte)(255 * a)) };
        c.DrawText("⌥", rect.Left + 14, rect.Top + 36, sym, ink);
        c.DrawText("option", rect.Left + 50, rect.Top + 34, txt, ink);
    }

    private static void DrawCaption(SKCanvas c)
    {
        if (_title == null) return;
        float age = (_frame - _captionFrame) / 30f;
        float a = Math.Clamp(age / 0.35f, 0, 1);
        float rise = (1 - a) * 12;
        using var titleFont = new SKFont(Bold, 36);
        using var subFont = new SKFont(Regular, 20);
        using var measure = new SKPaint { IsAntialias = true };
        float Width(SKFont f, string text)
        {
            measure.Typeface = f.Typeface;
            measure.TextSize = f.Size;
            return measure.MeasureText(text);
        }
        float tw = Width(titleFont, _title), sw = _sub == null ? 0 : Width(subFont, _sub);
        // centered on the dark terminal window, where nothing else happens
        float cx = 365, cy = 345;
        float bw = Math.Max(tw, sw) + 56, bh = _sub == null ? 66 : 100;
        float x = cx - bw / 2, y = cy - bh / 2 + rise;
        using var bg = new SKPaint { IsAntialias = true, Color = new SKColor(12, 20, 40, (byte)(200 * a)) };
        c.DrawRoundRect(SKRect.Create(x, y, bw, bh), 22, 22, bg);
        using var ink = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha((byte)(255 * a)) };
        c.DrawText(_title, cx - tw / 2, y + 46, titleFont, ink);
        if (_sub != null)
        {
            ink.Color = new SKColor(200, 222, 255, (byte)(255 * a));
            c.DrawText(_sub, cx - sw / 2, y + 80, subFont, ink);
        }
    }
}
