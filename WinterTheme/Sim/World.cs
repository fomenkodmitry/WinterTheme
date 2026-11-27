using System.Collections.Concurrent;
using SkiaSharp;
using WinterTheme.Native;

namespace WinterTheme.Sim;

/// <summary>Whole winter scene for one display. Coordinates are local points, top-left origin, y down.</summary>
public sealed partial class World
{
    public readonly Cocoa.ScreenInfo Screen;
    public readonly float W, H, MenuBarH;
    public readonly Random Rng = new();

    public float Time;
    public float Wind;
    private float _gust, _gustTarget, _gustT = 30;

    public readonly List<Flake> Flakes = new();
    public readonly List<Particle> Particles = new();
    public readonly Pile Ground;
    public readonly Dictionary<int, WindowPile> WinPiles = new();
    public readonly List<WindowPile> PileOrder = new();
    public readonly List<Ball> Balls = new();
    public readonly List<Present> Presents = new();
    public readonly List<Bubble> Bubbles = new();
    public readonly Tractor Tractor = new();
    public readonly Grinch Grinch = new();
    public readonly Santa Santa = new();

    // input (local coordinates)
    public float Mx = -1000, My = -1000;
    private float _pmx, _pmy;
    public bool MouseInside, MouseDown, OptionDown;
    private bool _prevDown;
    public Ball? Held;
    private float _holdOffX, _holdOffY;
    public bool HoverInteractive;
    public bool HoverBall;
    public bool ClickReleased;

    private float _tractorCheck = 5, _nextGrinch, _nextSanta;
    private bool _initialFill = true;

    private float _nextChat = 35, _nextStar = 45, _flurryT;
    private float _showT, _showNext;
    private float _countdown = -1, _clockCheck;
    private int _lastCount, _countdownYear, _celebratedYear, _leetDay = -1, _oldNewYear;
    private float _throwVX, _throwVY;

    private static readonly Settings S = Settings.Current;
    private const float MaxBallR = 76;

    /// <summary>Landing faster than this (a fall of about 160 points) breaks a ball or a snowman.</summary>
    private const float BreakSpeed = 560;

    public World(Cocoa.ScreenInfo screen)
    {
        Screen = screen;
        W = (float)screen.Cg.W;
        H = (float)screen.Cg.H;
        MenuBarH = screen.MenuBarH;
        Ground = new Pile(0, W, H, 170, 2.6f, false);
        _nextGrinch = Rand(120, 260);
        _nextSanta = Rand(60, 160);
    }

    public float Rand(float a, float b) => a + (float)Rng.NextDouble() * (b - a);

    // ------------------------------------------------------------------ input

    public void PollInput()
    {
        var p = Quartz.MouseLocation();
        _pmx = Mx;
        _pmy = My;
        Mx = (float)(p.X - Screen.Cg.X);
        My = (float)(p.Y - Screen.Cg.Y);
        MouseInside = Mx >= 0 && My >= 0 && Mx < W && My < H;
        _prevDown = MouseDown;
        MouseDown = Quartz.LeftButtonDown();
        OptionDown = Quartz.OptionDown();
    }

    // ------------------------------------------------------------------ main update

    public void Update(float dt)
    {
        while (_inbox.TryDequeue(out var msg)) msg(this);
        Time += dt;
        UpdateWind(dt);
        UpdateFlakes(dt);
        UpdateParticles(dt);
        RelaxPiles();
        UpdateMouse(dt);
        UpdateBalls(dt);
        UpdatePresents(dt);
        UpdateTractor(dt);
        UpdateGrinch(dt);
        UpdateSanta(dt);
        UpdateEvents(dt);
        UpdateShow(dt);
        UpdateClock(dt);
        UpdateFun(dt);

        for (int i = Bubbles.Count - 1; i >= 0; i--)
        {
            Bubbles[i].Life -= dt;
            if (Bubbles[i].Life <= 0) Bubbles.RemoveAt(i);
        }

        if (S.Melt)
        {
            Ground.Melt(dt * 0.035f);
            foreach (var p in PileOrder) p.Melt(dt * 0.02f);
            MeltSnowmen(dt);
        }
    }

    private void UpdateWind(float dt)
    {
        _gustT -= dt;
        if (_gustT <= 0)
        {
            _gustTarget = _gustTarget == 0 ? Rand(-70, 70) : 0;
            _gustT = _gustTarget == 0 ? Rand(25, 60) : Rand(3, 7);
        }
        _gust += (_gustTarget - _gust) * Math.Min(1, dt * 0.8f);
        Wind = 14 * MathF.Sin(Time * 0.05f) + 7 * MathF.Sin(Time * 0.17f + 1.3f) + _gust;
    }

    // ------------------------------------------------------------------ flakes

    private int TargetFlakes => S.Paused ? 0 : (int)(320 * S.FlakeFactor * (_flurryT > 0 ? 3 : 1) * (W * H) / (1512f * 982f));

    private void Respawn(Flake f, bool anywhere)
    {
        f.Depth = (float)Rng.NextDouble();
        f.Size = f.Depth < 0.4f ? Rand(0.6f, 1.6f) : Rand(1.2f, 3.8f);
        f.X = Rand(-40, W + 40);
        f.Y = anywhere ? Rand(-10, H) : Rand(-30, -5);
        f.Phase = Rand(0, MathF.Tau);
        f.Freq = Rand(0.4f, 1.4f);
        f.Amp = Rand(6, 26);
        f.Spin = Rand(0, MathF.Tau);
    }

    private void UpdateFlakes(float dt)
    {
        int target = TargetFlakes;
        if (_initialFill)
        {
            _initialFill = false;
            for (int i = 0; i < target; i++)
            {
                var f = new Flake();
                Respawn(f, true);
                Flakes.Add(f);
            }
        }
        int spawn = Math.Min(Math.Max(0, target - Flakes.Count), 6 + target / 40);
        for (int i = 0; i < spawn; i++)
        {
            var f = new Flake();
            Respawn(f, false);
            Flakes.Add(f);
        }

        float speedMul = S.Intensity == 2 ? 1.35f : 1f;
        for (int i = Flakes.Count - 1; i >= 0; i--)
        {
            var f = Flakes[i];
            float fall = (20 + f.Depth * 38 + f.Size * 7) * speedMul;
            float sway = MathF.Sin(Time * f.Freq + f.Phase) * f.Amp;
            f.X += (Wind * (0.35f + f.Depth) + sway) * dt;
            f.Spin += dt * (f.Freq - 0.9f);
            float ny = f.Y + fall * dt;

            bool done = false;
            if (f.Depth >= 0.4f)
                done = TryLand(f.X, f.Y, ny, f.Size * 0.55f, true);
            else if (ny > H + 6)
                done = true;

            if (done)
            {
                if (Flakes.Count > target) { Flakes[i] = Flakes[^1]; Flakes.RemoveAt(Flakes.Count - 1); }
                else Respawn(f, false);
                continue;
            }

            f.Y = ny;
            if (f.X < -50) f.X += W + 100;
            else if (f.X > W + 50) f.X -= W + 100;
        }
    }

