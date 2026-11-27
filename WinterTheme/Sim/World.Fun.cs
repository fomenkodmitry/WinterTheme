using SkiaSharp;

namespace WinterTheme.Sim;

/// <summary>Extra physics and easter eggs: rolling balls, hats in the wind, icicles, bullfinches, aurora and friends.</summary>
public sealed partial class World
{
    // ------------------------------------------------------------------ delayed actions

    private readonly List<(float At, Action Do)> _later = new();

    private void Later(float delay, Action action) => _later.Add((Time + delay, action));

    private void RunLater()
    {
        for (int i = _later.Count - 1; i >= 0; i--)
        {
            if (_later[i].At > Time) continue;
            var a = _later[i].Do;
            _later.RemoveAt(i);
            a();
        }
    }

    /// <summary>For the offscreen checks: pretend a gust is blowing.</summary>
    internal void ForceGust(float gust) { _gust = _gustTarget = gust; _gustT = 60; }

    private void UpdateFun(float dt)
    {
        RunLater();
        UpdateHats(dt);
        UpdateIcicles(dt);
        UpdateBirds(dt);
        Spindrift(dt);
        SnowmenDefend(dt);
        UpdateAurora(dt);
        _quakeCd -= dt;
    }

    // ------------------------------------------------------------------ rolling balls

    /// <summary>A loose ball on a slope rolls downhill, picks up a little snow and bumps into other balls.</summary>
    private void Roll(Ball b, float dt)
    {
        float h = Math.Max(3, b.R * 0.6f);
        float slope = (Ground.SurfaceY(Math.Min(W - 1, b.X + h)) - Ground.SurfaceY(Math.Max(0, b.X - h))) / (2 * h);
        b.VX += slope * 620 * dt;
        b.VX *= 1 - Math.Min(1, 2.2f * dt);
        if (MathF.Abs(b.VX) < 4 && MathF.Abs(slope) < 0.12f)
        {
            b.VX = 0;
            return;
        }
        b.VX = Math.Clamp(b.VX, -320, 320);
        float nx = b.X + b.VX * dt;

        foreach (var t in Balls)
        {
            if (t == b || t.Below != null || t.Flying || t.Held) continue;
            float d = t.X - b.X;
            if (MathF.Sign(d) != MathF.Sign(b.VX) || MathF.Abs(t.X - nx) >= b.R + t.R * 0.9f) continue;
            if (MathF.Abs(t.Y - b.Y) > b.R + t.R) continue;
            if (t.Above == null && t.Man == null)
            {
                // pass the momentum on, like billiards
                float k = b.R * b.R / (t.R * t.R);
                t.VX += b.VX * Math.Min(1.2f, k) * 0.8f;
                b.VX *= 0.2f;
            }
            else
            {
                b.VX = -b.VX * 0.25f;
                if (t.Root.Man != null && MathF.Abs(b.VX) > 20 && Rng.NextDouble() < 0.5)
                    SayMan(t.Root, Pick(new[] { "Ой!", "Осторожнее!", "Кто катается?" }));
            }
            nx = b.X;
            break;
        }

        if (nx < b.R) { nx = b.R; b.VX = MathF.Abs(b.VX) * 0.3f; }
        if (nx > W - b.R) { nx = W - b.R; b.VX = -MathF.Abs(b.VX) * 0.3f; }

        float dx = nx - b.X;
        b.Angle += dx / b.R;
        if (MathF.Abs(b.VX) > 40 && b.R < MaxBallR)
        {
            float taken = Ground.Take(nx - b.R * 0.5f, nx + b.R * 0.5f, MathF.Abs(dx) * 0.12f);
            b.R = Math.Min(MaxBallR, MathF.Sqrt(b.R * b.R + taken * Ground.BinW * 2.2f / MathF.PI));
        }
        b.X = nx;
    }

    // ------------------------------------------------------------------ snowmen on windows

    private static readonly string[] RideLines = { "Полегче!", "Покатай меня!", "Вжух!", "Меня укачивает...", "Йе-хууу!" };

    /// <summary>Surface the (root) ball rests on: the cap of its window, or the ground drift.</summary>
    public float RestSurface(Ball b) =>
        b.SupportId >= 0 && WinPiles.TryGetValue(b.SupportId, out var p) ? p.SurfaceY(Math.Clamp(b.X, p.Left, p.Right - 0.01f)) : Ground.SurfaceY(b.X);

