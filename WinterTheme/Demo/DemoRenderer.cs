using SkiaSharp;
using WinterTheme.Native;
using WinterTheme.Render;
using WinterTheme.Sim;

namespace WinterTheme.Demo;

/// <summary>
/// Renders README screenshots offscreen: the real simulation and painter on top of a mock desktop.
/// Run with: WinterTheme --render-screens &lt;dir&gt;
/// </summary>
public static partial class DemoRenderer
{
    private static float W = 1280, H = 800, Scale = 2;
    private const float MenuH = 25;
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
        Fireworks(dir);
        Snowball(dir);
        Linked(dir);
        FunChecks();
        Night(dir);
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

    private static void Fireworks(string dir)
    {
        var w = NewWorld();
        Accumulate(w, 180);
        var man = Stack(w, 640, 44, 31, 22);
        w.MakeSnowman(man, hat: 1, scarf: 0xFF16A34A);
        w.StartFireworks(20);
        Simulate(w, 4.6f);
        w.Bubbles.Clear();
        w.Say("Уууух!", () => new SKPoint(man.Top.X, man.Top.Y - man.Top.R * 2.3f), 3);

        Save(w, Path.Combine(dir, "fireworks.jpg"));
    }

    private static void Snowball(string dir)
    {
        var w = NewWorld();
        Accumulate(w, 160);
        var man = Stack(w, 300, 44, 31, 22);
        w.MakeSnowman(man, hat: 2, scarf: 0xFFEAB308);
        w.Bubbles.Clear();

        // let the Grinch steal the nose, then hit him with a thrown snowball
        w.StartGrinch();
        int guard = 0;
        while (w.Grinch.State != GrinchState.Escape && guard++ < 5000) w.Update(Dt);
        Simulate(w, 0.3f);
        float gx = w.Grinch.X, gy = w.Ground.SurfaceY(gx) - 60;
        var ball = new Ball { X = gx + 200 * w.Grinch.Dir, Y = gy - 30, R = 12, Flying = true, VX = -500 * w.Grinch.Dir, VY = -120 };
        w.Balls.Add(ball);
        guard = 0;
        while (w.Balls.Contains(ball) && guard++ < 300) w.Update(Dt);
        Simulate(w, 0.25f);
        Console.WriteLine($"snowball: gx={gx} dir={w.Grinch.Dir} grinch hit={w.Grinch.HitT < 1}, nose back={man.Man!.HasNose}");

        Save(w, Path.Combine(dir, "snowball.jpg"));
    }

