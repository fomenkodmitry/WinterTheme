using SkiaSharp;
using WinterTheme.Native;
using WinterTheme.Render;
using WinterTheme.Sim;

namespace WinterTheme.Demo;

/// <summary>
/// A scripted promo clip rendered offscreen with the real simulation and painter, 30 fps JPEG frames:
/// vertical 1080x1920 for Reels/Shorts/TikTok by default, or horizontal 1920x1080.
/// Run with: WinterTheme --render-video &lt;dir&gt; [vertical|horizontal]; promo.sh encodes it with ffmpeg.
/// </summary>
public static partial class DemoRenderer
{
    /// <summary>Where things go in one orientation. Coordinates are mock-desktop points.</summary>
    private sealed record Layout(
        float W, float H, float Scale,
        SKRect Terminal, SKRect Editor,
        float KingX, float BaseX, float MiniX,
        float RollFrom, float RollTo,
        float SantaX, float SantaY, float SantaDropX, int SantaDir,
        float GrinchX, int GrinchDir, SKPoint ThrowFrom, float GrinchSpeed,
        float TractorSpeed, float TractorHold,
        float RiderDx, SKPoint WindowDrag, float JerkOut, float JerkBack,
        SKPoint CaptionCenter, float CaptionMaxW,
        SKPoint IntroCursor, SKPoint EndCursor);

    private static readonly Layout Vertical = new(
        W: 540, H: 960, Scale: 2,
        Terminal: new SKRect(20, 470, 520, 730), Editor: new SKRect(40, 150, 500, 430),
        KingX: 400, BaseX: 115, MiniX: 498,
        RollFrom: 185, RollTo: 290,
        SantaX: 560, SantaY: 92, SantaDropX: 270, SantaDir: -1,
        GrinchX: -60, GrinchDir: 1, ThrowFrom: new SKPoint(165, 630), GrinchSpeed: 0.45f,
        TractorSpeed: 110, TractorHold: 5.0f,
        RiderDx: 300, WindowDrag: new SKPoint(40, 60), JerkOut: 50, JerkBack: -200,
        CaptionCenter: new SKPoint(270, 600), CaptionMaxW: 500,
        IntroCursor: new SKPoint(300, 380), EndCursor: new SKPoint(470, 880));

    private static readonly Layout Horizontal = new(
        W: 1280, H: 720, Scale: 1.5f,
        Terminal: new SKRect(90, 170, 640, 560), Editor: new SKRect(520, 110, 1170, 520),
        KingX: 190, BaseX: 760, MiniX: 1190,
        RollFrom: 330, RollTo: 640,
        SantaX: 1150, SantaY: 110, SantaDropX: 640, SantaDir: -1,
        GrinchX: 470, GrinchDir: 1, ThrowFrom: new SKPoint(990, 330), GrinchSpeed: 1,
        TractorSpeed: 170, TractorHold: 5.2f,
        RiderDx: 330, WindowDrag: new SKPoint(110, 40), JerkOut: 70, JerkBack: -260,
        CaptionCenter: new SKPoint(365, 345), CaptionMaxW: 640,
        IntroCursor: new SKPoint(640, 380), EndCursor: new SKPoint(1260, 700));

    private static Layout L = Vertical;
    private static World _w = null!;
    private static Painter _painter = null!;
    private static SKSurface _surface = null!;
    private static string _dir = "";
    private static int _frame;

    // scripted cursor and captions
    private static float _cx, _cy;
    private static bool _down, _option;
    private static string? _title, _sub;
    private static int _captionFrame;
    private static float _keyFade;