    /// <summary>Rides along with its window; falls when the window is gone, minimized or too small now.</summary>
    private void FollowSupport(Ball b)
    {
        if (b.SupportId < 0) return;
        if (WinPiles.TryGetValue(b.SupportId, out var p) && S.SnowOnWindows)
        {
            float x = p.Bx + b.SupportDx;
            if (x >= p.Left - b.R * 0.3f && x <= p.Right + b.R * 0.3f)
            {
                b.X = Math.Clamp(x, b.R, W - b.R);
                return;
            }
        }
        b.SupportId = -1;
        if (b.Man != null) SayMan(b, Pick(new[] { "А-а-а!", "Куда окно делось?!", "Падаю!" }));
    }

    /// <summary>A falling ball that crosses a window cap lands on it.</summary>
    private bool CatchOnWindow(Ball b, float prevY)
    {
        if (!S.SnowOnWindows || Time < b.NoCatchUntil) return false;
        WindowPile? best = null;
        float bestS = float.MaxValue;
        float r = b.R * 0.85f;
        foreach (var p in PileOrder)
        {
            if (!p.Contains(b.X) || !p.VisibleAt(b.X)) continue;
            float s = p.SurfaceY(b.X);
            if (prevY + r <= s + 1 && b.Y + r >= s && s < bestS)
            {
                best = p as WindowPile;
                bestS = s;
            }
        }
        if (best == null) return false;
        b.SupportId = best.Id;
        b.SupportDx = b.X - best.Bx;
        return true;
    }

    private static readonly string[] FallLines = { "Уаа!", "А-а-а!", "Держите меня!", "Лечу!" };
    private static readonly string[] BrokenLines = { "Нееет!", "Он разбился...", "Слепи его заново!", "Покойся с миром, снежок" };

    /// <summary>The window moved out from under its snowmen: they keep its last speed and fall.</summary>
    private void ShakeOff(int windowId, float vx)
    {
        foreach (var root in Balls.Where(b => b.Below == null && b.SupportId == windowId && !b.Held).ToList())
        {
            root.SupportId = -1;
            root.NoCatchUntil = Time + 1.2f;
            root.VX = Math.Clamp(vx * 0.6f, -900, 900);
            root.VY = -Rand(80, 200);
            root.Y -= 1;
            if (root.Man != null) SayMan(root, Pick(FallLines), 1.5f);
        }
    }

    /// <summary>A snowman (or a ball) that hit the ground too hard breaks into snow.</summary>
    private void Shatter(Ball root)
    {
        float x = root.X, y = root.Y;
        if (root.Man != null && !root.Man.HatOff) KnockHat(root, Rand(-150, 150), -Rand(200, 320));
        foreach (var b in root.StackFromRoot().ToList())
        {
            b.VX = Rand(-60, 60);
            Splat(b);
            Balls.Remove(b);
        }
        Powder(x, y, 16, 140, 160);
        Say(Pick(new[] { "Бдыщ!", "Хрусь!", "Бум!" }), () => new SKPoint(x, y - 30), 1.5f);
        if (root.Man != null) React(BrokenLines, 0.6f);
    }

    // ------------------------------------------------------------------ snowman tricks

    private Ball? _pendingDetach, _tapRoot;
    private float _pressX, _pressY, _pressT;

    private static readonly string[] Names =
    {
        "Валера", "Снежок", "Пломбир", "Кефир", "Борис", "Сугробыч", "Морозко", "Геннадий", "Пельмень", "Витя",
        "Хрустик", "Тимлид", "Сеня", "Пончик", "Аркадий", "Зефир", "Тоша", "Буран",
    };

    private static readonly string[] TapLines = { "Хи-хи!", "Щекотно!", "Ой!", "Чего?", "Ещё!", "Не тыкай!" };
    private static readonly string[] DanceLines = { "Jingle bells!", "Ля-ля-ля!", "Диско!", "Танцуют все!", "Хо-хо-хо!" };
    private static readonly string[] Chorus = { "Jingle bells!", "Jingle bells!", "Jingle all the way!", "Ля-ля-ля!", "Хо-хо-хо!", "С Новым годом!" };

    internal void OnTap(Ball root)
    {
        var m = root.Man;
        if (m == null) return;
        if (Time - m.LastTap > 1.2f) m.Taps = 0;
        m.Taps++;
        m.LastTap = Time;
        m.HopT = 0.3f;
        ScareBirdsOn(root);
        if (m.Taps == 1 && Rng.NextDouble() < 0.5) SayMan(root, Pick(TapLines), 1.5f);
        if (m.Taps < 5) return;

        m.Taps = 0;
        var men = Snowmen().OrderBy(b => b.X).ToList();
        if (men.Count >= 3)
        {
            // the whole gang sings, one line each
            foreach (var r in men) r.Man!.DanceT = 9;
            for (int i = 0; i < Chorus.Length; i++)
            {
                var singer = men[i % men.Count];
                string line = Chorus[i];
                Later(i * 1.3f, () =>
                {
                    if (singer.Man != null && Balls.Contains(singer)) SayMan(singer, line, 1.6f);
                });
            }
            Later(Chorus.Length * 1.3f, () =>
            {
                foreach (var r in Snowmen()) Confetti(r.Top.X, r.Top.Y - r.Top.R, 25);
            });
        }
        else
        {
            m.DanceT = 6;
            SayMan(root, Pick(DanceLines), 3);
        }
    }