    /// <summary>Two displays side by side: Santa and the tractor must cross from one to the other.</summary>
    private static void Linked(string dir)
    {
        var a = new World(new Cocoa.ScreenInfo(0, new CGRect(0, 0, W, H), new CGRect(0, 0, W, H), MenuH, 0));
        var b = new World(new Cocoa.ScreenInfo(1, new CGRect(W, 0, W, H), new CGRect(W, 0, W, H), MenuH, 0));
        a.RightNeighbor = b;
        b.LeftNeighbor = a;
        foreach (var w in new[] { a, b })
        {
            w.UpdateWindows(new List<Quartz.WinInfo>());
            for (int i = 0; i < w.Ground.N; i++) w.Ground.H[i] = 30 + 8 * MathF.Sin(i * 0.05f);
        }

        void Step() { a.Update(Dt); b.Update(Dt); }

        // tractor: starts on the left display going right, must clean both
        a.StartTractor();
        a.Tractor.Dir = 1;
        a.Tractor.X = -170;
        bool bGotTractor = false;
        for (int k = 0; k < 30 * 60 && (a.Tractor.Active || b.Tractor.Active); k++)
        {
            Step();
            bGotTractor |= b.Tractor.Active;
        }
        Console.WriteLine($"linked tractor: reached B={bGotTractor}, A avg={a.Ground.Average:F1}, B avg={b.Ground.Average:F1}");

        // santa: called on B, must start on A's far side and fly through both, one present in total
        for (int tries = 0; tries < 20; tries++)
        {
            b.StartSanta();
            Step();
            if (a.Santa.Active && a.Santa.Dir > 0) break;
            a.Santa.Active = b.Santa.Active = false;
        }
        bool saved = false;
        bool aSeen = a.Santa.Active, bSeen = false;
        for (int k = 0; k < 30 * 40 && (a.Santa.Active || b.Santa.Active); k++)
        {
            Step();
            bSeen |= b.Santa.Active;
            if (!saved && a.Santa.Active && b.Santa.Active && b.Santa.X > 60)
            {
                saved = true;
                a.Bubbles.Clear();
                b.Bubbles.Clear();
                SaveWide(a, b, Path.Combine(dir, "two-displays.jpg"));
                Console.WriteLine($"linked santa: A.X={a.Santa.X:F0} B.X={b.Santa.X:F0} (A.X - W = {a.Santa.X - W:F0})");
            }
        }
        Console.WriteLine($"linked santa: on A={aSeen}, on B={bSeen}, presents A={a.Presents.Count} B={b.Presents.Count}");

        // drag a whole snowman from A to B, then throw a snowball from A to B
        a.Bubbles.Clear();
        var root = new Ball { X = 1180, R = 40 };
        root.Y = a.Ground.SurfaceY(root.X) - 34;
        var mid = new Ball { X = 1180, R = 28, Below = root };
        var head = new Ball { X = 1180, R = 20, Below = mid };
        root.Above = mid;
        mid.Above = head;
        a.Balls.AddRange(new[] { root, mid, head });
        a.MakeSnowman(root, hat: 1, scarf: 0xFFDC2626);
        a.Held = root;
        root.Held = true;
        a.MouseDown = b.MouseDown = true;
        for (float gx = 1180; gx < 1500; gx += 8)
        {
            a.Mx = gx; b.Mx = gx - W;
            a.My = b.My = root.Y;
            Step();
        }
        a.MouseDown = b.MouseDown = false;
        Step();
        Step();
        Console.WriteLine($"linked drag: A balls={a.Balls.Count}, B balls={b.Balls.Count}, B snowman={b.Balls.Any(x => x.Man != null)}, B held={b.Held != null}, X={root.X:F0}");

        var ball = new Ball { X = 1200, Y = 300, R = 10, Flying = true, VX = 700, VY = -100 };
        a.Balls.Add(ball);
        bool reachedB = false;
        for (int k = 0; k < 60; k++)
        {
            Step();
            reachedB |= b.Balls.Contains(ball);
        }
        Console.WriteLine($"linked throw: reached B={reachedB}, still flying={a.Balls.Contains(ball) || b.Balls.Contains(ball)}");
    }

    private static void SaveWide(World a, World b, string path)
    {
        var info = new SKImageInfo((int)(W * 2), (int)H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var c = surface.Canvas;
        foreach (var (w, x) in new[] { (a, 0f), (b, W) })
        {
            c.Save();
            c.Translate(x, 0);
            c.ClipRect(new SKRect(0, 0, W, H));
            DrawDesktop(c);
            c.SaveLayer();
            new Painter().Draw(c, w);
            c.Restore();
            c.Restore();
        }
        using var p = new SKPaint { Color = new SKColor(10, 10, 14), StrokeWidth = 6 };
        c.DrawLine(W, 0, W, H, p);
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, 88);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine($"wrote {path}");
    }