    /// <summary>Checks whether something falling from y0 to y1 at x hits snow; deposits and returns true.</summary>
    private bool TryLand(float x, float y0, float y1, float amount, bool absorbOnBalls)
    {
        if (x < 0 || x >= W) return y1 > H + 6;

        if (S.SnowOnWindows)
        {
            foreach (var p in PileOrder)
            {
                if (!p.Contains(x)) continue;
                float s = p.SurfaceY(x);
                if (y0 <= s && y1 >= s && p.VisibleAt(x))
                {
                    // snow falls in front of windows too: only part of it settles, and a full cap lets it pass
                    if (p.HeightAt(x) >= p.CapAt(x) - 0.4f) continue;
                    if (absorbOnBalls && Rng.NextDouble() > 0.45) continue;
                    p.Add(x, amount);
                    return true;
                }
            }
        }

        if (absorbOnBalls)
        {
            foreach (var b in Balls)
            {
                float dx = x - b.X;
                if (MathF.Abs(dx) > b.R * 0.8f) continue;
                float top = b.Y - MathF.Sqrt(Math.Max(0, b.R * b.R - dx * dx));
                if (y0 <= top && y1 >= top) return true;
            }
        }

        float g = Ground.SurfaceY(x);
        if (y1 >= g)
        {
            Ground.Add(x, amount);
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ particles

    public void Spawn(PKind kind, float x, float y, float vx, float vy, float size, float life, SKColor color, float amount = 0)
    {
        if (Particles.Count > 3500) return;
        Particles.Add(new Particle
        {
            Kind = kind, X = x, Y = y, VX = vx, VY = vy, Size = size, Life = life, MaxLife = life,
            Color = color, Amount = amount, Rot = Rand(0, MathF.Tau),
        });
    }

    public void Powder(float x, float y, int count, float spread = 60, float up = 70)
    {
        for (int i = 0; i < count; i++)
            Spawn(PKind.Powder, x + Rand(-3, 3), y + Rand(-2, 2), Rand(-spread, spread), Rand(-up, -up * 0.2f),
                Rand(0.8f, 2.2f), Rand(0.5f, 1.2f), SKColors.White);
    }

    public void Sparkles(float x, float y, int count, float spread = 80)
    {
        SKColor[] cols = { new(255, 240, 160), new(200, 230, 255), SKColors.White, new(255, 200, 220) };
        for (int i = 0; i < count; i++)
            Spawn(PKind.Sparkle, x, y, Rand(-spread, spread), Rand(-spread, spread * 0.4f),
                Rand(1.5f, 3.5f), Rand(0.6f, 1.4f), cols[Rng.Next(cols.Length)]);
    }

    public void Confetti(float x, float y, int count)
    {
        SKColor[] cols = { new(239, 68, 68), new(250, 204, 21), new(34, 197, 94), new(59, 130, 246), new(236, 72, 153) };
        for (int i = 0; i < count; i++)
            Spawn(PKind.Confetti, x, y, Rand(-160, 160), Rand(-320, -120), Rand(2, 4), Rand(1.2f, 2.2f), cols[Rng.Next(cols.Length)]);
    }

    private void Clump(float x, float y, float amount, float vx, float vy)
    {
        Spawn(PKind.Clump, x, y, vx, vy, Math.Clamp(MathF.Sqrt(amount) * 1.6f, 1.5f, 6.5f), 8, SKColors.White, amount);
    }

    private void UpdateParticles(float dt)
    {
        for (int i = Particles.Count - 1; i >= 0; i--)
        {
            var p = Particles[i];
            p.Life -= dt;
            bool dead = p.Life <= 0;
            switch (p.Kind)
            {
                case PKind.Clump:
                {
                    p.VY += 650 * dt;
                    p.VX *= 1 - 0.6f * dt;
                    float ny = p.Y + p.VY * dt;
                    if (TryLand(p.X + p.VX * dt, p.Y, ny, p.Amount * 0.8f, false))
                    {
                        Powder(p.X, p.Y, 2, 40, 40);
                        dead = true;
                    }
                    break;
                }
                case PKind.Powder:
                    p.VY += 110 * dt;
                    p.VX += (Wind * 0.6f - p.VX) * Math.Min(1, 1.4f * dt);
                    break;
                case PKind.Sparkle:
                    p.VY += 25 * dt;
                    p.VX *= 1 - 1.5f * dt;
                    p.VY *= 1 - 1.0f * dt;
                    break;
                case PKind.Smoke:
                    p.VY -= 6 * dt;
                    p.VX += Wind * 0.25f * dt;
                    p.Size += 9 * dt;
                    break;
                case PKind.Confetti:
                    p.VY += 260 * dt;
                    p.VX *= 1 - 1.2f * dt;
                    p.VY = Math.Min(p.VY, 90);
                    p.Rot += dt * 8;
                    break;
                case PKind.Rocket:
                    p.VY += 450 * dt;
                    p.VX *= 1 - 0.3f * dt;
                    if (Rng.NextDouble() < 0.8)
                        Spawn(PKind.Sparkle, p.X, p.Y, Rand(-15, 15), Rand(10, 40), Rand(0.8f, 1.5f), Rand(0.25f, 0.5f), new SKColor(255, 220, 150));
                    if (p.VY > -30 || dead)
                    {
                        Burst(p.X, p.Y, p.Color);
                        dead = true;
                    }
                    break;
                case PKind.Note:
                case PKind.Heart:
                    p.VX *= 1 - 0.8f * dt;
                    p.VY *= 1 - 0.4f * dt;
                    break;
                case PKind.Drop:
                    p.VY += 600 * dt;
                    if (p.Y >= Ground.SurfaceY(Math.Clamp(p.X, 0, W - 1))) dead = true;
                    break;
                case PKind.Shard:
                    p.VY += 900 * dt;
                    p.Rot += dt * 6;
                    if (p.Y >= Ground.SurfaceY(Math.Clamp(p.X, 0, W - 1)))
                    {
                        dead = true;
                        SKColor[] ice = { new(220, 240, 255), SKColors.White };
                        for (int k = 0; k < 6; k++)
                            Spawn(PKind.Sparkle, p.X, p.Y - 2, Rand(-60, 60), Rand(-90, -20), Rand(1, 2), Rand(0.3f, 0.7f), ice[k % 2]);
                    }
                    break;
                case PKind.Star:
                    if (Rng.NextDouble() < 0.6)
                        Spawn(PKind.Sparkle, p.X, p.Y, p.VX * 0.05f + Rand(-10, 10), p.VY * 0.05f + Rand(-10, 10),
                            Rand(0.8f, 1.6f), Rand(0.3f, 0.7f), new SKColor(220, 235, 255));
                    break;
            }

            p.X += p.VX * dt;
            p.Y += p.VY * dt;
            if (p.Y > H + 20) dead = true;
            if (dead)
            {
                Particles[i] = Particles[^1];
                Particles.RemoveAt(Particles.Count - 1);
            }
        }
    }

    // ------------------------------------------------------------------ piles & windows

    private bool _relaxDir;

    private void RelaxPiles()
    {
        _relaxDir = !_relaxDir;
        Ground.Relax(_relaxDir);
        Ground.Relax(!_relaxDir);
        foreach (var p in PileOrder)
        {
            var (l, r) = p.Relax(_relaxDir);
            p.SpillL += l;
            p.SpillR += r;
            if (p.SpillL > 1.4f)
            {
                Clump(p.Left - 1, p.BaseY - p.H[0] - 1, p.SpillL, Rand(-30, -8), Rand(-10, 5));
                p.SpillL = 0;
            }
            if (p.SpillR > 1.4f)
            {
                Clump(p.Right + 1, p.BaseY - p.H[^1] - 1, p.SpillR, Rand(8, 30), Rand(-10, 5));
                p.SpillR = 0;
            }
        }
    }

    private void Shed(Pile p, float vxBias = 0)
    {
        if (p is WindowPile wp) DropIcicles(wp, vxBias);
        int step = Math.Max(3, p.N / 70);
        for (int i = 0; i < p.N; i += step)
        {
            float amt = 0, hMax = 0;
            for (int k = i; k < Math.Min(p.N, i + step); k++)
            {
                amt += p.H[k];
                hMax = Math.Max(hMax, p.H[k]);
            }
            if (amt < 0.4f) continue;
            float x = p.BinCenter(Math.Min(p.N - 1, i + step / 2));
            Clump(x, p.BaseY - hMax * 0.5f, amt, Rand(-35, 35) + vxBias, Rand(-60, 0));
        }
        Array.Clear(p.H);
    }

    public void UpdateWindows(List<Quartz.WinInfo> wins)
    {
        var rects = new List<(int id, float x, float y, float w, float h)>(wins.Count);
        foreach (var wi in wins)
        {
            float x = (float)(wi.Bounds.X - Screen.Cg.X), y = (float)(wi.Bounds.Y - Screen.Cg.Y);
            float w = (float)wi.Bounds.W, h = (float)wi.Bounds.H;
            if (x + w < 0 || x > W || y > H || y + h < 0) continue;
            rects.Add((wi.Id, x, y, w, h));
        }

        TrackShake(rects);

        var seen = new HashSet<int>();
        PileOrder.Clear();
        for (int idx = 0; idx < rects.Count; idx++)
        {
            var (id, x, y, w, h) = rects[idx];
            bool eligible = S.SnowOnWindows && y > MenuBarH + 6 && y < H - 60 && w >= 80 && h >= 40;
            if (WinPiles.TryGetValue(id, out var pile))
            {
                bool moved = MathF.Abs(pile.Bx - x) > 0.5f || MathF.Abs(pile.By - y) > 0.5f || MathF.Abs(pile.Bw - w) > 0.5f;
                if (!eligible)
                {
                    Shed(pile);
                    WinPiles.Remove(id);
                    continue;
                }
                if (moved)
                {
                    Shed(pile, (x - pile.Bx) * 4);
                    pile = new WindowPile(id, x, y, w, h);
                    WinPiles[id] = pile;
                    foreach (var r in Balls)
                        if (r.Below == null && r.SupportId == id && r.Man != null && Rng.NextDouble() < 0.08)
                            SayMan(r, Pick(RideLines), 1.5f);
                }
            }
            else if (eligible)
            {
                pile = new WindowPile(id, x, y, w, h);
                WinPiles[id] = pile;
            }
            else continue;

            seen.Add(id);
            PileOrder.Add(pile);

            for (int i = 0; i < pile.N; i++)
            {
                float cx = pile.BinCenter(i);
                bool vis = cx >= 0 && cx < W;
                for (int j = 0; j < idx && vis; j++)
                {
                    var r = rects[j];
                    if (cx >= r.x && cx <= r.x + r.w && y - 3 >= r.y - 16 && y + 2 <= r.y + r.h + 16)
                        vis = false;
                }
                pile.Visible[i] = vis;
            }
        }

        if (WinPiles.Count != seen.Count)
        {
            foreach (var id in WinPiles.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                Shed(WinPiles[id]);
                WinPiles.Remove(id);
            }
        }
    }

    public void ClearAll()
    {
        for (int i = 0; i < Ground.N; i += 6)
            if (Ground.H[i] > 2) Powder(Ground.BinCenter(i), H - Ground.H[i], 2, 80, 140);
        Array.Clear(Ground.H);
        foreach (var p in PileOrder) Shed(p);
    }

    // ------------------------------------------------------------------ mouse / snowman building

    private Ball? HitBall(float x, float y, float pad)
    {
        Ball? best = null;
        foreach (var b in Balls)
        {
            float dx = x - b.X, dy = y - b.Y;
            if (dx * dx + dy * dy <= (b.R + pad) * (b.R + pad))
                if (best == null || b.Y < best.Y) best = b;
        }
        return best;
    }

    private Present? HitPresent(float x, float y)
    {
        foreach (var p in Presents)
            if (MathF.Abs(x - p.X) < p.Size * 0.7f && MathF.Abs(y - p.Y) < p.Size * 0.8f)
                return p;
        return null;
    }

    private bool NearGround(float x, float y) => y > Ground.SurfaceY(x) - 34 && y < H + 2;

    private void UpdateMouse(float dt)
    {
        bool pressed = MouseDown && !_prevDown;
        bool released = !MouseDown && _prevDown;

        var hover = MouseInside ? HitBall(Mx, My, 5) : null;
        var hoverPresent = MouseInside ? HitPresent(Mx, My) : null;
        bool hoverSanta = MouseInside && HitSanta(Mx, My);
        var hoverHat = MouseInside && hover == null ? HitHat(Mx, My) : null;
        HoverBall = hover != null || hoverPresent != null || hoverSanta || hoverHat != null;

        bool interactiveBefore = HoverInteractive;
        HoverInteractive = Held != null ||
                           (MouseInside && (HoverBall || (OptionDown && NearGround(Mx, My))));

        // only react to presses that our (interactive) window actually swallowed
        if (pressed && interactiveBefore && MouseInside && Held == null)
        {
            if (hoverHat != null && hoverPresent == null) ReturnHat(hoverHat);
            else OnPress(hover, hoverPresent, hoverSanta);
        }

        if (Held != null)
        {
            if (MouseDown) DragHeld(dt);
            if (Held != null && (released || !MouseDown)) Release();
        }

        if (released && interactiveBefore) ClickReleased = true;

        if (S.CursorPush && Held == null && MouseInside)
        {
            float vx = (Mx - _pmx) / Math.Max(dt, 0.001f), vy = (My - _pmy) / Math.Max(dt, 0.001f);
            if (vx * vx + vy * vy > 25 * 25 && MathF.Abs(Mx - _pmx) < 300)
            {
                Push(Ground, vx);
                foreach (var p in PileOrder) Push(p, vx);
                if (S.Icicles) PokeIcicles(vx);
            }
        }
    }

    private void OnPress(Ball? hover, Present? present, bool santa)
    {
        if (present != null)
        {
            OpenPresent(present);
            return;
        }

        if (santa && hover == null)
        {
            SantaGift();
            return;
        }

        _throwVX = _throwVY = 0;

        if (hover != null)
        {
            var root = hover.Root;
            if (root.Man != null && hover.Above == null && hover.StackCount == 3 && !root.Man.HasNose)
            {
                root.Man.HasNose = true;
                Sparkles(hover.X + hover.R * 0.4f, hover.Y, 14, 60);
                Say("Спасибо!", () => new SKPoint(hover.X, hover.Y - hover.R * 2.2f));
                return;
            }

            _pressX = Mx;
            _pressY = My;
            _pressT = Time;
            _tapRoot = root.Man != null ? root : null;

            Ball grab;
            if (hover.Above == null && hover.Below != null)
            {
                // the top ball comes off the stack once the drag really starts (a short tap is a tap)
                _pendingDetach = hover;
                grab = hover;
            }
            else
            {
                _pendingDetach = null;
                grab = hover.Above != null ? root : hover;
            }

            Held = grab;
            grab.Held = true;
            grab.Flying = false;
            grab.SupportId = -1;
            _holdOffX = grab.X - Mx;
            _holdOffY = grab.Y - My;
            return;
        }

        if (OptionDown && NearGround(Mx, My))
        {
            if (Ground.HeightAt(Mx) < 2.5f)
            {
                Say("Тут мало снега", () => new SKPoint(Mx, My - 20), 1.4f);
                return;
            }
            float taken = Ground.Take(Mx - 8, Mx + 8, 3);
            float r = 5 + MathF.Sqrt(taken) * 0.6f;
            var b = new Ball { X = Mx, R = r, Held = true };
            b.Y = Ground.SurfaceY(Mx) - r * 0.85f;
            Balls.Add(b);
            Held = b;
            _holdOffX = 0;
            _holdOffY = b.Y - My;
            Powder(Mx, b.Y + r, 4, 30, 40);
        }
    }

    private void DragHeld(float dt)
    {
        if (_pendingDetach != null)
        {
            if (MathF.Abs(Mx - _pressX) + MathF.Abs(My - _pressY) < 7) return;
            var top = _pendingDetach;
            _pendingDetach = null;
            ScareBirdsOn(top.Root);
            top.Root.Man = null;
            if (top.Below != null) top.Below.Above = null;
            top.Below = null;
        }

        // dragged onto the next display: the ball (or the whole snowman) goes with the cursor
        if ((Mx >= W || Mx < 0) && Next(Mx >= W ? 1 : -1) is { } n)
        {
            HandOverHeld(n);
            return;
        }

        var b = Held!;
        // smoothed hand velocity, used when the ball is let go (a flick throws it)
        float k = Math.Min(1, dt * 14);
        _throwVX += ((Mx - _pmx) / Math.Max(dt, 0.001f) - _throwVX) * k;
        _throwVY += ((My - _pmy) / Math.Max(dt, 0.001f) - _throwVY) * k;

        float nx = Math.Clamp(Mx + _holdOffX, b.R, W - b.R);
        float ny = My + _holdOffY;

        if (b.Above == null && b.Below == null)
        {
            // rolls on the ground drift, or on a window cap when held right at its surface
            float groundY = Ground.SurfaceY(nx) - b.R * 0.85f;
            Pile? surf = ny >= groundY - b.R * 0.35f ? Ground : null;
            if (surf == null && S.SnowOnWindows)
                foreach (var p in PileOrder)
                {
                    if (!p.Contains(nx) || !p.VisibleAt(nx)) continue;
                    float py = p.SurfaceY(nx) - b.R * 0.85f;
                    if (ny >= py - b.R * 0.35f && ny <= py + b.R * 0.6f)
                    {
                        surf = p;
                        break;
                    }
                }
            if (surf != null)
            {
                // rolling along the snow: pick up snow and grow
                float dx = nx - b.X;
                b.Angle += dx / b.R;
                float per = Math.Min(MathF.Abs(dx) * 0.35f, 4f);
                if (per > 0.01f && b.R < MaxBallR)
                {
                    float taken = surf.Take(nx - b.R * 0.55f, nx + b.R * 0.55f, per);
                    float area = b.R * b.R + taken * surf.BinW * 2.2f / MathF.PI;
                    b.R = Math.Min(MaxBallR, MathF.Sqrt(area));
                    if (taken > 1.5f && Rng.NextDouble() < 0.5)
                        Powder(nx - Math.Sign(dx) * b.R * 0.8f, surf.SurfaceY(nx), 1, 30, 30);
                }
                ny = surf.SurfaceY(nx) - b.R * 0.85f;
                _holdOffY = ny - My;
            }
            b.X = nx;
            b.Y = Math.Min(ny, Ground.SurfaceY(nx) - b.R * 0.85f);
        }
        else
        {
            b.X = nx;
            b.Y = Math.Min(ny, Ground.SurfaceY(nx) - b.R * 0.85f);
        }
        b.VY = 0;
    }

    private void Release()
    {
        var b = Held!;
        b.Held = false;
        Held = null;

        bool tap = Time - _pressT < 0.35f && MathF.Abs(Mx - _pressX) + MathF.Abs(My - _pressY) < 7;
        var tapRoot = _tapRoot;
        _tapRoot = null;
        if (_pendingDetach != null)
        {
            _pendingDetach = null;
            if (tap && tapRoot != null) OnTap(tapRoot);
            return;
        }
        if (tap && tapRoot?.Man != null)
        {
            OnTap(tapRoot);
            return;
        }

        if (b.Above != null || b.Below != null) return;

        Ball? target = null;
        foreach (var t in Balls)
        {
            if (t == b || t.Flying || t.Above != null || t.StackCount >= 3) continue;
            if (MathF.Abs(b.X - t.X) < t.R * 0.9f && b.Y < t.Y && b.R <= t.R * 1.1f)
                if (target == null || t.Y < target.Y) target = t;
        }

        if (target != null)
        {
            b.Below = target;
            target.Above = b;
            Powder(b.X, b.Y + b.R, 5, 50, 40);
            if (b.StackCount == 3) MakeSnowman(b.Root);
            return;
        }

        // flicked: it becomes a snowball
        float sp = MathF.Sqrt(_throwVX * _throwVX + _throwVY * _throwVY);
        bool lifted = b.Y < Ground.SurfaceY(b.X) - b.R * 0.85f - 4 || _throwVY < -200;
        if (sp > 450 && lifted && b.R <= 34)
        {
            float m = Math.Min(1, 1600 / sp);
            b.Flying = true;
            b.VX = _throwVX * m;
            b.VY = _throwVY * m;
        }
    }

    private static readonly SKColor[] ScarfColors =
    {
        new(220, 38, 38), new(37, 99, 235), new(22, 163, 74), new(234, 179, 8), new(147, 51, 234), new(236, 72, 153),
    };

    private static readonly string[] Hellos =
    {
        "Привет!", "Ура, я живой!", "Холодно!", "С Новым годом!", "Где мой нос?.. А, вот.", "Я родился!", "Мама?",
        "Какой я красивый!", "Спасибо, что слепил!", "Я тут постою, ладно?", "Морковка на месте. Начинаем!",
    };

    public void MakeSnowman(Ball root, int hat = -1, uint scarf = 0, bool nose = true, string? hello = null, string? name = null)
    {
        bool giant = root.R >= 66, mini = root.R <= 13;
        name ??= giant ? "Снежный Король" : Pick(Names);
        if (hat < 0)
        {
            if (giant) hello ??= "Я — Снежный Король! Склонитесь!";
            else if (mini) hello ??= "Пи-пи! Я мини-снеговик!";
            else if (Rng.NextDouble() < 0.35) hello ??= $"Привет! Я {name}!";
        }
        root.Man = new Snowman
        {
            Name = name,
            Hat = hat >= 0 ? hat : giant ? 4 : Rng.Next(4),
            Scarf = scarf != 0 ? new SKColor(scarf) : ScarfColors[Rng.Next(ScarfColors.Length)],
            HatColor = ScarfColors[Rng.Next(ScarfColors.Length)],
            HasNose = nose,
            NoseLostAt = Time,
            ArmL = Rand(-2.9f, -2.3f),
            ArmR = Rand(-0.8f, -0.25f),
        };
        if (hat >= 0) return;
        var head = root.Top;
        Sparkles(head.X, head.Y, 30, 120);
        Say(hello ?? Pick(Hellos), () => new SKPoint(head.X, head.Y - head.R * 2.3f), 3);
    }

    private void Push(Pile p, float vx)
    {
        if (!p.Contains(Mx)) return;
        if (p is WindowPile && !p.VisibleAt(Mx)) return;
        float surf = p.SurfaceY(Mx);
        if (My < surf - 3 || My > p.BaseY + 1) return;

        const float r = 12;
        float removed = 0;
        int a = p.BinOf(Mx - r), bEnd = p.BinOf(Mx + r);
        for (int i = a; i <= bEnd; i++)
        {
            float dx = p.BinCenter(i) - Mx;
            if (MathF.Abs(dx) > r) continue;
            float low = My + MathF.Sqrt(r * r - dx * dx) * 0.6f;
            float maxH = Math.Max(0, p.BaseY - low);
            if (p.H[i] > maxH)
            {
                removed += p.H[i] - maxH;
                p.H[i] = maxH;
            }
        }
        if (removed < 0.05f) return;
        int dir = vx >= 0 ? 1 : -1;
        p.Add(Mx + dir * (r + 5), removed * 0.55f);
        p.Add(Mx - dir * (r + 5), removed * 0.25f);
        int n = (int)Math.Min(6, 1 + removed);
        for (int i = 0; i < n; i++)
            Spawn(PKind.Powder, Mx + Rand(-6, 6), My, dir * Rand(30, 110) + vx * 0.1f, Rand(-110, -30),
                Rand(0.8f, 2f), Rand(0.5f, 1.1f), SKColors.White);
    }

    // ------------------------------------------------------------------ balls

    private void UpdateBalls(float dt)
    {
        for (int i = Balls.Count - 1; i >= 0; i--)
            if (Balls[i].Flying && Fly(Balls[i], dt))
                Balls.RemoveAt(i);

        List<Ball>? broken = null;

        foreach (var b in Balls)
        {
            if (b.Below != null || b.Held || b.Flying) continue;
            FollowSupport(b);
            float target = RestSurface(b) - b.R * 0.85f;
            if (b.SupportId < 0 && b.Above == null && b.Man == null && b.Y >= target - 2) Roll(b, dt);
            target = RestSurface(b) - b.R * 0.85f;
            float lift = Lift(b);
            if (lift > 0)
            {
                b.Y = target - lift;
                b.VY = 0;
            }
            else if (b.Y < target - 0.5f)
            {
                float py = b.Y;
                b.VY += 1000 * dt;
                b.Y += b.VY * dt;
                if (b.VX != 0)
                {
                    // thrown off a window: keeps drifting sideways while falling
                    b.X = Math.Clamp(b.X + b.VX * dt, b.R, W - b.R);
                    b.VX *= 1 - Math.Min(1, 0.4f * dt);
                }
                if (b.SupportId < 0 && CatchOnWindow(b, py)) target = RestSurface(b) - b.R * 0.85f;
                if (b.Y >= target)
                {
                    // a fall from higher than ~1.5 window title bars... well, about 160 points, breaks it
                    if (b.VY > BreakSpeed)
                    {
                        (broken ??= new List<Ball>()).Add(b);
                        continue;
                    }
                    if (b.VY > 250) Powder(b.X, b.Y + b.R, 6, 70, 60);
                    b.Y = target;
                    b.VY = 0;
                    if (b.Above != null || b.Man != null) b.VX = 0;
                }
            }
            else
            {
                // on a window that moved up: stay on top of it, do not sink into the title bar
                b.Y = b.SupportId >= 0 && b.Y > target ? target : b.Y + (target - b.Y) * Math.Min(1, dt * 6);
                b.VY = 0;
            }
        }

        if (broken != null)
            foreach (var b in broken) Shatter(b);

        foreach (var b in Balls)
        {
            if (b.Below != null) continue;
            float sway = b.Man is { DanceT: > 0 } dm ? MathF.Sin(dm.DanceT * 7) : 0;
            for (var cur = b; cur.Above != null; cur = cur.Above)
            {
                var a = cur.Above;
                a.X = cur.X + sway * a.R * 0.15f;
                a.Y = cur.Y - cur.R * 0.8f - a.R * 0.82f;
            }

            var man = b.Man;
            if (man == null) continue;
            UpdateTricks(b, man, dt);
            man.NextBlink -= dt;
            if (man.NextBlink <= 0)
            {
                man.Blink = 0.15f;
                man.NextBlink = Rand(2.5f, 6f);
            }
            man.Blink = Math.Max(0, man.Blink - dt);
            float dxm = Mx - b.X;
            bool near = MouseInside && MathF.Abs(dxm) < 180 && My > b.Top.Y - 200;
            man.Wave += ((near ? 1 : 0) - man.Wave) * Math.Min(1, dt * 3);
            if (!man.HasNose && Time - man.NoseLostAt > 180)
            {
                man.HasNose = true;
                var head = b.Top;
                Sparkles(head.X + head.R * 0.4f, head.Y, 10, 40);
            }
        }
    }

    // ------------------------------------------------------------------ presents

    private static readonly string[] PresentLines =
    {
        "Хо-хо-хо!", "Мандаринка!", "Носки! Тёплые!", "С Новым годом!", "Код без багов!", "Отпуск в январе!", "Ещё кофе!",
        "Прод не упадёт до 9 января!", "Зарплата x2!", "Тазик оливье!", "Шоколадный Дед Мороз!", "Мерж без конфликтов!",
        "Зелёный CI с первого раза!", "Свитер с оленями!", "Сон до обеда!", "Пятница каждый день!", "Мандарины без косточек!",
        "Новый MacBook! ...шучу, носки", "Счастья, здоровья, аптайма!", "Релиз без хотфиксов!", "Ноль митингов в январе!",
        "Ёлочная игрушка!", "Бенгальский огонь!", "Варежки!", "Шапка-ушанка!", "Валенки!", "Коробка. Просто коробка",
        "Внутри ещё одна коробка...", "Пустота. Зато красиво", "Гирлянда, которая не путается!", "Икра! Красная!",
        "Холодец!", "Селёдка под шубой!", "Ирония судьбы по ТВ!", "Новогоднее чудо!", "Желание сбудется!",
        "Удача на весь год!", "Отпуск без рабочего чата!", "Пароль от вайфая Деда Мороза", "Код-ревью: LGTM",
        "Все тикеты закрыты!", "Тимлид в отпуске!", "Кофе бесплатно весь год!", "+100 к карме!", "Тёплый плед!",
        "Какао с маршмеллоу!", "Пряник!", "Леденец!", "Мишура!", "Календарь с котиками!", "Год без дедлайнов!",
        "Годовая премия!", "Сюрприз! Это ты!", "Билет на море!", "Ноутбук не тормозит!", "Батарея на весь день!",
        "Наушники, которые не путаются", "Вечный пробник мандарина", "Прощение за TODO в коде", "Тесты сами написались!",
        "Продакшн-доступ... нет, тебе рано", "Мандарин с секретом!", "Хорошее настроение!", "Объятия от снеговика!",
        "Снежинка ручной работы", "Ты молодец в этом году!", "Оливье, проверенное временем", "Время на хобби!",
        "Пульт от телевизора! Наконец-то", "Котёнок! ...нет, это шарф", "Год без багов! ...почти",
    };

    private static readonly string[] CoalLines =
    {
        "Уголёк. Ты плохо себя вёл в этом году", "Уголь. Зато экологичный", "Уголёк. За пятничные деплои",
        "Уголёк. За TODO в коде", "Уголёк. За force push в main", "Уголёк. Подумай о своём поведении",
    };

    private static readonly string[] FireworkLines = { "Салют!", "Бабах!", "Праздничный салют!", "Ой, оно взрывается!" };

    private void OpenPresent(Present p)
    {
        Presents.Remove(p);
        float x = p.X, y = p.Y;
        SKPoint At() => new(x, y - 40);
        double r = Rng.NextDouble();

        if (p.Coal || r < 0.10)
        {
            for (int i = 0; i < 9; i++)
                Spawn(PKind.Smoke, x + Rand(-6, 6), y, Rand(-20, 20), Rand(-50, -20), Rand(3, 6), Rand(1, 2), new SKColor(60, 60, 66));
            Say(Pick(CoalLines), At, 3);
            return;
        }

        Confetti(x, y, 50);
        Sparkles(x, y, 16, 90);

        if (r < 0.22)
        {
            for (int i = 0; i < 3; i++) LaunchRocket(x + Rand(-30, 30), y);
            Say(Pick(FireworkLines), At, 2.5f);
        }
        else if (r < 0.31)
        {
            SnowmanFromBox(x);
        }
        else if (r < 0.39)
        {
            for (int i = 0; i < Ground.N; i++)
            {
                float d = MathF.Abs(Ground.BinCenter(i) - x) / 55;
                if (d < 1) Ground.AddBin(i, 30 * (1 - d * d));
            }
            Powder(x, y, 20, 90, 120);
            Say("Личный сугроб!", At, 2.5f);
        }
        else if (r < 0.45 && !Grinch.Active)
        {
            var g = Grinch;
            g.Active = true;
            g.State = GrinchState.Escape;
            g.StateT = 0;
            g.X = x;
            g.Dir = x < W / 2 ? -1 : 1;
            g.HasPresent = true;
            g.PresentColor = p.Box;
            g.PresentCoal = false;
            g.Hits = 0;
            g.Reformed = false;
            g.HasCarrot = false;
            g.Target = null;
            g.Victim = null;
            g.HitT = 99;
            Say("Сюрприз! Хе-хе!", GrinchAnchor, 2f);
            React(GrinchAlert, 0.8f);
        }
        else if (r < 0.52)
        {
            _flurryT = 20;
            Say("Метель в подарок!", At, 2.5f);
        }
        else if (r < 0.55 && !Tractor.Active)
        {
            StartTractor();
        }
        else
        {
            Say(Pick(PresentLines), At, 2.8f);
        }
    }

    private void SnowmanFromBox(float x)
    {
        x = Math.Clamp(x, 30, W - 30);
        Ball? below = null, root = null;
        foreach (var r in new[] { 22f, 16f, 11f })
        {
            var b = new Ball { X = x, R = r, Y = Ground.SurfaceY(x) - r, Below = below };
            if (below != null) below.Above = b;
            root ??= b;
            below = b;
            Balls.Add(b);
        }
        MakeSnowman(root!, hello: "Я был в коробке!");
    }

    private void UpdatePresents(float dt)
    {
        for (int i = Presents.Count - 1; i >= 0; i--)
        {
            var p = Presents[i];
            p.Age += dt;
            if (!p.Landed)
            {
                p.VY += 420 * dt;
                p.VX *= 1 - 0.4f * dt;
                p.X += p.VX * dt;
                p.Y += p.VY * dt;
                if (p.X < p.Size) { p.X = p.Size; p.VX = Math.Abs(p.VX); }
                if (p.X > W - p.Size) { p.X = W - p.Size; p.VX = -Math.Abs(p.VX); }
                float g = Ground.SurfaceY(p.X) - p.Size * 0.35f;
                if (p.Y >= g)
                {
                    p.Y = g;
                    p.Landed = true;
                    Powder(p.X, g + p.Size * 0.3f, 8, 80, 80);
                }
            }
            else
            {
                p.Y = Ground.SurfaceY(p.X) - p.Size * 0.35f;
            }
            if (p.Age > 900) Presents.RemoveAt(i);
        }
    }

    // ------------------------------------------------------------------ tractor

    public void StartTractor()
    {
        if (Tractor.Active) return;
        Tractor.Active = true;
        Tractor.Dir = Rng.Next(2) == 0 ? 1 : -1;
        Tractor.X = Tractor.Dir > 0 ? -170 : W + 170;
        Tractor.Load = 0;
        Tractor.T = 0;
        Tractor.Passed = false;
        Say("Бип-бип! Чищу!", () => new SKPoint(Math.Clamp(Tractor.X, 60, W - 60), H - 135), 2.5f);
        React(TractorAlert, 0.5f, Tractor.Dir > 0 ? 0 : W);
    }

    private static readonly string[] TractorAlert =
    {
        "Трактор! Только не меня!", "Осторожно, я живой!", "Бип-бип и тебе", "Жаль, у меня нет ног", "Аккуратнее там!",
    };

    private void UpdateTractor(float dt)
    {
        var t = Tractor;
        if (!t.Active)
        {
            Ground.Wall = -1;
            return;
        }

        t.T += dt;
        float prevBlade = t.BladeX;
        t.X += t.Dir * t.Speed * dt;
        t.Wheel += t.Speed * dt / 25f * t.Dir;
        float blade = t.BladeX;

        // scrape everything the blade passed
        float a = Math.Min(prevBlade, blade), b = Math.Max(prevBlade, blade);
        if (b >= 0 && a < W)
        {
            int ia = Ground.BinOf(a), ib = Ground.BinOf(b);
            for (int i = ia; i <= ib; i++)
            {
                float keep = 1.5f + (i * 7 % 5) * 0.25f;
                if (Ground.H[i] > keep)
                {
                    t.Load += Ground.H[i] - keep;
                    Ground.H[i] = keep;
                }
            }
            int wall = Ground.BinOf(blade);
            Ground.Wall = t.Dir > 0 ? wall + 1 : wall;
            // keep the cleaned strip behind the blade from refilling by slope relaxation
            int behindA = t.Dir > 0 ? Ground.BinOf(blade - 260) : wall;
            int behindB = t.Dir > 0 ? wall : Ground.BinOf(blade + 260);
            for (int i = behindA; i <= behindB; i++)
                Ground.H[i] = Math.Min(Ground.H[i], 3f);
        }
        else Ground.Wall = -1;

        // shove loose balls and presents
        float front = blade + t.Dir * t.PileH * 0.9f;
        foreach (var ball in Balls)
        {
            if (ball.Below != null || ball.Above != null || ball.Held || ball.Flying || ball.SupportId >= 0 || ball.R > 30) continue;
            bool ahead = t.Dir > 0 ? ball.X > t.X && ball.X - ball.R < front : ball.X < t.X && ball.X + ball.R > front;
            if (ahead) ball.X = front + t.Dir * ball.R;
        }
        foreach (var p in Presents)
        {
            bool ahead = t.Dir > 0 ? p.X > t.X && p.X - p.Size * 0.5f < front : p.X < t.X && p.X + p.Size * 0.5f > front;
            if (ahead && p.Landed) p.X = front + t.Dir * p.Size * 0.5f;
        }
        foreach (var h in Hats)
        {
            if (!h.Landed || h.OnPile != null) continue;
            bool ahead = t.Dir > 0 ? h.X > t.X && h.X - h.R * 0.8f < front : h.X < t.X && h.X + h.R * 0.8f > front;
            if (ahead) h.X = front + t.Dir * h.R * 0.8f;
        }

        if (t.Load > 30 && Rng.NextDouble() < Math.Min(0.9, t.Load / 2500.0))
            Spawn(PKind.Powder, front, H - t.PileH, t.Dir * Rand(20, 90), Rand(-140, -60), Rand(1, 2.5f), Rand(0.6f, 1.2f), SKColors.White);

        t.SmokeT -= dt;
        if (t.SmokeT <= 0)
        {
            t.SmokeT = 0.12f;
            float ex = t.X + t.Dir * 17 * Tractor.Scale, ey = H - 82 * Tractor.Scale;
            Spawn(PKind.Smoke, ex, ey, -t.Dir * Rand(5, 20), Rand(-45, -25), Rand(3, 5), Rand(1.2f, 2f), new SKColor(120, 120, 130));
        }

        // drive on to the next display; snow scraped here after the handover is sent along
        if (Next(t.Dir) is { } next)
        {
            if (!t.Passed)
            {
                float nx = t.X + Dx(next);
                if (t.Dir > 0 ? nx >= -170 : nx <= next.W + 170)
                {
                    t.Passed = true;
                    t.LoadSent = t.Load;
                    float load = t.Load, wheel = t.Wheel, tt = t.T;
                    int dir = t.Dir;
                    next.Post(w => w.AcceptTractor(nx, dir, load, wheel, tt));
                }
            }
            else if (t.Load > t.LoadSent)
            {
                float delta = t.Load - t.LoadSent;
                t.LoadSent = t.Load;
                next.Post(w => { if (w.Tractor.Active) w.Tractor.Load += delta; });
            }
        }

        if ((t.Dir > 0 && t.X > W + 220) || (t.Dir < 0 && t.X < -220))
        {
            t.Active = false;
            t.Load = 0;
            Ground.Wall = -1;
        }
    }

    // ------------------------------------------------------------------ grinch

    private static readonly string[] GrinchLines =
    {
        "Хе-хе-хе!", "Это теперь моё!", "Ненавижу праздники!", "Морковка!", "Ничего личного!", "Отменяю Новый год!",
        "Буду есть с хрустом!", "Праздник отменяется!", "Мне нужнее!",
    };

    private static readonly string[] GrinchAlert =
    {
        "Гринч! Кидай в него снежок!", "Держите подарки!", "Опять он!", "Гринч! Швырни ком в него!", "Спасите мою морковку!",
    };

    private static readonly string[] GrinchOuch =
    {
        "Ай!", "Нечестно!", "Мой мех!", "Ладно-ладно, ухожу!", "Я ещё вернусь!", "Холодно же!", "Снег за шиворот!",
    };

    private SKPoint GrinchAnchor() => new(Grinch.X, Ground.SurfaceY(Grinch.X) - 110);


    public void StartGrinch()
    {
        if (Grinch.Active) return;
        var g = Grinch;
        g.Active = true;
        g.State = GrinchState.Run;
        g.HasCarrot = false;
        g.HasPresent = false;
        g.Target = null;
        g.Victim = null;
        g.HitT = 99;
        g.Passed = false;
        g.Hits = 0;
        g.Reformed = false;

        object? target = Presents.Where(p => p.Landed).OrderBy(_ => Rng.Next()).FirstOrDefault();
        target ??= Balls.Where(b => b.Below == null && b.SupportId < 0 && b.Man is { HasNose: true }).OrderBy(_ => Rng.Next()).FirstOrDefault();
        g.Target = target;

        float tx = target switch { Present p => p.X, Ball b => b.X, _ => W / 2 };
        g.Dir = tx > W / 2 ? 1 : -1;
        if (target == null) g.Dir = Rng.Next(2) == 0 ? 1 : -1;
        g.X = g.Dir > 0 ? -60 : W + 60;
        React(GrinchAlert, 0.75f);
    }

    private void UpdateGrinch(float dt)
    {
        var g = Grinch;
        if (!g.Active) return;
        g.StateT += dt;
        g.HitT += dt;

        switch (g.State)
        {
            case GrinchState.Run:
            {
                g.X += g.Dir * 170 * dt;
                g.Step += dt * 13;
                if (g.Target != null)
                {
                    float tx = g.Target switch
                    {
                        Present p => p.X,
                        Ball b => b.X - g.Dir * (b.R + 14),
                        _ => 0,
                    };
                    bool gone = g.Target switch
                    {
                        Present p => !Presents.Contains(p),
                        Ball b => !Balls.Contains(b) || b.Man is not { HasNose: true },
                        _ => true,
                    };
                    if (gone) g.Target = null;
                    else if (MathF.Abs(g.X - tx) < 8)
                    {
                        g.State = GrinchState.Steal;
                        g.StateT = 0;
                    }
                }
                break;
            }
            case GrinchState.Steal:
                if (g.StateT > 0.6f && g.Target != null)
                {
                    if (g.Target is Present p && Presents.Remove(p))
                    {
                        g.HasPresent = true;
                        g.PresentColor = p.Box;
                        g.PresentCoal = p.Coal;
                        Powder(p.X, p.Y, 6);
                    }
                    else if (g.Target is Ball b && b.Man is { HasNose: true } man)
                    {
                        man.HasNose = false;
                        man.NoseLostAt = Time;
                        g.HasCarrot = true;
                        g.Victim = man;
                        var head = b.Top;
                        Say("Эй! Мой нос!", () => new SKPoint(head.X, head.Y - head.R * 2.3f), 2.5f);
                    }
                    g.Target = null;
                    Say(Pick(GrinchLines), GrinchAnchor, 2f);
                }
                if (g.StateT > 1.3f)
                {
                    g.State = GrinchState.Escape;
                    g.StateT = 0;
                }
                break;
            case GrinchState.Escape:
                g.X += g.Dir * (g.Reformed ? 90 : 280) * dt;
                g.Step += dt * (g.Reformed ? 9 : 18);
                break;
        }

        // footprints in the drift
        float s = MathF.Sin(g.Step);
        if (g.State != GrinchState.Steal && MathF.Sign(s) != MathF.Sign(g.LastStepSin) && g.X > 0 && g.X < W)
        {
            float fx = g.X + g.Dir * (s > 0 ? 8 : -4);
            Ground.Take(fx - 3, fx + 3, 2.5f);
            if (Ground.HeightAt(fx) > 3) Powder(fx, Ground.SurfaceY(fx), 2, 30, 50);
        }
        g.LastStepSin = s;

        if (!g.Passed && g.Target == null && Next(g.Dir) is { } n)
        {
            float nx = g.X + Dx(n);
            if (g.Dir > 0 ? nx >= -60 : nx <= n.W + 60)
            {
                g.Passed = true;
                var copy = new Grinch
                {
                    Active = true, X = nx, Dir = g.Dir, State = g.State == GrinchState.Steal ? GrinchState.Escape : g.State,
                    StateT = g.StateT, Step = g.Step, LastStepSin = g.LastStepSin, HasCarrot = g.HasCarrot,
                    HasPresent = g.HasPresent, PresentColor = g.PresentColor, HitT = g.HitT,
                    PresentCoal = g.PresentCoal, Hits = g.Hits, Reformed = g.Reformed,
                };
                n.Post(w => w.AcceptGrinch(copy));
            }
        }

        if (g.X < -120 || g.X > W + 120) g.Active = false;
    }

    // ------------------------------------------------------------------ santa

    public void StartSanta() => StartSanta(Rng.Next(2) == 0 ? 1 : -1, this);

    /// <summary>Santa starts at the far end of the linked displays and drops the present on <paramref name="dropOn"/>.</summary>
    private void StartSanta(int dir, World dropOn)
    {
        var first = End(-dir);
        if (first != this) first.Post(w => w.LaunchSanta(dir, dropOn));
        else LaunchSanta(dir, dropOn);
    }

    private void LaunchSanta(int dir, World dropOn)
    {
        if (Santa.Active) return;
        var s = Santa;
        s.Active = true;
        s.Dir = dir;
        s.X = s.Dir > 0 ? -260 : W + 260;
        s.Y0 = MenuBarH + Rand(70, Math.Max(90, H * 0.28f));
        s.T = 0;
        s.Dropped = false;
        s.DropX = Rand(W * 0.15f, W * 0.85f);
        s.DropWorld = dropOn;
        s.Extra = 0;
        s.ClickCd = 0;
        s.Passed = false;
        React(SantaAlert, 0.6f);
    }

    private void AcceptSanta(float x, float globalY0, float t, int dir, object? dropOn, bool dropped, int extra)
    {
        if (Santa.Active) return;
        var s = Santa;
        s.Active = true;
        s.X = x;
        s.Dir = dir;
        s.Y0 = Math.Clamp(globalY0 - (float)Screen.Cg.Y, MenuBarH + 50, Math.Max(MenuBarH + 60, H * 0.5f));
        s.T = t;
        s.DropWorld = dropOn;
        s.Dropped = dropped;
        s.DropX = Rand(W * 0.15f, W * 0.85f);
        s.Extra = extra;
        s.ClickCd = 0;
        s.Passed = false;
        React(SantaAlert, 0.4f);
    }

    private static readonly string[] SantaAlert =
    {
        "Санта!!!", "Я хорошо себя вёл!", "Хо-хо-хо! ...я тренируюсь", "Санта, кликни мне подарок!", "Дед Мороз! То есть Санта!",
    };

    private static readonly string[] SantaClick =
    {
        "Хо-хо! Держи ещё!", "Лови!", "Для хорошего человека не жалко!", "Последний! ...наверное", "Хо-хо-хо!",
    };

    private bool HitSanta(float x, float y)
    {
        var s = Santa;
        if (!s.Active) return false;
        float lx = (x - s.X) / (s.Dir * 0.9f), ly = (y - s.Y) / 0.9f;
        return lx > -42 && lx < 190 && ly > -64 && ly < 6;
    }

    internal void SantaGift()
    {
        var s = Santa;
        if (s.ClickCd > 0) return;
        s.ClickCd = 0.8f;
        float sx = s.X, sy = s.Y;
        if (s.Extra == 3 && Presents.Count < 8)
        {
            // greedy clicking gets a lump of coal
            s.Extra++;
            s.ClickCd = 2;
            DropPresent(s.X - s.Dir * 20, s.Y, s.Dir * 60, coal: true);
            Say("Хо-хо! А жадинам — уголёк!", () => new SKPoint(sx, sy - 40), 2.5f);
            return;
        }
        if (s.Extra >= 3 || Presents.Count >= 8)
        {
            s.ClickCd = 3;
            Say("Хо-хо! Хватит на сегодня!", () => new SKPoint(sx, sy - 40), 2f);
            return;
        }
        s.Extra++;
        DropPresent(s.X - s.Dir * 20, s.Y, s.Dir * 60);
        Sparkles(s.X, s.Y - 20, 14, 70);
        Say(Pick(SantaClick), () => new SKPoint(sx, sy - 40), 2f);
    }

    private void DropPresent(float x, float y, float vx, bool coal = false)
    {
        if (coal)
        {
            Presents.Add(new Present { X = x, Y = y, VX = vx, VY = -40, Size = 16, Box = new SKColor(38, 38, 44), Ribbon = new SKColor(70, 70, 80), Coal = true });
            return;
        }
        var c = BoxColors[Rng.Next(BoxColors.Length)];
        var rib = BoxColors[(Array.IndexOf(BoxColors, c) + 2) % BoxColors.Length];
        Presents.Add(new Present { X = x, Y = y, VX = vx, VY = -40, Size = Rand(18, 26), Box = c, Ribbon = rib == c ? SKColors.Gold : rib });
    }

    private static readonly SKColor[] BoxColors =
    {
        new(220, 38, 38), new(37, 99, 235), new(22, 163, 74), new(147, 51, 234), new(234, 179, 8),
    };

    private void UpdateSanta(float dt)
    {
        var s = Santa;
        if (!s.Active) return;
        s.T += dt;
        s.ClickCd -= dt;
        s.X += s.Dir * 150 * dt;
        s.Y = s.Y0 + MathF.Sin(s.T * 1.3f) * 16;

        s.TrailT -= dt;
        if (s.TrailT <= 0)
        {
            s.TrailT = 0.03f;
            Spawn(PKind.Sparkle, s.X - s.Dir * 30, s.Y + Rand(-6, 4), -s.Dir * Rand(10, 40), Rand(-10, 15), Rand(1.2f, 2.6f),
                Rand(0.8f, 1.6f), Rng.Next(3) == 0 ? new SKColor(255, 230, 140) : SKColors.White);
        }

        if (!s.Passed && Next(s.Dir) is { } n)
        {
            float nx = s.X + Dx(n);
            if (s.Dir > 0 ? nx >= -260 : nx <= n.W + 260)
            {
                s.Passed = true;
                float gy = s.Y0 + (float)Screen.Cg.Y, t = s.T;
                int dir = s.Dir, extra = s.Extra;
                var drop = s.DropWorld;
                bool dropped = s.Dropped;
                n.Post(w => w.AcceptSanta(nx, gy, t, dir, drop, dropped, extra));
            }
        }

        if (!s.Dropped && (s.DropWorld == null || s.DropWorld == this) && (s.Dir > 0 ? s.X > s.DropX : s.X < s.DropX))
        {
            s.Dropped = true;
            if (Presents.Count < 6)
            {
                DropPresent(s.X - s.Dir * 20, s.Y, s.Dir * 60);
                float sx = s.X, sy = s.Y;
                Say("Хо-хо-хо!", () => new SKPoint(sx, sy - 40), 2f);
            }
        }

        if ((s.Dir > 0 && s.X > W + 300) || (s.Dir < 0 && s.X < -300)) s.Active = false;
    }

    // ------------------------------------------------------------------ random events

    private void UpdateEvents(float dt)
    {
        _tractorCheck -= dt;
        if (_tractorCheck <= 0)
        {
            _tractorCheck = 4;
            if (S.AutoTractor && !Tractor.Active && (Ground.Average > 22 || Ground.Max > 70)) StartTractor();
        }

        if (S.RandomGrinch)
        {
            _nextGrinch -= dt;
            if (_nextGrinch <= 0)
            {
                _nextGrinch = Rand(200, 420);
                StartGrinch();
            }
        }

        if (S.RandomSanta)
        {
            _nextSanta -= dt;
            if (_nextSanta <= 0)
            {
                _nextSanta = Rand(150, 360);
                // linked displays share one Santa: only the leftmost one rolls the dice
                if (Next(-1) == null)
                {
                    var chain = Chain();
                    StartSanta(Rng.Next(2) == 0 ? 1 : -1, chain[Rng.Next(chain.Count)]);
                }
            }
        }

        _flurryT -= dt;

        if (S.Chatter)
        {
            _nextChat -= dt;
            if (_nextChat <= 0)
            {
                _nextChat = Rand(30, 80);
                Chat();
            }
        }

        if (S.ShootingStars)
        {
            _nextStar -= dt;
            if (_nextStar <= 0)
            {
                int hour = DateTime.Now.Hour;
                _nextStar = hour >= 17 || hour < 7 ? Rand(35, 110) : Rand(120, 300);
                ShootingStar();
            }
        }
    }

    // ------------------------------------------------------------------ snowballs

    private static readonly string[] OuchLines = { "Ай!", "Эй, за что?", "Снежная война?!", "Нечестно!", "Хи-хи, щекотно", "Мимо! ...ой, нет" };
    private static readonly string[] CheerLines = { "Попал!", "Так его!", "Меткий!", "В яблочко!", "Ура!" };

    /// <summary>Moves a thrown snowball; returns true when it splatted or left the screen.</summary>
    private bool Fly(Ball b, float dt)
    {
        float py = b.Y;
        b.VY += 900 * dt;
        b.X += b.VX * dt;
        b.Y += b.VY * dt;
        b.Angle += b.VX * dt / b.R;

        // a snowball flies on to the next display
        int side = b.X >= W ? 1 : b.X < 0 ? -1 : 0;
        if (side != 0 && Next(side) is { } next)
        {
            b.X += Dx(next);
            b.Y += Dy(next);
            next.Post(w => w.Balls.Add(b));
            return true;
        }

        if (b.X < -80 || b.X > W + 80 || b.Y > H + 80) return true;

        var g = Grinch;
        if (g.Active)
        {
            float feet = Ground.SurfaceY(g.X);
            if (MathF.Abs(b.X - g.X) < 20 + b.R && b.Y > feet - 100 - b.R && b.Y < feet + b.R)
            {
                HitGrinch();
                Splat(b);
                return true;
            }
        }

        foreach (var t in Balls)
        {
            if (t == b || t.Flying || t.Held) continue;
            float dx = b.X - t.X, dy = b.Y - t.Y, rr = b.R + t.R * 0.95f;
            if (dx * dx + dy * dy >= rr * rr) continue;
            var root = t.Root;
            if (root.Man != null)
            {
                SayMan(root, Pick(OuchLines));
                if (t.Above == null && t.Below != null && b.R >= 5) KnockHat(root, b.VX * 0.4f, -Rand(150, 230));
                ScareBirdsOn(root);
            }
            else if (t.Above == null && t.Below == null && t.R < 30) t.X = Math.Clamp(t.X + MathF.Sign(b.VX) * 10, t.R, W - t.R);
            Splat(b);
            return true;
        }

        if (b.VY > 0 && S.SnowOnWindows)
        {
            foreach (var p in PileOrder)
            {
                if (!p.Contains(b.X) || !p.VisibleAt(b.X)) continue;
                float s = p.SurfaceY(b.X);
                if (py + b.R * 0.5f <= s && b.Y + b.R * 0.5f >= s)
                {
                    Splat(b);
                    return true;
                }
            }
        }

        if (b.Y + b.R * 0.6f >= Ground.SurfaceY(b.X))
        {
            Splat(b);
            return true;
        }
        return false;
    }

    /// <summary>Breaks a snowball into clumps that settle wherever they land, so the snow is not lost.</summary>
    private void Splat(Ball b)
    {
        float total = b.R * b.R * MathF.PI / (Ground.BinW * 2.2f);
        int n = Math.Clamp((int)(total / 12), 4, 24);
        for (int i = 0; i < n; i++)
            Clump(b.X + Rand(-b.R, b.R) * 0.5f, b.Y + Rand(-b.R, b.R) * 0.5f, total / n, b.VX * 0.15f + Rand(-90, 90), Rand(-180, -40));
        Powder(b.X, b.Y, 10, 90, 90);
    }

    internal void HitGrinch()
    {
        var g = Grinch;
        g.HitT = 0;
        float feet = Ground.SurfaceY(g.X);
        Powder(g.X, feet - 70, 14, 90, 90);

        if (g.HasPresent)
        {
            g.HasPresent = false;
            Presents.Add(new Present
            {
                X = g.X, Y = feet - 60, VX = -g.Dir * 80, VY = -260, Size = g.PresentCoal ? 16 : 22, Box = g.PresentColor,
                Ribbon = SKColors.Gold, Coal = g.PresentCoal,
            });
        }
        if (g.HasCarrot)
        {
            g.HasCarrot = false;
            var root = Balls.FirstOrDefault(x => x.Man == g.Victim);
            if (root?.Man != null)
            {
                root.Man.HasNose = true;
                var head = root.Top;
                Sparkles(head.X + head.R * 0.4f, head.Y, 14, 60);
                SayMan(root, "Мой нос! Спасибо!");
            }
        }
        g.Victim = null;
        g.Target = null;

        g.Hits++;
        if (g.Hits >= 3 && !g.Reformed)
        {
            Reform();
            if (g.State != GrinchState.Escape)
            {
                g.State = GrinchState.Escape;
                g.StateT = 0;
            }
            return;
        }
        if (g.Reformed)
        {
            Say(Pick(new[] { "Эй, я же исправился!", "Ну хватит!", "Я хороший теперь!" }), GrinchAnchor, 2f);
            return;
        }

        Say(Pick(GrinchOuch), GrinchAnchor, 2f);
        React(CheerLines, 0.5f);
        if (g.State != GrinchState.Escape)
        {
            g.State = GrinchState.Escape;
            g.StateT = 0;
            g.Dir = g.X < W / 2 ? -1 : 1;
        }
    }

    // ------------------------------------------------------------------ fireworks, stars, new year

    private static readonly SKColor[] FireColors =
    {
        new(255, 80, 80), new(255, 210, 70), new(90, 230, 120), new(90, 170, 255), new(255, 120, 220), new(180, 120, 255),
    };

    public void LaunchRocket(float x, float fromY)
    {
        float apex = Rand(MenuBarH + 60, H * 0.45f);
        float v = MathF.Sqrt(2 * 450 * Math.Max(80, fromY - apex));
        Spawn(PKind.Rocket, x, fromY, Rand(-25, 25), -v, 2, 6, FireColors[Rng.Next(FireColors.Length)]);
    }

    public void Burst(float x, float y, SKColor col)
    {
        var col2 = Rng.Next(3) == 0 ? SKColors.White : FireColors[Rng.Next(FireColors.Length)];
        const int n = 72;
        for (int i = 0; i < n; i++)
        {
            float a = i * MathF.Tau / n + Rand(-0.05f, 0.05f), sp = Rand(120, 200);
            Spawn(PKind.Sparkle, x, y, MathF.Cos(a) * sp, MathF.Sin(a) * sp, Rand(2f, 3.2f), Rand(1.3f, 2.2f), i % 4 == 0 ? col2 : col);
        }
        Sparkles(x, y, 10, 40);
    }

    private static readonly string[] FireworkCheer = { "Уууух!", "Красота!", "Салют!", "Ещё!", "Вау!" };

    public void StartFireworks(float seconds)
    {
        _showT = Math.Max(_showT, seconds);
        _showNext = 0;
        React(FireworkCheer, 0.7f);
    }

    private void UpdateShow(float dt)
    {
        if (_showT <= 0) return;
        _showT -= dt;
        _showNext -= dt;
        if (_showNext > 0) return;
        _showNext = Rand(0.25f, 0.8f);
        int count = Rng.Next(4) == 0 ? 2 : 1;
        for (int i = 0; i < count; i++)
        {
            float x = Rand(W * 0.08f, W * 0.92f);
            LaunchRocket(x, Ground.SurfaceY(x));
        }
    }

    private static readonly string[] StarLines = { "Звезда! Загадай желание!", "Видел? Звезда упала!", "Я загадал морковку" };

    public void ShootingStar()
    {
        int dir = Rng.Next(2) == 0 ? 1 : -1;
        float x = dir > 0 ? Rand(-50, W * 0.5f) : Rand(W * 0.5f, W + 50);
        float y = MenuBarH + Rand(10, H * 0.25f);
        float sp = Rand(500, 800), a = Rand(0.25f, 0.5f);
        Spawn(PKind.Star, x, y, dir * MathF.Cos(a) * sp, MathF.Sin(a) * sp, 1.6f, Rand(0.9f, 1.5f), SKColors.White);
        React(StarLines, 0.35f);
    }

    /// <summary>Runs a 10-second countdown and the midnight show right now (for trying it out).</summary>
    public void RehearseNewYear()
    {
        _countdown = 10;
        _lastCount = 99;
        _countdownYear = 0;
    }

    private void UpdateClock(float dt)
    {
        if (_countdown >= 0)
        {
            _countdown -= dt;
            int k = (int)MathF.Ceiling(_countdown);
            if (k != _lastCount && k > 0)
            {
                _lastCount = k;
                Say(k.ToString(), () => new SKPoint(W / 2, H * 0.32f), 0.95f, 3);
            }
            if (_countdown <= 0)
            {
                _countdown = -1;
                Celebrate(_countdownYear);
            }
            return;
        }

        _clockCheck -= dt;
        if (_clockCheck > 0) return;
        _clockCheck = 0.5f;
        var now = DateTime.Now;

        if (now is { Hour: 13, Minute: 37 } && _leetDay != now.DayOfYear && S.Chatter)
        {
            _leetDay = now.DayOfYear;
            React(new[] { "13:37! Элитно.", "1337!", "Время хакеров: 13:37" }, 1);
        }
        if (now is { Month: 1, Day: 14, Hour: 0, Minute: < 2 } && _oldNewYear != now.Year && S.NewYearShow)
        {
            _oldNewYear = now.Year;
            StartFireworks(15);
            Say("Со Старым Новым годом!", () => new SKPoint(W / 2, H * 0.32f), 5, 1.8f);
        }

        if (!S.NewYearShow) return;
        if (now is { Month: 12, Day: 31, Hour: 23, Minute: 59, Second: >= 50 } && _celebratedYear != now.Year + 1)
        {
            _countdown = 60 - now.Second - now.Millisecond / 1000f;
            _lastCount = 99;
            _countdownYear = now.Year + 1;
        }
        else if (now is { Month: 1, Day: 1, Hour: 0, Minute: < 2 } && _celebratedYear != now.Year)
        {
            Celebrate(now.Year);
        }
    }

    private static readonly string[] NewYearLines = { "С Новым годом!", "Ура-а-а!", "С новым счастьем!", "Загадал!", "Чокаемся!" };

    private void Celebrate(int year)
    {
        if (year > 0) _celebratedYear = year;
        Say(year > 0 ? $"С Новым {year} годом!" : "С Новым годом!", () => new SKPoint(W / 2, H * 0.32f), 6, 2.2f);
        for (int k = 0; k < 6; k++) Confetti(W * (k + 0.5f) / 6, MenuBarH + 10, 40);
        StartFireworks(40);
        foreach (var root in Snowmen().ToList()) SayMan(root, Pick(NewYearLines), 4);
        StartSanta();
    }

    // ------------------------------------------------------------------ snowmen talk

    private IEnumerable<Ball> Snowmen() => Balls.Where(b => b.Below == null && b.Man != null && !b.Held);

    private void SayMan(Ball root, string text, float life = 2.5f)
    {
        var head = root.Top;
        Say(text, () => new SKPoint(head.X, head.Y - head.R * 2.3f), life);
    }

    /// <summary>A snowman (the one nearest to <paramref name="nearX"/>, or a random one) comments on an event.</summary>
    private void React(string[] lines, float chance, float? nearX = null)
    {
        if (Rng.NextDouble() > chance) return;
        var men = Snowmen().ToList();
        if (men.Count == 0) return;
        var man = nearX is { } nx ? men.OrderBy(b => MathF.Abs(b.X - nx)).First() : men[Rng.Next(men.Count)];
        SayMan(man, Pick(lines));
    }

    private static readonly string[] ChatLines =
    {
        "Холодно-то как!", "Хочу на море. Хотя нет, растаю", "Мне бы шарф потеплее", "Скоро Новый год!",
        "Ты работаешь или на снег смотришь?", "Ещё снега!", "Морковка чешется", "Летом меня не будет. Цени момент",
        "Стою. Это моё кардио", "Слепи мне друга!", "Жду Санту", "Хорошо стоим", "Не забудь сохранить файл!",
        "Перерыв! Попей чаю", "Я не толстый, я из трёх комов", "Снег — это вода в отпуске", "Мечтаю о холодильнике",
        "Где мои ноги?", "Чую Гринча...", "Хо-хо... ой, это не моя реплика", "Сосулька — снег с характером",
        "Красивые у тебя окна", "Помаши мне курсором!", "Если что, я за тобой не слежу", "Мандаринку бы...",
        "Кто-нибудь видел трактор?", "Подарки открывать будешь?", "Скинь в меня снежок. Шучу. Не надо",
        "Подожди, я позирую", "Ты сегодня продуктивный!", "Зима — лучшее время года", "Пойду погуляю. А, точно",
    };

    private void Chat()
    {
        if (Bubbles.Count > 1) return;
        var men = Snowmen().ToList();
        if (men.Count == 0) return;
        var man = men[Rng.Next(men.Count)];
        var now = DateTime.Now;

        var pool = new List<string>(ChatLines);
        if (!man.Man!.HasNose) pool.AddRange(new[] { "Кто видел мой нос?", "Без морковки как без рук", "Гринч, верни нос!" });
        if (now.Hour < 5) pool.AddRange(new[] { "Не спится?", "Иди спать, код подождёт", "Тсс, снег спит", "Уже ночь, а ты всё тут" });
        if (now is { Month: 12, Day: 31 }) pool.AddRange(new[] { "Сегодня оливье!", "Куранты скоро!", "Желание уже придумал?", "Последний день года!" });
        if (now is { Month: 1, Day: <= 2 }) pool.AddRange(new[] { "С Новым годом! Голова не болит?", "Оливье ещё осталось?", "Как отметил?" });
        if (now.Month == 12 && now.Day < 31) pool.Add($"До Нового года {31 - now.Day + 1} дн.");
        if (men.Count == 1) pool.AddRange(new[] { "Одиноко мне тут...", "Слепи мне компанию!" });
        if (men.Count >= 3) pool.AddRange(new[] { "Нас тут целая банда!", "Снеговики, стройся!" });
        if (Presents.Count > 0) pool.Add("Там подарок! Кликни!");
        if (!string.IsNullOrEmpty(man.Man.Name)) pool.Add($"Меня зовут {man.Man.Name}, кстати");
        if (man.Man.HatOff) pool.AddRange(new[] { "Уши мёрзнут без шапки...", "Кто-нибудь видел мою шапку?" });
        if (man.R >= 66) pool.AddRange(new[] { "Я тут главный", "Склонитесь перед королём!", "Корона не жмёт" });
        if (man.R <= 13) pool.AddRange(new[] { "Пи!", "Я маленький, но гордый!", "Меня не видно, да?" });
        if (now is { DayOfWeek: DayOfWeek.Friday, Hour: >= 16 }) pool.AddRange(new[] { "Пятница! Не деплой!", "Уже пятница, закругляйся" });
        if (now is { DayOfWeek: DayOfWeek.Monday, Hour: < 12 }) pool.Add("Понедельник... держись");
        if (now is { Month: 12, Day: 25 }) pool.Add("Merry Christmas!");
        if (now is { Month: 1, Day: 7 }) pool.Add("С Рождеством!");
        if (now is { Month: 1, Day: 13 }) pool.Add("Сегодня Старый Новый год!");
        if (Birds.Count > 0) pool.Add("Снегири прилетели!");
        if (Aurora > 0.3f) pool.Add("Какое сияние...");
        pool.AddRange(new[] { "Потыкай меня пять раз. Ну пожалуйста", "Говорят, Гринча можно перевоспитать", "Если потрясти окно, будет весело" });

        SayMan(man, Pick(pool.ToArray()), 3);
    }

    // ------------------------------------------------------------------ linked displays

    /// <summary>Displays that touch this one on the left and right (set by the app).</summary>
    public World? LeftNeighbor, RightNeighbor;

    private readonly ConcurrentQueue<Action<World>> _inbox = new();

    /// <summary>Runs <paramref name="action"/> on this world at the start of its next update (thread-safe).</summary>
    public void Post(Action<World> action) => _inbox.Enqueue(action);

    public World? Next(int dir) => !S.LinkScreens ? null : dir > 0 ? RightNeighbor : LeftNeighbor;

    /// <summary>Converts a local x of this world to a local x of <paramref name="other"/>.</summary>
    private float Dx(World other) => (float)(Screen.Cg.X - other.Screen.Cg.X);

    private float Dy(World other) => (float)(Screen.Cg.Y - other.Screen.Cg.Y);

    private void HandOverHeld(World n)
    {
        var held = Held!;
        var stack = held.StackFromRoot().ToList();
        foreach (var b in stack)
        {
            Balls.Remove(b);
            b.X += Dx(n);
            b.Y += Dy(n);
        }
        Held = null;
        float ox = _holdOffX, oy = _holdOffY, vx = _throwVX, vy = _throwVY;
        n.Post(w => w.AcceptHeld(stack, held, ox, oy, vx, vy));
    }

    private void AcceptHeld(List<Ball> stack, Ball held, float offX, float offY, float vx, float vy)
    {
        foreach (var b in stack) b.SupportId = -1;
        Balls.AddRange(stack);
        if (Held != null)
        {
            // already dragging something here (should not happen): just drop it
            held.Held = false;
            return;
        }
        Held = held;
        held.Held = true;
        _holdOffX = offX;
        _holdOffY = offY;
        _throwVX = vx;
        _throwVY = vy;
    }

    private World End(int dir)
    {
        var w = this;
        for (int guard = 0; guard < 16 && w.Next(dir) is { } n && n != this; guard++) w = n;
        return w;
    }

    private List<World> Chain()
    {
        var list = new List<World>();
        for (var w = End(-1); list.Count < 16; w = w.Next(1)!)
        {
            list.Add(w);
            if (w.Next(1) is not { } n || n == list[0]) break;
        }
        return list;
    }

    private void AcceptTractor(float x, int dir, float load, float wheel, float t)
    {
        if (Tractor.Active) return;
        var tr = Tractor;
        tr.Active = true;
        tr.X = x;
        tr.Dir = dir;
        tr.Load = load;
        tr.Wheel = wheel;
        tr.T = t;
        tr.SmokeT = 0;
        tr.Passed = false;
        React(TractorAlert, 0.4f, dir > 0 ? 0 : W);
    }

    private void AcceptGrinch(Grinch src)
    {
        if (Grinch.Active) return;
        var g = Grinch;
        g.Active = true;
        g.X = src.X;
        g.Dir = src.Dir;
        g.State = src.State;
        g.StateT = src.StateT;
        g.Step = src.Step;
        g.LastStepSin = src.LastStepSin;
        g.HasCarrot = src.HasCarrot;
        g.HasPresent = src.HasPresent;
        g.PresentColor = src.PresentColor;
        g.HitT = src.HitT;
        g.PresentCoal = src.PresentCoal;
        g.Hits = src.Hits;
        g.Reformed = src.Reformed;
        g.Victim = null;
        g.Passed = false;
        g.Target = null;
        if (g.State == GrinchState.Run)
        {
            // still hunting: look for loot ahead on this display
            bool Ahead(float x) => (x - g.X) * g.Dir > 0;
            g.Target = (object?)Presents.Where(p => p.Landed && Ahead(p.X)).OrderBy(_ => Rng.Next()).FirstOrDefault()
                       ?? Balls.Where(b => b.Below == null && b.SupportId < 0 && b.Man is { HasNose: true } && Ahead(b.X)).OrderBy(_ => Rng.Next()).FirstOrDefault();
        }
        if (g.Target != null || g.HasPresent || g.HasCarrot) React(GrinchAlert, 0.6f);
    }

    public void Say(string text, Func<SKPoint> anchor, float life = 2.5f, float scale = 1)
    {
        // one speaker, one bubble: drop whatever is already said at the same spot
        var at = anchor();
        Bubbles.RemoveAll(b =>
        {
            var o = b.Anchor();
            return MathF.Abs(o.X - at.X) < 40 && MathF.Abs(o.Y - at.Y) < 40;
        });
        Bubbles.Add(new Bubble { Text = text, Anchor = anchor, Life = life, Scale = scale });
    }

    private string Pick(string[] lines) => lines[Rng.Next(lines.Length)];
}