    /// <summary>How high a snowman jumps right now (single hop on a tap, bouncing while dancing).</summary>
    private static float Lift(Ball root)
    {
        var m = root.Man;
        if (m == null) return 0;
        float lift = 0;
        if (m.HopT > 0) lift = MathF.Sin(MathF.PI * (1 - m.HopT / 0.3f)) * root.R * 0.25f;
        if (m.DanceT > 0) lift = Math.Max(lift, MathF.Abs(MathF.Sin(m.DanceT * 7)) * root.R * 0.22f);
        return lift;
    }

    private static readonly SKColor[] NoteColors = { new(255, 90, 90), new(255, 210, 80), new(110, 220, 140), new(120, 180, 255), new(230, 130, 255) };

    private void UpdateTricks(Ball root, Snowman m, float dt)
    {
        m.HopT = Math.Max(0, m.HopT - dt);
        if (m.DanceT <= 0) return;
        m.DanceT -= dt;
        m.Wave = 1;
        var head = root.Top;
        if (Rng.NextDouble() < dt * 3)
            Spawn(PKind.Note, head.X + Rand(-head.R, head.R), head.Y - head.R * 1.4f, Rand(-20, 20), Rand(-55, -30), 5, 2.2f,
                NoteColors[Rng.Next(NoteColors.Length)]);
    }

    private void MeltSnowmen(float dt)
    {
        for (int i = Balls.Count - 1; i >= 0; i--)
        {
            var b = Balls[i];
            if (b.Flying || b.Held) continue;
            b.R = Math.Max(0, b.R - dt * 0.02f);
        }
        foreach (var root in Balls.Where(b => b.Below == null && !b.Held && b.StackFromRoot().Any(x => x.R < 5)).ToList())
        {
            foreach (var b in root.StackFromRoot().ToList())
            {
                Powder(b.X, b.Y, 6, b.R * 2 + 20, 60);
                Balls.Remove(b);
            }
        }
    }

    // ------------------------------------------------------------------ hats in the wind

    public readonly List<FlyingHat> Hats = new();

    private static readonly string[] HatLines = { "Шапка!", "Моя шапка!", "Верни шапку, ветер!", "Ой, уши мёрзнут!" };

    internal void KnockHat(Ball root, float vx, float vy)
    {
        var m = root.Man;
        if (m == null || m.HatOff) return;
        var head = root.Top;
        m.HatOff = true;
        Hats.Add(new FlyingHat
        {
            X = head.X, Y = head.Y - head.R * 0.78f, VX = vx, VY = vy, VRot = Rand(-7, 7), R = head.R,
            Kind = m.Hat, Color = m.HatColor, Owner = m,
        });
    }

    private Ball? OwnerRoot(FlyingHat h) =>
        h.Owner == null ? null : Balls.FirstOrDefault(b => b.Below == null && b.Man == h.Owner && !b.Flying);

    private FlyingHat? HitHat(float x, float y)
    {
        foreach (var h in Hats)
            if (!h.Returning && MathF.Abs(x - h.X) < h.R * 0.9f + 4 && y < h.Y + 4 && y > h.Y - h.R * 1.1f - 4)
                return h;
        return null;
    }

    /// <summary>Clicked: the hat flies back to its snowman (or to any snowman missing one).</summary>
    internal void ReturnHat(FlyingHat h)
    {
        var owner = OwnerRoot(h);
        if (owner == null)
        {
            owner = Snowmen().Where(b => b.Man!.HatOff).OrderBy(b => MathF.Abs(b.X - h.X)).FirstOrDefault();
            if (owner == null)
            {
                Sparkles(h.X, h.Y, 12, 50);
                Hats.Remove(h);
                return;
            }
            // adopted: the old owner is gone, so this hat now belongs to this snowman
            foreach (var other in Hats.Where(o => o.Owner == owner.Man && o != h)) other.Owner = null;
            h.Owner = owner.Man;
            owner.Man!.Hat = h.Kind;
            owner.Man.HatColor = h.Color;
        }
        h.Returning = true;
        h.RetT = 0;
        h.SX = h.X;
        h.SY = h.Y;
    }