    private static void Check(string name, bool ok, string details = "") =>
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name} {details}");

    /// <summary>Headless checks of the extra physics and easter eggs (printed, not asserted).</summary>
    private static void FunChecks()
    {
        var s = Settings.Current;

        // a loose ball rolls down a slope
        {
            var w = NewWorld(false);
            for (int i = 0; i < w.Ground.N; i++) w.Ground.H[i] = Math.Max(2, 90 - i * 0.6f);
            var b = new Ball { X = 60, R = 14 };
            b.Y = w.Ground.SurfaceY(b.X) - b.R * 0.85f;
            w.Balls.Add(b);
            Simulate(w, 3);
            Check("rolling", b.X > 120, $"x 60 -> {b.X:F0}, r 14 -> {b.R:F1}");
        }

        // wind takes a hat, a click brings it back
        {
            var w = NewWorld(false);
            for (int i = 0; i < w.Ground.N; i++) w.Ground.H[i] = 20;
            var man = Stack(w, 600, 40, 28, 20);
            w.MakeSnowman(man, hat: 0, scarf: 0xFFDC2626);
            w.KnockHat(man, 150, -200);
            Simulate(w, 2.5f);
            var hat = w.Hats.FirstOrDefault();
            bool landed = hat is { Landed: true };
            if (hat != null) w.ReturnHat(hat);
            Simulate(w, 1.2f);
            Check("hat knocked, landed, returned", landed && !man.Man!.HatOff && w.Hats.Count == 0, $"landed={landed} hatOff={man.Man!.HatOff}");

            w.ForceGust(85);
            int blown = 0;
            for (int k = 0; k < 30 * 80 && blown == 0; k++)
            {
                w.Update(Dt);
                if (man.Man.HatOff) blown++;
            }
            Check("gust blows the hat off", blown > 0);
        }

        // five taps: a dance; three snowmen: a chorus
        {
            var w = NewWorld(false);
            for (int i = 0; i < w.Ground.N; i++) w.Ground.H[i] = 20;
            var a = Stack(w, 300, 40, 28, 20);
            var b = Stack(w, 600, 40, 28, 20);
            var c = Stack(w, 900, 40, 28, 20);
            foreach (var r in new[] { a, b, c }) w.MakeSnowman(r, hat: 1, scarf: 0xFF2563EB);
            for (int k = 0; k < 5; k++) w.OnTap(b);
            var lines = new HashSet<string>();
            float y0 = b.Y, minY = b.Y;
            for (int k = 0; k < 30 * 9; k++)
            {
                w.Update(Dt);
                foreach (var bb in w.Bubbles) lines.Add(bb.Text);
                minY = Math.Min(minY, b.Y);
            }
            Check("chorus", lines.Contains("Jingle all the way!") && y0 - minY > 3, $"lines={lines.Count} jump={y0 - minY:F1}");
        }

        // three snowballs reform the Grinch, he gives a present
        {
            var w = NewWorld(false);
            w.StartGrinch();
            Simulate(w, 2);
            int before = w.Presents.Count;
            for (int k = 0; k < 3; k++) w.HitGrinch();
            Simulate(w, 3);
            Check("grinch reformed", w.Grinch.Reformed && w.Presents.Count == before + 1, $"presents {before} -> {w.Presents.Count}");
        }

        // snowmen throw snowballs at a passing Grinch
        {
            var w = NewWorld(false);
            for (int i = 0; i < w.Ground.N; i++) w.Ground.H[i] = 15;
            foreach (var x in new[] { 250f, 640f, 1030f }) w.MakeSnowman(Stack(w, x, 40, 28, 20), hat: 2, scarf: 0xFF16A34A);
            w.StartGrinch();
            int thrown = 0, hits = 0;
            float lastHit = w.Grinch.HitT;
            for (int k = 0; k < 30 * 12 && w.Grinch.Active; k++)
            {
                int before = w.Balls.Count(bb => bb.Flying);
                w.Update(Dt);
                thrown += Math.Max(0, w.Balls.Count(bb => bb.Flying) - before);
                if (w.Grinch.HitT < lastHit) hits++;
                lastHit = w.Grinch.HitT;
            }
            Check("snowmen defend", thrown > 0, $"thrown={thrown} hits={hits}");
        }

        // icicles grow under snowy window caps and fall when the window moves
        {
            var w = NewWorld();
            foreach (var p in w.PileOrder)
                for (int i = 0; i < p.N; i++) p.H[i] = 8;
            Simulate(w, 150);
            int icicles = w.PileOrder.Sum(p => p.Icicles.Count);
            float longest = w.PileOrder.SelectMany(p => p.Icicles).Select(i => i.Len).DefaultIfEmpty(0).Max();
            w.UpdateWindows(Windows.Select(m => new Quartz.WinInfo(m.Id, new CGRect(m.Rect.Left + 40, m.Rect.Top, m.Rect.Width, m.Rect.Height))).ToList());
            int shards = w.Particles.Count(p => p.Kind == PKind.Shard);
            Check("icicles", icicles > 0 && shards >= icicles, $"icicles={icicles} longest={longest:F1} shards={shards}");
        }

        // shaking a window is an earthquake
        {
            var w = NewWorld();
            var man = Stack(w, 400, 40, 28, 20);
            w.MakeSnowman(man, hat: 3, scarf: 0xFFDC2626);
            w.Bubbles.Clear();
            for (int k = 0; k < 12; k++)
            {
                float dx = k % 2 == 0 ? 40 : -40;
                w.UpdateWindows(Windows.Select(m => new Quartz.WinInfo(m.Id, new CGRect(m.Rect.Left + dx, m.Rect.Top, m.Rect.Width, m.Rect.Height))).ToList());
                for (int f = 0; f < 3; f++) w.Update(Dt);
            }
            Check("window shake quake", w.Bubbles.Any(), string.Join(", ", w.Bubbles.Select(b => b.Text)));
        }

        // bullfinches land and fly off from the cursor
        {
            var w = NewWorld();
            Accumulate(w, 60);
            var man = Stack(w, 400, 40, 28, 20);
            w.MakeSnowman(man, hat: 0, scarf: 0xFFDC2626);
            w.SpawnFlock();
            w.SpawnFlock();
            int n = w.Birds.Count;
            Simulate(w, 10);
            int perched = w.Birds.Count(b => b.State == BirdState.Perch);
            var first = w.Birds.FirstOrDefault(b => b.State == BirdState.Perch);
            if (first != null)
            {
                w.MouseInside = true;
                w.Mx = first.X + 20;
                w.My = first.Y;
                w.Update(Dt);
            }
            Check("birds", perched > 0 && first?.State == BirdState.Leave, $"spawned={n} perched={perched}");
            w.MouseInside = false;
        }

        // a snowman put on a window rides with it and falls when the window goes away
        {
            var w = NewWorld();
            var term = Windows.First(m => m.Id == 2);
            float x = term.Rect.MidX - 120;
            var man = Stack(w, x, 22, 16, 11);
            w.MakeSnowman(man, hat: 0, scarf: 0xFFDC2626);
            man.Y = term.Rect.Top - 120;
            Simulate(w, 1.5f);
            bool onCap = man.SupportId == 2 && MathF.Abs(man.Y + man.R * 0.85f - term.Rect.Top) < 16;
            w.UpdateWindows(Windows.Select(m => new Quartz.WinInfo(m.Id, new CGRect(m.Rect.Left + (m.Id == 2 ? 150 : 0), m.Rect.Top - (m.Id == 2 ? 40 : 0), m.Rect.Width, m.Rect.Height))).ToList());
            Simulate(w, 0.5f);
            bool rode = man.SupportId == 2 && MathF.Abs(man.X - (x + 150)) < 1 && man.Y + man.R < term.Rect.Top - 30;
            w.UpdateWindows(Windows.Where(m => m.Id != 2).Select(m => new Quartz.WinInfo(m.Id, new CGRect(m.Rect.Left, m.Rect.Top, m.Rect.Width, m.Rect.Height))).ToList());
            Simulate(w, 2);
            bool broke = !w.Balls.Contains(man);
            Check("snowman on a window", onCap && rode && broke, $"onCap={onCap} rode={rode} broke={broke}");

            // a sharp jerk of the window throws a snowman off
            var w2 = NewWorld();
            var man2 = Stack(w2, x, 22, 16, 11);
            w2.MakeSnowman(man2, hat: 1, scarf: 0xFF2563EB);
            man2.Y = term.Rect.Top - 60;
            Simulate(w2, 1.5f);
            bool on2 = man2.SupportId == 2;
            foreach (var dx in new[] { 40f, 80f, 40f })
            {
                w2.UpdateWindows(Windows.Select(m => new Quartz.WinInfo(m.Id, new CGRect(m.Rect.Left + (m.Id == 2 ? dx : 0), m.Rect.Top, m.Rect.Width, m.Rect.Height))).ToList());
                for (int f = 0; f < 3; f++) w2.Update(Dt);
            }
            bool off = man2.SupportId == -1;
            Simulate(w2, 2);
            Check("jerk shakes it off and it breaks", on2 && off && !w2.Balls.Contains(man2), $"on={on2} off={off} broken={!w2.Balls.Contains(man2)}");

            // a gentle drop survives
            var w3 = NewWorld(false);
            var ball = new Ball { X = 500, R = 15, Y = w3.Ground.SurfaceY(500) - 15 * 0.85f - 100 };
            w3.Balls.Add(ball);
            Simulate(w3, 1.5f);
            Check("short drop survives", w3.Balls.Contains(ball));
        }

        // giant snowman wears a crown; Santa gives coal to the greedy
        {
            var w = NewWorld(false);
            var king = Stack(w, 600, 70, 48, 32);
            w.MakeSnowman(king);
            w.StartSanta();
            for (int k = 0; k < 5; k++)
            {
                w.SantaGift();
                Simulate(w, 2.1f);
            }
            Check("crown + coal", king.Man!.Hat == 4 && w.Presents.Any(p => p.Coal), $"hat={king.Man.Hat} presents={w.Presents.Count} coal={w.Presents.Count(p => p.Coal)}");
        }

        // melting shrinks snowmen until they collapse
        {
            var w = NewWorld(false);
            var man = Stack(w, 600, 14, 9, 6);
            w.MakeSnowman(man, hat: 2, scarf: 0xFFDC2626);
            s.Melt = true;
            Simulate(w, 70);
            s.Melt = false;
            Check("melting", !w.Balls.Contains(man), $"balls left={w.Balls.Count}");
        }
    }

    /// <summary>Night scene: aurora, bullfinches, icicles, a king and a dancing snowman.</summary>
    private static void Night(string dir)
    {
        var w = NewWorld();
        Accumulate(w, 200);
        foreach (var p in w.PileOrder)
            for (int i = 0; i < p.N; i++) p.H[i] = Math.Max(p.H[i], 7);
        Simulate(w, 160);

        var king = Stack(w, 980, 70, 48, 32);
        w.MakeSnowman(king);
        var dancer = Stack(w, 700, 38, 27, 19);
        w.MakeSnowman(dancer, hat: 2, scarf: 0xFF16A34A);
        var hatless = Stack(w, 330, 36, 25, 18);
        w.MakeSnowman(hatless, hat: 0, scarf: 0xFF2563EB);
        var term = Windows.First(m => m.Id == 2);
        var rider = Stack(w, term.Rect.Left + 330, 20, 14, 10);
        w.MakeSnowman(rider, hat: 3, scarf: 0xFFEAB308);
        rider.Y = term.Rect.Top - 80;
        Simulate(w, 1);
        w.KnockHat(hatless, -60, -150);
        w.StartAurora(60);
        w.SpawnFlock();
        w.SpawnFlock();
        Simulate(w, 9);
        for (int k = 0; k < 5; k++) w.OnTap(dancer);
        Simulate(w, 1.1f);
        w.Bubbles.Clear();
        w.Say("Jingle bells!", () => new SKPoint(dancer.Top.X, dancer.Top.Y - dancer.Top.R * 2.3f), 3);
        w.Say("Склонитесь!", () => new SKPoint(king.Top.X, king.Top.Y - king.Top.R * 2.6f), 3);

        Save(w, Path.Combine(dir, "night.jpg"));
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
            // proportions of the original 1280x800 layout, so other sizes (the promo video) look the same
            hills.MoveTo(0, H * 0.8f);
            hills.QuadTo(W * 0.203f, H * 0.7f, W * 0.406f, H * 0.7875f);
            hills.QuadTo(W * 0.64f, H * 0.875f, W * 0.82f, H * 0.75f);
            hills.QuadTo(W * 0.922f, H * 0.7f, W, H * 0.7625f);
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
        if (W >= 800)
        {
            c.DrawText("Mon 22 Dec  18:42", W - 135, 17, thin, p);
            DrawFlakeIcon(c, W - 160, 12.5f);
        }
        else
        {
            c.DrawText("18:42", W - 50, 17, thin, p);
            DrawFlakeIcon(c, W - 75, 12.5f);
        }

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