    public static void RenderPromo(string dir, bool vertical = true)
    {
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir, "f*.jpg")) File.Delete(f);
        _dir = dir;
        L = vertical ? Vertical : Horizontal;

        var s = Settings.Current; // in-memory only, never saved here
        s.AutoTractor = s.RandomGrinch = s.RandomSanta = false;
        s.Chatter = s.Birds = s.Aurora = s.ShootingStars = s.NewYearShow = false;
        s.SnowOnWindows = s.Garland = s.Icicles = true;
        s.Melt = s.Paused = false;
        s.CursorPush = true;

        W = L.W;
        H = L.H;
        Scale = L.Scale;
        SetWindow(2, L.Terminal);
        SetWindow(1, L.Editor);
        (_cx, _cy) = (L.IntroCursor.X, L.IntroCursor.Y);
        _surface = SKSurface.Create(new SKImageInfo((int)(W * Scale), (int)(H * Scale), SKColorType.Rgba8888, SKAlphaType.Premul));
        _painter = new Painter();

        _w = NewWorld();
        _w.GrinchSpeed = L.GrinchSpeed;
        Accumulate(_w, 200);
        foreach (var p in _w.PileOrder)
            for (int i = 0; i < p.N; i++) p.H[i] = Math.Max(p.H[i], 6);
        Simulate(_w, 120);
        _w.Particles.Clear();
        _w.Bubbles.Clear();

        // a small crowd for later: the Snow King and a tiny one
        var king = Stack(_w, L.KingX, 66, 46, 31);
        _w.MakeSnowman(king, hat: 4, scarf: 0xFFEC4899, name: "Снежный Король");
        var mini = Stack(_w, L.MiniX, 13, 9, 6);
        _w.MakeSnowman(mini, hat: 2, scarf: 0xFFDC2626);
        // the base of the snowman we build on camera
        var baseStack = Stack(_w, L.BaseX, 46, 33);
        Simulate(_w, 2);
        _w.Bubbles.Clear();

        Intro();
        var man = BuildSnowman(baseStack);
        SantaScene();
        GrinchScene(man);
        TractorScene();
        WindowScene();
        NightScene(man);
        Finale();

        Console.WriteLine($"promo: {_frame} frames ({_frame / 30f:F1} s), {(int)(W * Scale)}x{(int)(H * Scale)}, in {dir}");
    }

    private static void SetWindow(int id, SKRect rect)
    {
        int i = Array.FindIndex(Windows, m => m.Id == id);
        Windows[i] = Windows[i] with { Rect = rect };
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
        float gx = L.RollFrom;
        MoveTo(gx, _w.Ground.SurfaceY(gx) - 6, 0.9f);
        _option = true;
        Hold(0.3f);
        _down = true;
        Hold(0.1f);

        // roll back and forth along the drift until the ball is head-sized
        int dir = 1;
        for (float t = 0; t < 3.5f && (_w.Held == null || _w.Held.R < 25 || t < 1.2f) && (_w.Held == null || _w.Held.R < 30); t += Dt)
        {
            _cx += dir * 110 * Dt;
            if (_cx > L.RollTo) dir = -1;
            if (_cx < L.RollFrom) dir = 1;
            _cy = _w.Ground.SurfaceY(_cx) - 6;
            Frame();
        }
        _option = false;
        var ball = _w.Held;
        Console.WriteLine($"promo: rolled ball r={ball?.R:F1}");

        // a quick lift off the drift (a slow one keeps rolling it), then carry it over and drop
        float tx = baseStack.X, ty = baseStack.Above!.Y - baseStack.Above.R - (ball?.R ?? 20) - 20;
        MoveTo(_cx + 10, _cy - 110, 0.15f);
        MoveTo(tx, ty, 1.1f, ease: true);
        _down = false;
        Hold(0.2f);
        Console.WriteLine($"promo: snowman made={baseStack.Man != null}");
        Hold(2.2f);
        MoveTo(L.IntroCursor.X, L.IntroCursor.Y - 50, 0.7f);
        return baseStack;
    }

    private static void SantaScene()
    {
        Caption("Санта дарит подарки", "кликни по подарку, чтобы открыть");
        _w.StartSanta();
        var s = _w.Santa;
        s.Dir = L.SantaDir;
        s.X = L.SantaX;
        s.Y0 = L.SantaY;
        s.DropX = L.SantaDropX;
        s.Dropped = false;
        _w.Bubbles.Clear();
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
        MoveTo(L.ThrowFrom.X, L.ThrowFrom.Y, 0.6f);
        _w.Presents.Clear();
        _w.StartGrinch();
        var g = _w.Grinch;
        g.Target = man;
        g.Dir = L.GrinchDir;
        g.X = L.GrinchX;
        _w.Bubbles.Clear();
        for (int k = 0; k < 30 * 5 && !g.HasCarrot; k++) Frame();
        Hold(0.2f);

        // snowballs from the cursor once he has the carrot, thrown when they fly clear of the snowman
        int throws = 0;
        for (int k = 0; k < 30 * 5 && g.Active && !g.Reformed; k++)
        {
            bool clear = MathF.Sign(g.X - man.X) != MathF.Sign(_cx - man.X) || MathF.Abs(g.X - man.X) > 70;
            if (clear && k % 6 == 0 && throws < 9)
            {
                throws++;
                Flick(g);
            }
            else Frame();
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
        float feet = _w.Ground.SurfaceY(Math.Clamp(g.X, 0, W - 1));
        float x0 = _cx + 10, y0 = _cy - 10;
        const float t = 0.42f;
        float speed = _w.GrinchSpeed * g.State switch { GrinchState.Escape => g.Reformed ? 90 : 280, GrinchState.Run => 170, _ => 0 };
        float tx = g.X + g.Dir * speed * t, ty = feet - 55;
        _w.Balls.Add(new Ball { X = x0, Y = y0, R = 7, Flying = true, VX = (tx - x0) / t, VY = (ty - y0 - 0.5f * 900 * t * t) / t });
        _cx += 10;
        _cy -= 10;
        Frame();
        _cx -= 10;
        _cy += 10;
    }

    private static void TractorScene()
    {
        Caption("Трактор сам чистит сугробы", null);
        MoveTo(L.IntroCursor.X, L.IntroCursor.Y - 80, 0.5f);
        for (int i = 0; i < _w.Ground.N; i++)
            _w.Ground.H[i] = Math.Max(_w.Ground.H[i], 34 + 8 * MathF.Sin(i * 0.07f) + 4 * MathF.Sin(i * 0.31f));
        _w.StartTractor();
        _w.Tractor.Dir = 1;
        _w.Tractor.X = -120;
        _w.Tractor.Speed = L.TractorSpeed;
        Hold(L.TractorHold);
        _w.Tractor.Active = false;
        _w.Ground.Wall = -1;
        _w.Bubbles.Clear();
    }

    private static void WindowScene()
    {
        Caption("Ставь снеговиков на окна", "они едут вместе с окном");
        int idx = Array.FindIndex(Windows, m => m.Id == 1);
        var start = Windows[idx].Rect;
        var rider = Stack(_w, start.Left + L.RiderDx, 22, 16, 11);
        _w.MakeSnowman(rider, hat: 3, scarf: 0xFFEAB308, name: "Пассажир");
        rider.Y = start.Top - 70;
        _w.Bubbles.Clear();
        MoveTo(start.MidX, start.Top + 12, 1.0f, ease: true);
        Hold(0.4f);

        // drag the window by its title bar: the snowman rides along
        _down = true;
        MoveWindow(idx, start, L.WindowDrag.X, L.WindowDrag.Y, 1.5f);
        Caption("Ставь снеговиков на окна", "а резко дёрнешь — разобьётся");
        MoveWindow(idx, Windows[idx].Rect, L.JerkOut, 0, 0.25f);
        MoveWindow(idx, Windows[idx].Rect, L.JerkBack, -10, 0.25f);
        _down = false;
        Hold(2.6f);
        Console.WriteLine($"promo: rider broken={!_w.Balls.Contains(rider)}");

        // the window goes back to where it was
        MoveWindow(idx, Windows[idx].Rect, start.Left - Windows[idx].Rect.Left, start.Top - Windows[idx].Rect.Top, 0.4f);
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

    private static void NightScene(Ball man)
    {
        Caption("Снегири и северное сияние", "а ещё пасхалки: тапни снеговика 5 раз");
        _w.StartAurora(40);
        _w.SpawnFlock();
        _w.SpawnFlock();
        _w.ShootingStar();
        _w.Bubbles.Clear();
        MoveTo(man.X, man.Above!.Y, 1.4f, ease: true);
        Hold(0.8f);
        for (int k = 0; k < 5; k++)
        {
            Click(2);
            Hold(0.08f);
        }
        MoveTo(Math.Min(W - 40, man.X + 90), man.Above.Y - 120, 0.6f, ease: true);
        Hold(6.5f);
    }

    private static void Finale()
    {
        Caption("С Новым годом!", "github.com/fomenkodmitry/WinterTheme");
        _w.StartFireworks(10);
        for (int k = 0; k < 6; k++) _w.Confetti(W * (k + 0.5f) / 6, MenuH + 10, 40);
        _w.Bubbles.Clear();
        MoveTo(L.EndCursor.X, L.EndCursor.Y, 1.0f);
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
            _w.UpdateWindows(Windows.Select(m => new Quartz.WinInfo(m.Id, new CGRect(m.Rect.Left, m.Rect.Top, m.Rect.Width, m.Rect.Height))).ToList());
        _w.Update(Dt);
        RenderVideoFrame();
        _frame++;
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
        c.Scale(_down ? 0.92f : 1f);
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

    /// <summary>Title and subtitle centered on the dark terminal window, where nothing else happens.</summary>
    private static void DrawCaption(SKCanvas c)
    {
        if (_title == null) return;
        float age = (_frame - _captionFrame) / 30f;
        float a = Math.Clamp(age / 0.35f, 0, 1);
        float rise = (1 - a) * 12;

        using var measure = new SKPaint { IsAntialias = true };
        float Width(SKTypeface tf, float size, string text)
        {
            measure.Typeface = tf;
            measure.TextSize = size;
            return measure.MeasureText(text);
        }
        // shrink the text to fit the window
        float maxW = L.CaptionMaxW - 40;
        float titleSize = 36, subSize = 20;
        float tw = Width(Bold, titleSize, _title);
        if (tw > maxW)
        {
            titleSize *= maxW / tw;
            tw = Width(Bold, titleSize, _title);
        }
        float sw = _sub == null ? 0 : Width(Regular, subSize, _sub);
        if (sw > maxW)
        {
            subSize *= maxW / sw;
            sw = Width(Regular, subSize, _sub!);
        }

        using var titleFont = new SKFont(Bold, titleSize);
        using var subFont = new SKFont(Regular, subSize);
        float cx = L.CaptionCenter.X, cy = L.CaptionCenter.Y;
        float bw = Math.Max(tw, sw) + 40, bh = _sub == null ? titleSize + 30 : titleSize + subSize + 44;
        float x = cx - bw / 2, y = cy - bh / 2 + rise;
        using var bg = new SKPaint { IsAntialias = true, Color = new SKColor(12, 20, 40, (byte)(205 * a)) };
        c.DrawRoundRect(SKRect.Create(x, y, bw, bh), 20, 20, bg);
        using var ink = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha((byte)(255 * a)) };
        c.DrawText(_title, cx - tw / 2, y + 14 + titleSize * 0.95f, titleFont, ink);
        if (_sub != null)
        {
            ink.Color = new SKColor(200, 222, 255, (byte)(255 * a));
            c.DrawText(_sub, cx - sw / 2, y + 24 + titleSize + subSize * 0.95f, subFont, ink);
        }
    }
}