    private void UpdateHats(float dt)
    {
        // strong gusts take hats (beanies hold on)
        if (MathF.Abs(Wind) > 55)
            foreach (var root in Snowmen().ToList())
                if (root.Man is { HatOff: false, Hat: not 2 } && Rng.NextDouble() < dt * 0.08)
                {
                    KnockHat(root, Wind * 1.4f, -Rand(120, 220));
                    SayMan(root, Pick(HatLines));
                }

        for (int i = Hats.Count - 1; i >= 0; i--)
        {
            var h = Hats[i];
            h.Age += dt;

            if (h.Returning)
            {
                var root = OwnerRoot(h);
                if (root == null)
                {
                    h.Returning = false;
                    h.Landed = false;
                    continue;
                }
                var head = root.Top;
                float tx = head.X, ty = head.Y - head.R * 0.78f;
                h.RetT = Math.Min(1, h.RetT + dt / 0.8f);
                float t = h.RetT;
                h.X = h.SX + (tx - h.SX) * t;
                h.Y = h.SY + (ty - h.SY) * t - MathF.Sin(t * MathF.PI) * 80;
                h.Rot *= 1 - Math.Min(1, dt * 6);
                if (t >= 1)
                {
                    root.Man!.HatOff = false;
                    Sparkles(tx, ty, 12, 50);
                    if (Rng.NextDouble() < 0.6) SayMan(root, Pick(new[] { "Спасибо!", "Шапка! Родная!", "Тепло!" }));
                    Hats.RemoveAt(i);
                }
                continue;
            }

            if (!h.Landed)
            {
                float py = h.Y;
                h.VY = Math.Min(h.VY + 520 * dt, 260);
                h.VX += (Wind * 1.3f - h.VX) * Math.Min(1, 1.2f * dt);
                h.X += h.VX * dt;
                h.Y += h.VY * dt;
                h.Rot += h.VRot * dt;

                if (h.X < -60 || h.X > W + 60)
                {
                    // blown away: it comes back by itself a bit later
                    var owner = h.Owner;
                    Hats.RemoveAt(i);
                    Later(Rand(20, 35), () =>
                    {
                        var r = Balls.FirstOrDefault(b => b.Below == null && b.Man == owner);
                        if (r?.Man is not { HatOff: true }) return;
                        r.Man.HatOff = false;
                        Sparkles(r.Top.X, r.Top.Y - r.Top.R, 16, 60);
                        SayMan(r, "Шапка вернулась!");
                    });
                    continue;
                }

                if (h.VY > 0 && S.SnowOnWindows)
                    foreach (var p in PileOrder)
                    {
                        if (!p.Contains(h.X) || !p.VisibleAt(h.X)) continue;
                        float s = p.SurfaceY(h.X);
                        if (py <= s && h.Y >= s)
                        {
                            h.Landed = true;
                            h.OnPile = p as WindowPile;
                            h.Y = s;
                            break;
                        }
                    }
                if (!h.Landed && h.Y >= Ground.SurfaceY(h.X))
                {
                    h.Landed = true;
                    h.OnPile = null;
                }
                if (h.Landed)
                {
                    h.Rot = Rand(-0.35f, 0.35f);
                    h.Age = 0;
                    Powder(h.X, h.Y, 4, 40, 40);
                }
                continue;
            }

            // lying in the snow
            if (h.OnPile != null)
            {
                if (!WinPiles.TryGetValue(h.OnPile.Id, out var p) || p != h.OnPile || !p.VisibleAt(h.X))
                {
                    h.Landed = false;
                    h.VY = 0;
                    continue;
                }
                h.Y = p.SurfaceY(h.X);
            }
            else h.Y = Ground.SurfaceY(h.X);

            if (MathF.Abs(Wind) > 60 && Rng.NextDouble() < dt * 0.3)
            {
                h.Landed = false;
                h.VY = -Rand(80, 160);
                h.VX = Wind;
                continue;
            }

            var ownerRoot = OwnerRoot(h);
            if (ownerRoot != null && h.Age > 90) ReturnHat(h);
            else if (ownerRoot == null && h.Age > 120)
            {
                Sparkles(h.X, h.Y, 8, 40);
                Hats.RemoveAt(i);
            }
        }
    }

    // ------------------------------------------------------------------ icicles

