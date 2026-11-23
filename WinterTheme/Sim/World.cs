using SkiaSharp;
using WinterTheme.Native;

namespace WinterTheme.Sim;

/// <summary>Whole winter scene for one display. Coordinates are local points, top-left origin, y down.</summary>
public sealed class World
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

    private static readonly Settings S = Settings.Current;
    private const float MaxBallR = 76;

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

        for (int i = Bubbles.Count - 1; i >= 0; i--)
        {
            Bubbles[i].Life -= dt;
            if (Bubbles[i].Life <= 0) Bubbles.RemoveAt(i);
        }

        if (S.Melt)
        {
            Ground.Melt(dt * 0.035f);
            foreach (var p in PileOrder) p.Melt(dt * 0.02f);
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

    private int TargetFlakes => S.Paused ? 0 : (int)(320 * S.FlakeFactor * (W * H) / (1512f * 982f));

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
        HoverBall = hover != null || hoverPresent != null;

        bool interactiveBefore = HoverInteractive;
        HoverInteractive = Held != null ||
                           (MouseInside && (HoverBall || (OptionDown && NearGround(Mx, My))));

        // only react to presses that our (interactive) window actually swallowed
        if (pressed && interactiveBefore && MouseInside && Held == null)
            OnPress(hover, hoverPresent);

        if (Held != null)
        {
            if (MouseDown) DragHeld(dt);
            if (released || !MouseDown) Release();
        }

        if (released && interactiveBefore) ClickReleased = true;

        if (S.CursorPush && Held == null && MouseInside)
        {
            float vx = (Mx - _pmx) / Math.Max(dt, 0.001f), vy = (My - _pmy) / Math.Max(dt, 0.001f);
            if (vx * vx + vy * vy > 25 * 25 && MathF.Abs(Mx - _pmx) < 300)
            {
                Push(Ground, vx);
                foreach (var p in PileOrder) Push(p, vx);
            }
        }
    }

    private void OnPress(Ball? hover, Present? present)
    {
        if (present != null)
        {
            OpenPresent(present);
            return;
        }

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

            Ball grab;
            if (hover.Above == null && hover.Below != null)
            {
                // pull the top ball off the stack
                root.Man = null;
                hover.Below.Above = null;
                hover.Below = null;
                grab = hover;
            }
            else
            {
                grab = hover.Above != null ? root : hover;
            }

            Held = grab;
            grab.Held = true;
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
        var b = Held!;
        float nx = Math.Clamp(Mx + _holdOffX, b.R, W - b.R);
        float ny = My + _holdOffY;

        if (b.Above == null && b.Below == null)
        {
            float groundY = Ground.SurfaceY(nx) - b.R * 0.85f;
            if (ny >= groundY - b.R * 0.35f)
            {
                // rolling along the drift: pick up snow and grow
                float dx = nx - b.X;
                b.Angle += dx / b.R;
                float per = Math.Min(MathF.Abs(dx) * 0.35f, 4f);
                if (per > 0.01f && b.R < MaxBallR)
                {
                    float taken = Ground.Take(nx - b.R * 0.55f, nx + b.R * 0.55f, per);
                    float area = b.R * b.R + taken * Ground.BinW * 2.2f / MathF.PI;
                    b.R = Math.Min(MaxBallR, MathF.Sqrt(area));
                    if (taken > 1.5f && Rng.NextDouble() < 0.5)
                        Powder(nx - Math.Sign(dx) * b.R * 0.8f, Ground.SurfaceY(nx), 1, 30, 30);
                }
                ny = Ground.SurfaceY(nx) - b.R * 0.85f;
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

        if (b.Above != null || b.Below != null) return;

        Ball? target = null;
        foreach (var t in Balls)
        {
            if (t == b || t.Above != null || t.StackCount >= 3) continue;
            if (MathF.Abs(b.X - t.X) < t.R * 0.9f && b.Y < t.Y && b.R <= t.R * 1.1f)
                if (target == null || t.Y < target.Y) target = t;
        }

        if (target != null)
        {
            b.Below = target;
            target.Above = b;
            Powder(b.X, b.Y + b.R, 5, 50, 40);
            if (b.StackCount == 3) MakeSnowman(b.Root);
        }
    }

    private static readonly SKColor[] ScarfColors =
    {
        new(220, 38, 38), new(37, 99, 235), new(22, 163, 74), new(234, 179, 8), new(147, 51, 234), new(236, 72, 153),
    };

    private static readonly string[] Hellos = { "Привет!", "Ура, я живой!", "Холодно!", "С Новым годом!", "Где мой нос?.. А, вот." };

    public void MakeSnowman(Ball root, int hat = -1, uint scarf = 0, bool nose = true)
    {
        root.Man = new Snowman
        {
            Hat = hat >= 0 ? hat : Rng.Next(4),
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
        Say(Hellos[Rng.Next(Hellos.Length)], () => new SKPoint(head.X, head.Y - head.R * 2.3f), 3);
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
        foreach (var b in Balls)
        {
            if (b.Below != null || b.Held) continue;
            float target = Ground.SurfaceY(b.X) - b.R * 0.85f;
            if (b.Y < target - 0.5f)
            {
                b.VY += 1000 * dt;
                b.Y += b.VY * dt;
                if (b.Y >= target)
                {
                    if (b.VY > 250) Powder(b.X, b.Y + b.R, 6, 70, 60);
                    b.Y = target;
                    b.VY = 0;
                }
            }
            else
            {
                b.Y += (target - b.Y) * Math.Min(1, dt * 6);
                b.VY = 0;
            }
        }

        foreach (var b in Balls)
        {
            if (b.Below != null) continue;
            for (var cur = b; cur.Above != null; cur = cur.Above)
            {
                var a = cur.Above;
                a.X = cur.X;
                a.Y = cur.Y - cur.R * 0.8f - a.R * 0.82f;
            }

            var man = b.Man;
            if (man == null) continue;
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
    };

    private void OpenPresent(Present p)
    {
        Presents.Remove(p);
        Confetti(p.X, p.Y, 50);
        Sparkles(p.X, p.Y, 16, 90);
        float x = p.X, y = p.Y;
        Say(PresentLines[Rng.Next(PresentLines.Length)], () => new SKPoint(x, y - 40), 2.5f);
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
        Say("Бип-бип! Чищу!", () => new SKPoint(Math.Clamp(Tractor.X, 60, W - 60), H - 135), 2.5f);
    }

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
            if (ball.Below != null || ball.Above != null || ball.Held || ball.R > 30) continue;
            bool ahead = t.Dir > 0 ? ball.X > t.X && ball.X - ball.R < front : ball.X < t.X && ball.X + ball.R > front;
            if (ahead) ball.X = front + t.Dir * ball.R;
        }
        foreach (var p in Presents)
        {
            bool ahead = t.Dir > 0 ? p.X > t.X && p.X - p.Size * 0.5f < front : p.X < t.X && p.X + p.Size * 0.5f > front;
            if (ahead && p.Landed) p.X = front + t.Dir * p.Size * 0.5f;
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

        if ((t.Dir > 0 && t.X > W + 220) || (t.Dir < 0 && t.X < -220))
        {
            t.Active = false;
            t.Load = 0;
            Ground.Wall = -1;
        }
    }

    // ------------------------------------------------------------------ grinch

    private static readonly string[] GrinchLines = { "Хе-хе-хе!", "Это теперь моё!", "Ненавижу праздники!", "Морковка!" };

    public void StartGrinch()
    {
        if (Grinch.Active) return;
        var g = Grinch;
        g.Active = true;
        g.State = GrinchState.Run;
        g.HasCarrot = false;
        g.HasPresent = false;
        g.Target = null;

        object? target = Presents.Where(p => p.Landed).OrderBy(_ => Rng.Next()).FirstOrDefault();
        target ??= Balls.Where(b => b.Below == null && b.Man is { HasNose: true }).OrderBy(_ => Rng.Next()).FirstOrDefault();
        g.Target = target;

        float tx = target switch { Present p => p.X, Ball b => b.X, _ => W / 2 };
        g.Dir = tx > W / 2 ? 1 : -1;
        if (target == null) g.Dir = Rng.Next(2) == 0 ? 1 : -1;
        g.X = g.Dir > 0 ? -60 : W + 60;
    }

    private void UpdateGrinch(float dt)
    {
        var g = Grinch;
        if (!g.Active) return;
        g.StateT += dt;

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
                        Powder(p.X, p.Y, 6);
                    }
                    else if (g.Target is Ball b && b.Man is { HasNose: true } man)
                    {
                        man.HasNose = false;
                        man.NoseLostAt = Time;
                        g.HasCarrot = true;
                        var head = b.Top;
                        Say("Эй! Мой нос!", () => new SKPoint(head.X, head.Y - head.R * 2.3f), 2.5f);
                    }
                    g.Target = null;
                    Say(GrinchLines[Rng.Next(GrinchLines.Length)], () => new SKPoint(Grinch.X, Ground.SurfaceY(Grinch.X) - 110), 2f);
                }
                if (g.StateT > 1.3f)
                {
                    g.State = GrinchState.Escape;
                    g.StateT = 0;
                }
                break;
            case GrinchState.Escape:
                g.X += g.Dir * 280 * dt;
                g.Step += dt * 18;
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

        if (g.X < -120 || g.X > W + 120) g.Active = false;
    }

    // ------------------------------------------------------------------ santa

    public void StartSanta()
    {
        if (Santa.Active) return;
        var s = Santa;
        s.Active = true;
        s.Dir = Rng.Next(2) == 0 ? 1 : -1;
        s.X = s.Dir > 0 ? -260 : W + 260;
        s.Y0 = MenuBarH + Rand(70, Math.Max(90, H * 0.28f));
        s.T = 0;
        s.Dropped = false;
        s.DropX = Rand(W * 0.15f, W * 0.85f);
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
        s.X += s.Dir * 150 * dt;
        s.Y = s.Y0 + MathF.Sin(s.T * 1.3f) * 16;

        s.TrailT -= dt;
        if (s.TrailT <= 0)
        {
            s.TrailT = 0.03f;
            Spawn(PKind.Sparkle, s.X - s.Dir * 30, s.Y + Rand(-6, 4), -s.Dir * Rand(10, 40), Rand(-10, 15), Rand(1.2f, 2.6f),
                Rand(0.8f, 1.6f), Rng.Next(3) == 0 ? new SKColor(255, 230, 140) : SKColors.White);
        }

        if (!s.Dropped && (s.Dir > 0 ? s.X > s.DropX : s.X < s.DropX))
        {
            s.Dropped = true;
            if (Presents.Count < 6)
            {
                var c = BoxColors[Rng.Next(BoxColors.Length)];
                var rib = BoxColors[(Array.IndexOf(BoxColors, c) + 2) % BoxColors.Length];
                Presents.Add(new Present { X = s.X - s.Dir * 20, Y = s.Y, VX = s.Dir * 60, VY = -40, Size = Rand(18, 26), Box = c, Ribbon = rib == c ? SKColors.Gold : rib });
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
                StartSanta();
            }
        }
    }

    public void Say(string text, Func<SKPoint> anchor, float life = 2.5f)
    {
        Bubbles.Add(new Bubble { Text = text, Anchor = anchor, Life = life });
    }
}