    private void UpdateIcicles(float dt)
    {
        if (!S.Icicles)
        {
            foreach (var p in PileOrder) p.Icicles.Clear();
            return;
        }

        // keep clear of the window buttons (left on macOS, right on Windows)
        bool win = OperatingSystem.IsWindows();
        float padL = win ? 20 : 90, padR = win ? 150 : 24;

        foreach (var p in PileOrder)
        {
            p.IcicleT -= dt;
            if (p.IcicleT <= 0)
            {
                p.IcicleT = Rand(5, 15);
                float x = Rand(p.Left + padL, p.Right - padR);
                if (x > p.Left + padL && p.Icicles.Count < (int)((p.Right - p.Left) / 70) && p.HeightAt(x) > 4 &&
                    p.Icicles.All(ic => MathF.Abs(ic.X - x) > 14))
                    p.Icicles.Add(new Icicle { X = x, Len = 0.5f, MaxLen = Rand(6, 16), Grow = Rand(0.05f, 0.14f), DripT = Rand(5, 30) });
            }

            for (int i = p.Icicles.Count - 1; i >= 0; i--)
            {
                var ic = p.Icicles[i];
                if (p.HeightAt(ic.X) > 2.5f) ic.Len = Math.Min(ic.MaxLen, ic.Len + ic.Grow * dt * (S.Melt ? 2 : 1));
                else ic.Len -= dt * 1.5f;
                if (ic.Len <= 0)
                {
                    p.Icicles.RemoveAt(i);
                    continue;
                }
                ic.DripT -= dt;
                if (ic.DripT <= 0)
                {
                    ic.DripT = S.Melt ? Rand(1.5f, 4) : Rand(15, 45);
                    if (ic.Len > 4 && p.VisibleAt(ic.X))
                        Spawn(PKind.Drop, ic.X, p.BaseY + ic.Len, 0, 20, 1.3f, 3, new SKColor(170, 210, 255));
                }
            }
        }
    }

    private void BreakIcicle(WindowPile p, Icicle ic, float vx)
    {
        p.Icicles.Remove(ic);
        Spawn(PKind.Shard, ic.X, p.BaseY + ic.Len * 0.5f, vx, Rand(-40, 20), ic.Len, 4, new SKColor(215, 235, 255));
        Sparkles(ic.X, p.BaseY + 2, 4, 30);
    }

    private void DropIcicles(WindowPile p, float vxBias)
    {
        foreach (var ic in p.Icicles.ToList()) BreakIcicle(p, ic, vxBias + Rand(-20, 20));
    }

    /// <summary>Swiping the cursor through an icicle knocks it down.</summary>
    private void PokeIcicles(float vx)
    {
        foreach (var p in PileOrder)
        {
            if (My < p.BaseY - 2 || My > p.BaseY + 18) continue;
            foreach (var ic in p.Icicles.ToList())
                if (MathF.Abs(Mx - ic.X) < 5 && My <= p.BaseY + ic.Len + 2 && p.VisibleAt(ic.X))
                    BreakIcicle(p, ic, vx * 0.15f);
        }
    }

    // ------------------------------------------------------------------ wind: drifting snow

    private void Spindrift(float dt)
    {
        float strength = Math.Min(1, (MathF.Abs(Wind) - 45) / 35);
        if (strength <= 0) return;
        int dir = Wind > 0 ? 1 : -1;

        // low streaks of powder over the ground drift; a little snow moves downwind
        int n = (int)(strength * 4 * W / 1500 + Rng.NextDouble());
        for (int k = 0; k < n; k++)
        {
            float x = Rand(0, W);
            if (Ground.HeightAt(x) < 3) continue;
            float y = Ground.SurfaceY(x);
            Spawn(PKind.Powder, x, y - Rand(0, 3), Wind * Rand(1.2f, 2f), -Rand(5, 30), Rand(0.6f, 1.6f), Rand(0.6f, 1.4f), SKColors.White);
            float taken = Ground.Take(x - 2, x + 2, 0.15f);
            Ground.Add(Math.Clamp(x + dir * Rand(20, 60), 0, W - 1), taken);
        }

        // window caps lose snow into the wind
        foreach (var p in PileOrder)
        {
            if (Rng.NextDouble() > strength * dt * 6) continue;
            float x = Rand(p.Left, p.Right);
            if (!p.VisibleAt(x) || p.HeightAt(x) < 3) continue;
            p.Take(x - 3, x + 3, 0.5f);
            for (int k = 0; k < 3; k++)
                Spawn(PKind.Powder, x, p.SurfaceY(x), Wind * Rand(1.3f, 2f), -Rand(10, 50), Rand(0.6f, 1.5f), Rand(0.7f, 1.5f), SKColors.White);
        }
    }

    // ------------------------------------------------------------------ snowmen defend the presents

    private static readonly string[] ThrowLines = { "Получай!", "За морковку!", "Огонь!", "Снежком его!", "Лови!" };

    private void SnowmenDefend(float dt)
    {
        var g = Grinch;
        if (!g.Active || g.Reformed) return;
        float feet = Ground.SurfaceY(g.X);
        foreach (var root in Snowmen().ToList())
        {
            var m = root.Man!;
            m.ThrowCd -= dt;
            if (m.ThrowCd > 0) continue;
            float dist = g.X - root.X, ad = MathF.Abs(dist);
            if (ad < 90 || ad > 480 || Rng.NextDouble() > dt * 1.2) continue;

            m.ThrowCd = Rand(1.4f, 3f);
            var body = root.Above ?? root;
            int side = MathF.Sign(dist);
            float x0 = body.X + side * (body.R + 12), y0 = body.Y - body.R * 0.6f;
            float t = Math.Clamp(ad / 520f, 0.35f, 0.85f);
            float speed = g.State switch { GrinchState.Escape => 280, GrinchState.Run => 170, _ => 0 };
            float tx = g.X + g.Dir * speed * t + Rand(-30, 30), ty = feet - 55;
            Balls.Add(new Ball { X = x0, Y = y0, R = 6, Flying = true, VX = (tx - x0) / t, VY = (ty - y0 - 0.5f * 900 * t * t) / t });
            m.Wave = 1;
            if (Rng.NextDouble() < 0.4) SayMan(root, Pick(ThrowLines), 1.5f);
        }
    }

    private void Reform()
    {
        var g = Grinch;
        g.Reformed = true;
        g.HasPresent = g.HasCarrot = false;
        float feet = Ground.SurfaceY(g.X);
        for (int i = 0; i < 12; i++)
            Spawn(PKind.Heart, g.X + Rand(-20, 20), feet - 90, Rand(-40, 40), Rand(-70, -30), Rand(4, 7), Rand(1.5f, 2.5f), new SKColor(255, 90, 130));
        Say("Кажется, моё сердце подросло...", GrinchAnchor, 3);
        React(new[] { "Он исправился!", "Чудо!", "Гринч, ты чего?" }, 0.8f);
        Later(2.6f, () =>
        {
            if (!Grinch.Active) return;
            DropPresent(Grinch.X, Ground.SurfaceY(Grinch.X) - 80, -Grinch.Dir * 40);
            Say("Ладно... С Новым годом!", GrinchAnchor, 3);
        });
    }

    // ------------------------------------------------------------------ window shake = earthquake

    private sealed class ShakeTrack
    {
        public float X;
        public int Sign;
        public readonly List<float> Flips = new();

        // position at the previous poll, for the window's speed
        public float PX, PY, PT, LastDx;
    }

    private readonly Dictionary<int, ShakeTrack> _shake = new();
    private float _quakeCd;

    private static readonly string[] QuakeLines = { "Землетрясение!", "Кто трясёт?!", "Эй, не тряси окно!", "Укачало...", "Держитесь!" };

    private void TrackShake(IReadOnlyCollection<(int id, float x, float y, float w, float h)> rects)
    {
        foreach (var (id, x, y, w, _) in rects)
        {
            if (!_shake.TryGetValue(id, out var st))
            {
                _shake[id] = new ShakeTrack { X = x, PX = x, PY = y, PT = Time };
                continue;
            }

            // a jerk (sharp change of direction), a yank down or a very fast move throws snowmen off the window
            float pdt = Math.Max(0.05f, Time - st.PT);
            float mdx = x - st.PX, mdy = y - st.PY;
            bool jerk = MathF.Abs(mdx) > 12 && MathF.Abs(st.LastDx) > 12 && MathF.Sign(mdx) != MathF.Sign(st.LastDx);
            float vx = mdx / pdt, vy = mdy / pdt;
            if (jerk || vy > 900 || vx * vx + vy * vy > 2600f * 2600f)
                ShakeOff(id, jerk ? st.LastDx / pdt : vx);
            st.LastDx = mdx;
            st.PX = x;
            st.PY = y;
            st.PT = Time;

            float dx = x - st.X;
            if (MathF.Abs(dx) < 4) continue;
            int sign = Math.Sign(dx);
            if (st.Sign != 0 && sign != st.Sign)
            {
                st.Flips.Add(Time);
                st.Flips.RemoveAll(t => Time - t > 1.6f);
                if (st.Flips.Count >= 4)
                {
                    st.Flips.Clear();
                    Quake(x + w / 2, y, id);
                }
            }
            st.Sign = sign;
            st.X = x;
        }
        if (_shake.Count > rects.Count + 8)
        {
            var alive = rects.Select(r => r.id).ToHashSet();
            foreach (var id in _shake.Keys.Where(k => !alive.Contains(k)).ToList()) _shake.Remove(id);
        }
    }

    internal void Quake(float x, float y, int windowId = -1)
    {
        if (_quakeCd > 0) return;
        _quakeCd = 6;
        // whoever stands on the shaken window is thrown off
        foreach (var root in Balls.Where(b => b.Below == null && b.SupportId == windowId && windowId >= 0).ToList())
        {
            root.SupportId = -1;
            root.VY = -Rand(150, 250);
            root.Y -= 1;
            if (root.Man != null) SayMan(root, "А-а-а!");
        }
        foreach (var root in Snowmen().ToList())
        {
            root.Man!.HopT = 0.3f;
            if (Rng.NextDouble() < 0.4) KnockHat(root, Rand(-120, 120), -Rand(150, 250));
        }
        for (int k = 0; k < 24; k++)
        {
            float gx = Rand(0, W);
            if (Ground.HeightAt(gx) > 3) Powder(gx, Ground.SurfaceY(gx), 3, 50, 120);
        }
        foreach (var p in Presents)
        {
            p.Landed = false;
            p.VY = -Rand(150, 260);
            p.VX = Rand(-60, 60);
        }
        foreach (var b in Birds) Scare(b);
        if (Snowmen().Any()) React(QuakeLines, 1);
        else Say(Pick(QuakeLines), () => new SKPoint(x, y - 30), 2);
    }

    // ------------------------------------------------------------------ bullfinches

    public readonly List<Bird> Birds = new();
    private float _nextBirds = 45;

    public void SpawnFlock()
    {
        int side = Rng.Next(2) == 0 ? -1 : 1;
        int n = Rng.Next(1, 5);
        for (int i = 0; i < n && Birds.Count < 7; i++)
        {
            var b = new Bird
            {
                X = side < 0 ? -20 - i * 25 : W + 20 + i * 25,
                Y = MenuBarH + Rand(40, Math.Max(60, H * 0.3f)),
                State = BirdState.Arrive,
                Dir = -side,
            };
            if (!PickPerch(b)) return;
            Birds.Add(b);
        }
    }

    /// <summary>Chooses a free spot: a visible window cap, a snowman's head or the ground drift.</summary>
    private bool PickPerch(Bird b)
    {
        b.Pile = null;
        b.Head = null;
        double r = Rng.NextDouble();
        var heads = Snowmen().Where(s => Birds.All(o => o.Head != s.Top)).ToList();
        if (r < 0.35 && heads.Count > 0)
        {
            b.Head = heads[Rng.Next(heads.Count)].Top;
            b.PerchX = Rand(-0.3f, 0.3f);
            return true;
        }
        if (r < 0.8 && PileOrder.Count > 0)
        {
            for (int tries = 0; tries < 8; tries++)
            {
                var p = PileOrder[Rng.Next(PileOrder.Count)];
                float x = Rand(p.Left + 10, p.Right - 10);
                if (!p.VisibleAt(x) || p.HeightAt(x) < 1.5f) continue;
                b.Pile = p;
                b.PerchX = x - p.Left;
                return true;
            }
        }
        b.PerchX = Rand(W * 0.05f, W * 0.95f);
        return true;
    }

    /// <summary>Where the bird's feet go, or null when the perch is gone.</summary>
    private SKPoint? PerchPoint(Bird b)
    {
        if (b.Head != null)
        {
            var root = b.Head.Root;
            if (!Balls.Contains(b.Head) || root.Man == null || b.Head.Held || root.Held || b.Head.Above != null) return null;
            var m = root.Man;
            float hatH = m.HatOff ? 0 : m.Hat switch { 0 => 1.12f, 1 => 0.85f, 2 => 0.6f, 4 => 0.75f, _ => 0.3f };
            return new SKPoint(b.Head.X + b.PerchX * b.Head.R, b.Head.Y - b.Head.R * 0.9f - hatH * b.Head.R);
        }
        if (b.Pile != null)
        {
            if (!WinPiles.TryGetValue(b.Pile.Id, out var p) || p != b.Pile) return null;
            float x = p.Left + b.PerchX;
            if (!p.VisibleAt(x)) return null;
            return new SKPoint(x, p.SurfaceY(x));
        }
        // on the drift, or on top of a ball lying there
        float y = Ground.SurfaceY(b.PerchX);
        foreach (var root in Balls)
            if (root.Below == null && !root.Flying && MathF.Abs(root.X - b.PerchX) < root.R * 0.8f)
                y = Math.Min(y, root.Top.Y - root.Top.R * 0.95f);
        return new SKPoint(b.PerchX, y);
    }

    private void Scare(Bird b, float fromX = float.NaN)
    {
        if (b.State == BirdState.Leave) return;
        b.State = BirdState.Leave;
        b.Dir = float.IsNaN(fromX) ? (b.X < W / 2 ? -1 : 1) : (b.X >= fromX ? 1 : -1);
        b.VY = -Rand(120, 200);
        b.VX = b.Dir * Rand(60, 120);
    }

    private void ScareBirdsOn(Ball root)
    {
        foreach (var b in Birds)
            if (b.Head != null && b.Head.Root == root) Scare(b, root.X);
    }

    private void UpdateBirds(float dt)
    {
        if (S.Birds)
        {
            _nextBirds -= dt;
            if (_nextBirds <= 0)
            {
                _nextBirds = Rand(70, 170);
                SpawnFlock();
            }
        }

        for (int i = Birds.Count - 1; i >= 0; i--)
        {
            var b = Birds[i];
            b.T += dt;
            switch (b.State)
            {
                case BirdState.Arrive:
                {
                    b.Flap += dt * 22;
                    var target = PerchPoint(b);
                    if (target == null)
                    {
                        if (!PickPerch(b)) Scare(b);
                        break;
                    }
                    float dx = target.Value.X - b.X, dy = target.Value.Y - b.Y;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist < 4)
                    {
                        b.State = BirdState.Perch;
                        b.T = 0;
                        b.Stay = Rand(10, 28);
                        b.VX = b.VY = 0;
                        break;
                    }
                    float speed = Math.Min(170, dist * 3 + 20);
                    float k = Math.Min(1, dt * 4);
                    b.VX += (dx / dist * speed - b.VX) * k;
                    b.VY += (dy / dist * speed + MathF.Sin(b.T * 9) * 25 - b.VY) * k;
                    b.X += b.VX * dt;
                    b.Y += b.VY * dt;
                    if (MathF.Abs(b.VX) > 5) b.Dir = b.VX > 0 ? 1 : -1;
                    break;
                }
                case BirdState.Perch:
                {
                    var target = PerchPoint(b);
                    if (target == null)
                    {
                        Scare(b);
                        break;
                    }
                    b.X = target.Value.X;
                    b.Y = target.Value.Y;
                    b.Flap = 0;
                    b.HopT = Math.Max(0, b.HopT - dt);
                    b.Peck = Math.Max(0, b.Peck - dt);
                    if (Rng.NextDouble() < dt * 0.5) b.Peck = 0.35f;
                    if (Rng.NextDouble() < dt * 0.25 && b.HopT <= 0)
                    {
                        b.HopT = 0.25f;
                        float step = Rand(-12, 12);
                        if (b.Head != null) b.PerchX = Math.Clamp(b.PerchX + step / Math.Max(10, b.Head.R), -0.35f, 0.35f);
                        else if (b.Pile != null) b.PerchX = Math.Clamp(b.PerchX + step, 6, b.Pile.Right - b.Pile.Left - 6);
                        else b.PerchX = Math.Clamp(b.PerchX + step, 5, W - 5);
                        b.Dir = step > 0 ? 1 : -1;
                    }
                    if (Rng.NextDouble() < dt * 0.015) Say(Pick(new[] { "Чик-чирик!", "Фьюить!", "Чирик." }), () => new SKPoint(b.X, b.Y - 16), 1.5f);

                    bool scared = MouseInside && MathF.Abs(Mx - b.X) < 90 && MathF.Abs(My - b.Y) < 90;
                    scared |= Tractor.Active && MathF.Abs(Tractor.X - b.X) < 220 && b.Pile == null;
                    scared |= Grinch.Active && MathF.Abs(Grinch.X - b.X) < 160;
                    if (scared) Scare(b, MouseInside ? Mx : b.X - b.Dir);
                    else if (b.T > b.Stay)
                    {
                        if (Rng.NextDouble() < 0.4 && PickPerch(b)) b.State = BirdState.Arrive;
                        else Scare(b);
                    }
                    break;
                }
                case BirdState.Leave:
                    b.Flap += dt * 26;
                    b.VX += b.Dir * 260 * dt;
                    b.VY = Math.Max(-220, b.VY - 120 * dt);
                    b.X += b.VX * dt;
                    b.Y += b.VY * dt;
                    if (b.X < -40 || b.X > W + 40 || b.Y < -40) Birds.RemoveAt(i);
                    break;
            }
        }
    }

    // ------------------------------------------------------------------ aurora

    /// <summary>0..1 brightness of the northern lights.</summary>
    public float Aurora;
    private float _auroraT, _nextAurora = 900;

    private static readonly string[] AuroraLines = { "Северное сияние!", "Как красиво...", "Смотри наверх!" };

    public void StartAurora(float seconds = 50)
    {
        _auroraT = seconds;
        React(AuroraLines, 0.7f);
    }

    private void UpdateAurora(float dt)
    {
        if (S.Aurora)
        {
            int hour = DateTime.Now.Hour;
            if (hour >= 21 || hour < 4)
            {
                _nextAurora -= dt;
                if (_nextAurora <= 0)
                {
                    _nextAurora = Rand(1200, 2700);
                    StartAurora(Rand(40, 80));
                }
            }
        }

        if (_auroraT > 0)
        {
            _auroraT -= dt;
            Aurora = Math.Min(Math.Min(1, Aurora + dt / 6), Math.Max(0, _auroraT / 6));
        }
        else Aurora = Math.Max(0, Aurora - dt / 6);
    }
}
