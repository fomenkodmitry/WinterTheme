using SkiaSharp;
using WinterTheme.Sim;

namespace WinterTheme.Render;

/// <summary>Draws a <see cref="World"/> with SkiaSharp. One instance per overlay (paints are not shared across threads).</summary>
public sealed class Painter
{
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKFont _font = new(SKTypeface.FromFamilyName("Helvetica Neue", SKFontStyle.Bold), 13);
    private readonly SKFont _hintFont = new(SKTypeface.FromFamilyName("Helvetica Neue", SKFontStyle.Normal), 12);

    private readonly SKPaint _measure = new() { IsAntialias = true };

    private float Measure(SKFont font, string text)
    {
        _measure.Typeface = font.Typeface;
        _measure.TextSize = font.Size;
        return _measure.MeasureText(text);
    }
    private static SKColor A(SKColor c, float a) => c.WithAlpha((byte)Math.Clamp(a * 255, 0, 255));

    private static readonly SKColor Snow = new(255, 255, 255);
    private static readonly SKColor SnowShade = new(214, 228, 243);
    private static readonly SKColor Coal = new(30, 30, 34);

    public void Draw(SKCanvas c, World w)
    {
        Diag.Begin();
        c.Clear(SKColors.Transparent);
        if (w.Aurora > 0.01f) DrawAurora(c, w);
        DrawFlakes(c, w, far: true);
        Diag.Mark(0);
        if (w.Santa.Active) DrawSanta(c, w);
        Diag.Mark(1);
        foreach (var p in w.PileOrder)
        {
            DrawPile(c, p, true, w.Time, 15);
            if (p.Icicles.Count > 0) DrawIcicles(c, p);
        }
        Diag.Mark(2);
        if (Settings.Current.Garland) DrawGarland(c, w);
        Diag.Mark(3);
        DrawPile(c, w.Ground, false, w.Time, 60);
        Diag.Mark(4);
        foreach (var p in w.Presents) DrawPresent(c, p);
        if (w.Tractor.Active) DrawTractor(c, w);
        Diag.Mark(5);
        DrawBalls(c, w);
        Diag.Mark(6);
        foreach (var h in w.Hats) DrawFlyingHat(c, h);
        foreach (var b in w.Birds) DrawBird(c, b);
        if (w.Grinch.Active) DrawGrinch(c, w);
        Diag.Mark(7);
        DrawParticles(c, w);
        Diag.Mark(8);
        DrawFlakes(c, w, far: false);
        Diag.Mark(9);
        DrawBubbles(c, w);
        DrawHint(c, w);
        Diag.Mark(10);
    }

    // ------------------------------------------------------------------ snow

    // ------------------------------------------------------------------ sprite atlas
    // Flakes and garland bulbs are small sprites drawn with DrawAtlas: one native call (and one GPU draw)
    // for hundreds of them, instead of one call per circle, line and gradient.

    private const int BulbBox = 48; // px; a bulb sprite covers 24x24 pt around the bulb at 2x
    private static readonly int[] DotPx = { 4, 8, 16 };
    private static readonly SKRect[] DotRects = { new(0, 0, 4, 4), new(4, 0, 12, 8), new(12, 0, 28, 16) };
    private static readonly SKRect StarRect = new(28, 0, 60, 32);
    private const float StarR = 13; // px, spoke length in the star sprite
    private static readonly SKRect GlintRect = new(60, 0, 92, 32);
    private const float GlintR = 13; // px, arm length of the glint sprite
    private SKImage? _atlas;
    private readonly SKPaint _atlasPaint = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Low };

    private SKRect BulbRect(int color, bool lit) =>
        SKRect.Create(color * BulbBox, 32 + (lit ? 0 : BulbBox), BulbBox, BulbBox);

    private SKImage Atlas()
    {
        if (_atlas != null) return _atlas;
        int w = Math.Max(96, Bulbs.Length * BulbBox), h = 32 + 2 * BulbBox;
        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(SKColors.Transparent);
        using var p = new SKPaint { IsAntialias = true, Color = SKColors.White };

        for (int i = 0; i < DotRects.Length; i++)
            c.DrawCircle(DotRects[i].MidX, DotRects[i].MidY, DotPx[i] / 2f - 0.5f, p);

        // three spokes and a dot, like the big near flakes
        p.Style = SKPaintStyle.Stroke;
        p.StrokeCap = SKStrokeCap.Round;
        p.StrokeWidth = 2.4f;
        float cx = StarRect.MidX, cy = StarRect.MidY;
        for (int k = 0; k < 3; k++)
        {
            float a = k * MathF.PI / 3;
            c.DrawLine(cx - MathF.Cos(a) * StarR, cy - MathF.Sin(a) * StarR, cx + MathF.Cos(a) * StarR, cy + MathF.Sin(a) * StarR, p);
        }
        p.Style = SKPaintStyle.Fill;
        c.DrawCircle(cx, cy, 3.25f, p);

        // glint on the snow: a small cross with a bluish dot
        p.Style = SKPaintStyle.Stroke;
        p.StrokeWidth = 2.2f;
        p.Color = new SKColor(255, 255, 255, 230);
        float gx = GlintRect.MidX, gy = GlintRect.MidY;
        c.DrawLine(gx - GlintR, gy, gx + GlintR, gy, p);
        c.DrawLine(gx, gy - GlintR, gx, gy + GlintR, p);
        p.Style = SKPaintStyle.Fill;
        p.Color = new SKColor(190, 220, 255, 220);
        c.DrawCircle(gx, gy, 2.6f, p);
        p.Color = SKColors.White;

        // bulbs, drawn at 2x around (bx, by) = top of the bulb socket
        for (int i = 0; i < Bulbs.Length; i++)
        {
            foreach (bool lit in new[] { true, false })
            {
                var r = BulbRect(i, lit);
                c.Save();
                c.Translate(r.MidX, r.Top + BulbBox / 4f);
                c.Scale(2);
                DrawBulbShape(c, p, Bulbs[i], lit);
                c.Restore();
            }
        }
        _atlas = surface.Snapshot();
        return _atlas;
    }

    /// <summary>A bulb at (0, 0); lit = glow and full color, otherwise a dim bulb without glow.</summary>
    private static void DrawBulbShape(SKCanvas c, SKPaint p, SKColor col, bool lit)
    {
        p.Style = SKPaintStyle.Fill;
        if (lit)
        {
            using var glow = SKShader.CreateRadialGradient(new SKPoint(0, 6), 11, new[] { A(col, 0.55f), A(col, 0) }, null, SKShaderTileMode.Clamp);
            p.Shader = glow;
            p.Color = SKColors.White;
            c.DrawCircle(0, 6, 11, p);
            p.Shader = null;
        }
        p.Color = new SKColor(40, 70, 45);
        c.DrawRect(-1.8f, 0, 3.6f, 3, p);
        float k = lit ? 1 : 0.45f;
        p.Color = new SKColor((byte)(col.Red * k), (byte)(col.Green * k), (byte)(col.Blue * k));
        c.DrawOval(0, 6.5f, 2.8f, 4f, p);
        if (lit)
        {
            p.Color = new SKColor(255, 255, 255, 160);
            c.DrawCircle(-0.9f, 5.2f, 0.9f, p);
        }
    }

    private readonly List<SKRect> _aRects = new(1024);
    private readonly List<SKRotationScaleMatrix> _aXforms = new(1024);
    private readonly List<SKColor> _aColors = new(1024);

    private void AtlasBegin()
    {
        _aRects.Clear();
        _aXforms.Clear();
        _aColors.Clear();
    }

    /// <summary>Queues a sprite whose pixel (ax, ay), relative to the sprite's own corner, lands on (x, y).</summary>
    private void AtlasAdd(SKRect sprite, float x, float y, float scale, float radians, float ax, float ay, float alpha)
    {
        _aRects.Add(sprite);
        _aXforms.Add(SKRotationScaleMatrix.Create(scale, radians, x, y, ax, ay));
        _aColors.Add(SKColors.White.WithAlpha((byte)(Math.Clamp(alpha, 0, 1) * 255)));
    }

    private void AtlasFlush(SKCanvas c)
    {
        if (_aRects.Count == 0) return;
        c.DrawAtlas(Atlas(), _aRects.ToArray(), _aXforms.ToArray(), _aColors.ToArray(), SKBlendMode.Modulate, _atlasPaint);
        AtlasBegin();
    }

    private void DrawFlakes(SKCanvas c, World w, bool far)
    {
        float px = Math.Max(1, c.TotalMatrix.ScaleX); // device pixels per point
        AtlasBegin();
        foreach (var f in w.Flakes)
        {
            if (far != f.Depth < 0.4f) continue;
            float a = far ? 0.35f + f.Depth : 0.75f + f.Depth * 0.25f;
            if (!far && f.Size > 3.0f)
            {
                var r = StarRect;
                AtlasAdd(r, f.X, f.Y, f.Size * 1.25f / StarR, f.Spin, r.Width / 2, r.Height / 2, a);
            }
            else
            {
                float d = f.Size * 1.2f; // diameter in points
                int i = 0;
                while (i < DotPx.Length - 1 && DotPx[i] < d * px) i++;
                var r = DotRects[i];
                AtlasAdd(r, f.X, f.Y, d / DotPx[i], 0, r.Width / 2, r.Height / 2, a);
            }
        }
        AtlasFlush(c);
    }

    private sealed class PileCache
    {
        public readonly SKPath Path = new();
        public float[] Snap = Array.Empty<float>();
        public bool[] Vis = Array.Empty<bool>();
        public float BaseY = float.NaN, MaxH;
        public SKShader? Shader;
        public int Age;
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Pile, PileCache> _piles = new();

    /// <summary>
    /// The outline of a pile changes by fractions of a point per frame, so it is rebuilt only every few frames
    /// (or at once after a big change: a ball picking up snow, the tractor, the cursor). Reusing the same SKPath
    /// between rebuilds also lets Skia reuse its GPU tessellation.
    /// </summary>
    private PileCache PileShape(Pile p, bool useVis, float shadeDepth)
    {
        var pc = _piles.GetValue(p, _ => new PileCache());
        int n = p.N;
        pc.Age++;
        bool dirty = pc.Snap.Length != n || pc.BaseY != p.BaseY;
        float limit = pc.Age >= 6 ? 0.4f : 2.5f;
        for (int k = 0; !dirty && k < n; k++)
            dirty = MathF.Abs(p.H[k] - pc.Snap[k]) > limit || (useVis && pc.Vis[k] != p.Visible[k]);
        if (!dirty) return pc;
        pc.Age = 0;

        if (pc.Snap.Length != n)
        {
            pc.Snap = new float[n];
            pc.Vis = new bool[n];
        }
        Array.Copy(p.H, pc.Snap, n);
        Array.Copy(p.Visible, pc.Vis, n);
        pc.BaseY = p.BaseY;

        var path = pc.Path;
        path.Reset();
        int i = 0;
        float maxH = 0;
        while (i < n)
        {
            if (useVis && !p.Visible[i]) { i++; continue; }
            int s = i;
            float runMax = 0;
            while (i < n && (!useVis || p.Visible[i]))
            {
                runMax = Math.Max(runMax, p.H[i]);
                i++;
            }
            if (runMax < 0.35f) continue;
            maxH = Math.Max(maxH, runMax);

            float x0 = p.Left + s * p.BinW, x1 = p.Left + i * p.BinW;
            path.MoveTo(x0, p.BaseY);
            path.LineTo(x0, p.BaseY - p.H[s] * 0.7f);
            float px = p.BinCenter(s), py = p.BaseY - p.H[s];
            path.LineTo(px, py);
            for (int k = s + 1; k < i; k++)
            {
                float cx = p.BinCenter(k), cy = p.BaseY - p.H[k];
                path.QuadTo(px, py, (px + cx) * 0.5f, (py + cy) * 0.5f);
                px = cx;
                py = cy;
            }
            path.LineTo(px, py);
            path.LineTo(x1, p.BaseY - p.H[i - 1] * 0.7f);
            path.LineTo(x1, p.BaseY);
            path.Close();
        }
        pc.MaxH = maxH;
        pc.Shader?.Dispose();
        pc.Shader = path.IsEmpty ? null : SKShader.CreateLinearGradient(
            new SKPoint(0, p.BaseY - maxH), new SKPoint(0, p.BaseY + 1),
            new[] { Snow, Snow, SnowShade }, new[] { 0f, Math.Clamp(1 - shadeDepth / Math.Max(maxH, 1), 0, 0.95f), 1f },
            SKShaderTileMode.Clamp);
        return pc;
    }

    private void DrawPile(SKCanvas c, Pile p, bool useVis, float time, float shadeDepth)
    {
        var pc = PileShape(p, useVis, shadeDepth);
        var path = pc.Path;
        if (path.IsEmpty) return;
        int n = p.N;

        if (useVis)
        {
            // soft drop shadow so snow reads on white windows too (the ground does not need it)
            c.Save();
            c.Translate(0, 1.2f);
            _fill.Color = new SKColor(40, 60, 90, 45);
            c.DrawPath(path, _fill);
            c.Restore();
        }

        // outline of the surface: the same shape shifted up a little, under the snow. A stroke would make Skia
        // re-stroke the long path on the CPU every frame; a fill of the cached path is much cheaper.
        c.Save();
        c.Translate(0, -0.9f);
        _fill.Color = new SKColor(150, 175, 210, 140);
        c.DrawPath(path, _fill);
        c.Restore();

        _fill.Color = SKColors.White; // shader output is multiplied by paint alpha
        _fill.Shader = pc.Shader;
        c.DrawPath(path, _fill);
        _fill.Shader = null;

        // glints, batched through the sprite atlas
        AtlasBegin();
        for (int k = 0; k < n; k++)
        {
            if (p.H[k] < 4 || (useVis && !p.Visible[k])) continue;
            uint h = (uint)(k * 2654435761u);
            if (h % 23 != 0) continue;
            float tw = MathF.Sin(time * 2.2f + h % 100);
            if (tw < 0.55f) continue;
            float gx = p.BinCenter(k), gy = p.BaseY - p.H[k] + 2 + h % 5;
            float r = 2.2f * (tw - 0.5f) * 2;
            AtlasAdd(GlintRect, gx, gy, r / GlintR, 0, GlintRect.Width / 2, GlintRect.Height / 2, 1);
        }
        AtlasFlush(c);
    }

    private void DrawParticles(SKCanvas c, World w)
    {
        foreach (var p in w.Particles)
        {
            float life = Math.Clamp(p.Life / p.MaxLife, 0, 1);
            switch (p.Kind)
            {
                case PKind.Clump:
                    _fill.Color = Snow;
                    c.DrawCircle(p.X, p.Y, p.Size, _fill);
                    _fill.Color = SnowShade;
                    c.DrawCircle(p.X + p.Size * 0.3f, p.Y + p.Size * 0.3f, p.Size * 0.5f, _fill);
                    break;
                case PKind.Powder:
                    _fill.Color = A(Snow, life * 0.9f);
                    c.DrawCircle(p.X, p.Y, p.Size, _fill);
                    break;
                case PKind.Sparkle:
                {
                    float r = p.Size * (0.6f + 0.4f * MathF.Sin(p.Life * 20));
                    _stroke.StrokeWidth = 1;
                    _stroke.Color = A(p.Color, life);
                    c.DrawLine(p.X - r * 1.6f, p.Y, p.X + r * 1.6f, p.Y, _stroke);
                    c.DrawLine(p.X, p.Y - r * 1.6f, p.X, p.Y + r * 1.6f, _stroke);
                    _fill.Color = A(p.Color, life);
                    c.DrawCircle(p.X, p.Y, r * 0.5f, _fill);
                    break;
                }
                case PKind.Smoke:
                    _fill.Color = A(p.Color, life * 0.35f);
                    c.DrawCircle(p.X, p.Y, p.Size, _fill);
                    break;
                case PKind.Confetti:
                    _fill.Color = A(p.Color, Math.Min(1, life * 2));
                    c.Save();
                    c.Translate(p.X, p.Y);
                    c.RotateRadians(p.Rot);
                    c.DrawRect(-p.Size, -p.Size * 0.4f * MathF.Abs(MathF.Cos(p.Rot * 1.7f)), p.Size * 2, p.Size * 0.8f * MathF.Abs(MathF.Cos(p.Rot * 1.7f)) + 0.5f, _fill);
                    c.Restore();
                    break;
                case PKind.Rocket:
                    _stroke.StrokeWidth = 1.6f;
                    _stroke.Color = new SKColor(255, 220, 150, 200);
                    c.DrawLine(p.X, p.Y, p.X - p.VX * 0.03f, p.Y - p.VY * 0.03f, _stroke);
                    _fill.Color = SKColors.White;
                    c.DrawCircle(p.X, p.Y, 1.8f, _fill);
                    break;
                case PKind.Note:
                {
                    var col = A(p.Color, Math.Min(1, life * 1.5f));
                    float nx = p.X + MathF.Sin(p.Rot + p.Life * 4) * 4;
                    _fill.Color = col;
                    c.DrawOval(nx, p.Y, 3.2f, 2.4f, _fill);
                    _stroke.StrokeWidth = 1.4f;
                    _stroke.Color = col;
                    c.DrawLine(nx + 2.8f, p.Y, nx + 2.8f, p.Y - 11, _stroke);
                    c.DrawLine(nx + 2.8f, p.Y - 11, nx + 7, p.Y - 7, _stroke);
                    break;
                }
                case PKind.Heart:
                    DrawHeart(c, p.X, p.Y, p.Size, A(p.Color, Math.Min(1, life * 1.5f)));
                    break;
                case PKind.Drop:
                    _fill.Color = A(p.Color, 0.85f);
                    c.DrawOval(p.X, p.Y, 1.3f, 2f, _fill);
                    break;
                case PKind.Shard:
                {
                    c.Save();
                    c.Translate(p.X, p.Y);
                    c.RotateRadians(p.Rot);
                    using var shard = new SKPath();
                    float half = p.Size * 0.5f, wdt = 1.5f + p.Size * 0.12f;
                    shard.MoveTo(-wdt, -half);
                    shard.LineTo(wdt, -half);
                    shard.LineTo(0, half);
                    shard.Close();
                    _fill.Color = A(p.Color, 0.9f);
                    c.DrawPath(shard, _fill);
                    c.Restore();
                    break;
                }
                case PKind.Star:
                {
                    float tx = p.X - p.VX * 0.14f, ty = p.Y - p.VY * 0.14f;
                    using var sh = SKShader.CreateLinearGradient(new SKPoint(p.X, p.Y), new SKPoint(tx, ty),
                        new[] { A(SKColors.White, life), A(new SKColor(180, 210, 255), 0) }, null, SKShaderTileMode.Clamp);
                    _stroke.StrokeWidth = 2;
                    _stroke.Color = SKColors.White; // shader output is multiplied by paint alpha
                    _stroke.Shader = sh;
                    c.DrawLine(p.X, p.Y, tx, ty, _stroke);
                    _stroke.Shader = null;
                    _fill.Color = A(SKColors.White, life);
                    c.DrawCircle(p.X, p.Y, 1.8f, _fill);
                    break;
                }
            }
        }
    }

    // ------------------------------------------------------------------ garland

    private static readonly SKColor[] Bulbs =
    {
        new(255, 70, 70), new(255, 200, 60), new(70, 220, 110), new(80, 160, 255), new(255, 110, 200),
    };

    private SKPath? _wire;
    private float _wireW, _wireY;

    private void DrawGarland(SKCanvas c, World w)
    {
        float y = w.MenuBarH + 1;
        const float span = 150, sag = 13;
        int segs = (int)MathF.Ceiling(w.W / span);
        int mode = (int)(w.Time / 12) % 3;
        if (_wire == null || _wireW != w.W || _wireY != y)
        {
            _wire?.Dispose();
            _wire = new SKPath();
            for (int s = 0; s < segs; s++)
            {
                float x0 = s * span;
                _wire.MoveTo(x0, y);
                _wire.QuadTo(x0 + span / 2, y + sag * 2, x0 + span, y);
            }
            _wireW = w.W;
            _wireY = y;
        }
        _stroke.StrokeWidth = 1.3f;
        _stroke.Color = new SKColor(25, 60, 35, 220);
        c.DrawPath(_wire, _stroke);

        // dim bulbs first, then the lit layer on top faded by how bright each bulb is
        int bulbs = segs * 5;
        for (int pass = 0; pass < 2; pass++)
        {
            bool lit = pass == 1;
            AtlasBegin();
            for (int bulb = 0; bulb < bulbs; bulb++)
            {
                int s = bulb / 5, k = bulb % 5 + 1;
                float x0 = s * span, x1 = x0 + span, cx = (x0 + x1) / 2, t = k / 6f;
                float bx = (1 - t) * (1 - t) * x0 + 2 * (1 - t) * t * cx + t * t * x1;
                float by = (1 - t) * (1 - t) * y + 2 * (1 - t) * t * (y + sag * 2) + t * t * y;
                float on = mode switch
                {
                    0 => (bulb + (int)(w.Time * 3)) % 3 == 0 ? 1f : 0.35f,
                    1 => 0.4f + 0.6f * MathF.Max(0, MathF.Sin(w.Time * 4 + bulb * 2.39f)),
                    _ => 0.55f + 0.45f * MathF.Sin(w.Time * 1.5f),
                };
                var r = BulbRect(bulb % Bulbs.Length, lit);
                AtlasAdd(r, bx, by, 0.5f, 0, BulbBox / 2f, BulbBox / 4f, lit ? on : 1);
            }
            AtlasFlush(c);
        }
    }

    // ------------------------------------------------------------------ snowballs & snowmen

    private void DrawBalls(SKCanvas c, World w)
    {
        foreach (var root in w.Balls.Where(b => b.Below == null).OrderBy(b => b.R))
        {
            float gy = w.RestSurface(root);
            _fill.Color = new SKColor(60, 80, 120, 50);
            c.DrawOval(root.X, gy + 1, root.R * 0.95f, root.R * 0.18f, _fill);

            var man = root.Man;
            Ball? body = null, head = null;
            if (man != null && root.Above?.Above != null)
            {
                body = root.Above;
                head = body.Above;
                DrawArms(c, body, man, w.Time);
            }

            foreach (var b in root.StackFromRoot()) DrawBall(c, b);

            if (man != null && body != null && head != null)
            {
                for (int k = 0; k < 3; k++)
                {
                    _fill.Color = Coal;
                    c.DrawCircle(body.X + MathF.Sin(k * 1.3f) * 1.5f, body.Y - body.R * 0.35f + k * body.R * 0.38f, Math.Max(1.6f, body.R * 0.085f), _fill);
                }
                DrawScarf(c, body, head, man, w.Time);
                DrawFace(c, head, man, w);
                if (!man.HatOff) DrawHat(c, head, man);
            }
        }
    }

    private void DrawBall(SKCanvas c, Ball b)
    {
        using (var sh = SKShader.CreateRadialGradient(new SKPoint(b.X - b.R * 0.35f, b.Y - b.R * 0.4f), b.R * 1.5f,
                   new[] { Snow, new SKColor(236, 243, 251), new SKColor(196, 213, 233) }, new[] { 0f, 0.55f, 1f },
                   SKShaderTileMode.Clamp))
        {
            _fill.Color = SKColors.White; // shader output is multiplied by paint alpha
            _fill.Shader = sh;
            c.DrawCircle(b.X, b.Y, b.R, _fill);
            _fill.Shader = null;
        }

        // rolling texture
        c.Save();
        using (var clip = new SKPath())
        {
            clip.AddCircle(b.X, b.Y, b.R - 1);
            c.ClipPath(clip, antialias: true);
        }
        _stroke.StrokeWidth = Math.Max(0.8f, b.R * 0.05f);
        _stroke.Color = new SKColor(180, 200, 225, 120);
        for (int k = 0; k < 4; k++)
        {
            float ang = b.Angle + k * MathF.PI / 2;
            float ox = b.X + MathF.Cos(ang) * b.R * 0.55f, oy = b.Y + MathF.Sin(ang) * b.R * 0.55f;
            var rect = new SKRect(ox - b.R * 0.3f, oy - b.R * 0.18f, ox + b.R * 0.3f, oy + b.R * 0.18f);
            c.DrawArc(rect, ang * 57.3f, 140, false, _stroke);
        }
        c.Restore();

        _stroke.StrokeWidth = 1;
        _stroke.Color = new SKColor(150, 175, 210, 150);
        c.DrawCircle(b.X, b.Y, b.R, _stroke);
    }

    private void DrawArms(SKCanvas c, Ball body, Snowman m, float time)
    {
        _stroke.Color = new SKColor(101, 67, 33);
        float wv = m.Wave * MathF.Sin(time * 9) * 0.35f;
        DrawArm(c, body.X - body.R * 0.75f, body.Y - body.R * 0.15f, m.ArmL, body.R * 1.15f, body.R);
        DrawArm(c, body.X + body.R * 0.75f, body.Y - body.R * 0.15f, m.ArmR + wv - m.Wave * 0.5f, body.R * 1.15f, body.R);
    }

    private void DrawArm(SKCanvas c, float x, float y, float ang, float len, float r)
    {
        _stroke.StrokeWidth = Math.Max(1.8f, r * 0.075f);
        float ex = x + MathF.Cos(ang) * len, ey = y + MathF.Sin(ang) * len;
        c.DrawLine(x, y, ex, ey, _stroke);
        _stroke.StrokeWidth = Math.Max(1.2f, r * 0.05f);
        float mx = x + MathF.Cos(ang) * len * 0.7f, my = y + MathF.Sin(ang) * len * 0.7f;
        c.DrawLine(mx, my, mx + MathF.Cos(ang - 0.6f) * len * 0.28f, my + MathF.Sin(ang - 0.6f) * len * 0.28f, _stroke);
        c.DrawLine(ex, ey, ex + MathF.Cos(ang + 0.5f) * len * 0.2f, ey + MathF.Sin(ang + 0.5f) * len * 0.2f, _stroke);
        c.DrawLine(ex, ey, ex + MathF.Cos(ang - 0.4f) * len * 0.18f, ey + MathF.Sin(ang - 0.4f) * len * 0.18f, _stroke);
    }

    private void DrawScarf(SKCanvas c, Ball body, Ball head, Snowman m, float time)
    {
        float ny = head.Y + head.R * 0.78f;
        float wdt = head.R * 1.75f, hgt = Math.Max(4, head.R * 0.32f);
        _fill.Color = m.Scarf;
        c.DrawRoundRect(head.X - wdt / 2, ny - hgt / 2, wdt, hgt, hgt / 2, hgt / 2, _fill);
        float sway = MathF.Sin(time * 2.1f) * head.R * 0.08f;
        using (var tail = new SKPath())
        {
            float tx = head.X + head.R * 0.35f;
            tail.MoveTo(tx, ny);
            tail.LineTo(tx + hgt * 1.1f, ny);
            tail.LineTo(tx + hgt * 1.3f + sway, ny + head.R * 1.0f);
            tail.LineTo(tx + hgt * 0.2f + sway, ny + head.R * 0.95f);
            tail.Close();
            c.DrawPath(tail, _fill);
        }
        _stroke.StrokeWidth = Math.Max(1, hgt * 0.18f);
        _stroke.Color = new SKColor(255, 255, 255, 120);
        for (int k = -2; k <= 2; k++)
            c.DrawLine(head.X + k * wdt * 0.18f, ny - hgt * 0.35f, head.X + k * wdt * 0.18f, ny + hgt * 0.35f, _stroke);
    }

    private void DrawFace(SKCanvas c, Ball head, Snowman m, World w)
    {
        float r = head.R;
        _fill.Color = Coal;
        float eyeR = Math.Max(1.4f, r * 0.1f);
        float eyeH = m.Blink > 0 ? eyeR * 0.2f : eyeR;
        // the eyes follow the cursor a little
        float lx = 0, ly = 0;
        if (w.MouseInside)
        {
            float dx = w.Mx - head.X, dy = w.My - head.Y, len = MathF.Max(1, MathF.Sqrt(dx * dx + dy * dy));
            lx = dx / len * r * 0.06f;
            ly = dy / len * r * 0.05f;
        }
        c.DrawOval(head.X - r * 0.32f + lx, head.Y - r * 0.18f + ly, eyeR, eyeH, _fill);
        c.DrawOval(head.X + r * 0.26f + lx, head.Y - r * 0.18f + ly, eyeR, eyeH, _fill);
        for (int k = 0; k < 5; k++)
        {
            float t = (k - 2) / 2f;
            c.DrawCircle(head.X + t * r * 0.38f, head.Y + r * 0.32f + (1 - t * t) * r * 0.1f, Math.Max(1, r * 0.055f), _fill);
        }

        if (m.HasNose)
        {
            using var nose = new SKPath();
            nose.MoveTo(head.X - r * 0.02f, head.Y + r * 0.0f - r * 0.09f);
            nose.LineTo(head.X + r * 0.85f, head.Y + r * 0.1f);
            nose.LineTo(head.X - r * 0.02f, head.Y + r * 0.0f + r * 0.09f);
            nose.Close();
            _fill.Color = new SKColor(245, 130, 32);
            c.DrawPath(nose, _fill);
            _stroke.StrokeWidth = Math.Max(0.7f, r * 0.025f);
            _stroke.Color = new SKColor(190, 90, 20);
            c.DrawLine(head.X + r * 0.2f, head.Y - r * 0.05f, head.X + r * 0.25f, head.Y + r * 0.03f, _stroke);
            c.DrawLine(head.X + r * 0.45f, head.Y - r * 0.01f, head.X + r * 0.5f, head.Y + r * 0.06f, _stroke);
        }
        else
        {
            _fill.Color = new SKColor(200, 215, 235);
            c.DrawCircle(head.X + r * 0.05f, head.Y + r * 0.02f, Math.Max(1, r * 0.07f), _fill);
        }
    }

    private void DrawHat(SKCanvas c, Ball head, Snowman m) => DrawHatAt(c, head.X, head.Y - head.R * 0.78f, head.R, m.Hat, m.HatColor);

    /// <summary>Hat whose brim sits at (<paramref name="x"/>, <paramref name="top"/>) on a head of radius <paramref name="r"/>.</summary>
    private void DrawHatAt(SKCanvas c, float x, float top, float r, int kind, SKColor color)
    {
        switch (kind)
        {
            case 4: // crown for the giant
            {
                using var crown = new SKPath();
                crown.MoveTo(x - r * 0.62f, top + r * 0.05f);
                crown.LineTo(x - r * 0.68f, top - r * 0.55f);
                crown.LineTo(x - r * 0.34f, top - r * 0.25f);
                crown.LineTo(x, top - r * 0.75f);
                crown.LineTo(x + r * 0.34f, top - r * 0.25f);
                crown.LineTo(x + r * 0.68f, top - r * 0.55f);
                crown.LineTo(x + r * 0.62f, top + r * 0.05f);
                crown.Close();
                using var sh = SKShader.CreateLinearGradient(new SKPoint(0, top - r * 0.75f), new SKPoint(0, top + r * 0.05f),
                    new[] { new SKColor(255, 236, 140), new SKColor(232, 176, 40) }, null, SKShaderTileMode.Clamp);
                _fill.Color = SKColors.White; // shader output is multiplied by paint alpha
                _fill.Shader = sh;
                c.DrawPath(crown, _fill);
                _fill.Shader = null;
                _stroke.StrokeWidth = Math.Max(1, r * 0.03f);
                _stroke.Color = new SKColor(170, 120, 20);
                c.DrawPath(crown, _stroke);
                _fill.Color = new SKColor(220, 38, 38);
                c.DrawCircle(x, top - r * 0.12f, r * 0.08f, _fill);
                _fill.Color = new SKColor(37, 99, 235);
                c.DrawCircle(x - r * 0.38f, top - r * 0.1f, r * 0.06f, _fill);
                c.DrawCircle(x + r * 0.38f, top - r * 0.1f, r * 0.06f, _fill);
                _fill.Color = new SKColor(255, 250, 220);
                foreach (var (px, py) in new[] { (-0.68f, -0.55f), (0f, -0.75f), (0.68f, -0.55f) })
                    c.DrawCircle(x + px * r, top + py * r, r * 0.06f, _fill);
                break;
            }
            case 0: // top hat
                _fill.Color = new SKColor(25, 25, 30);
                c.DrawRoundRect(x - r * 0.8f, top - r * 0.12f, r * 1.6f, r * 0.18f, r * 0.08f, r * 0.08f, _fill);
                c.DrawRect(x - r * 0.52f, top - r * 1.0f, r * 1.04f, r * 0.92f, _fill);
                _fill.Color = color;
                c.DrawRect(x - r * 0.52f, top - r * 0.3f, r * 1.04f, r * 0.17f, _fill);
                break;
            case 1: // bucket
            {
                using var path = new SKPath();
                path.MoveTo(x - r * 0.72f, top + r * 0.05f);
                path.LineTo(x + r * 0.72f, top + r * 0.05f);
                path.LineTo(x + r * 0.5f, top - r * 0.85f);
                path.LineTo(x - r * 0.5f, top - r * 0.85f);
                path.Close();
                using var sh = SKShader.CreateLinearGradient(new SKPoint(x - r, 0), new SKPoint(x + r, 0),
                    new[] { new SKColor(120, 130, 145), new SKColor(205, 212, 222), new SKColor(110, 118, 132) }, null, SKShaderTileMode.Clamp);
                _fill.Color = SKColors.White; // shader output is multiplied by paint alpha
                _fill.Shader = sh;
                c.DrawPath(path, _fill);
                _fill.Shader = null;
                _stroke.StrokeWidth = Math.Max(1, r * 0.05f);
                _stroke.Color = new SKColor(80, 88, 100);
                c.DrawLine(x - r * 0.7f, top - r * 0.02f, x + r * 0.7f, top - r * 0.02f, _stroke);
                break;
            }
            case 2: // beanie
            {
                var rect = new SKRect(x - r * 0.82f, top - r * 0.6f, x + r * 0.82f, top + r * 0.6f);
                using var path = new SKPath();
                path.AddArc(rect, 180, 180);
                path.Close();
                _fill.Color = color;
                c.DrawPath(path, _fill);
                _fill.Color = new SKColor(255, 255, 255, 200);
                c.DrawRoundRect(x - r * 0.86f, top - r * 0.08f, r * 1.72f, r * 0.22f, r * 0.1f, r * 0.1f, _fill);
                _fill.Color = Snow;
                c.DrawCircle(x, top - r * 0.66f, r * 0.2f, _fill);
                break;
            }
            default: // santa hat
            {
                using var path = new SKPath();
                path.MoveTo(x - r * 0.75f, top + r * 0.05f);
                path.QuadTo(x - r * 0.1f, top - r * 1.3f, x - r * 0.95f, top - r * 0.9f);
                path.QuadTo(x + r * 0.2f, top - r * 1.5f, x + r * 0.75f, top + r * 0.05f);
                path.Close();
                _fill.Color = new SKColor(210, 30, 40);
                c.DrawPath(path, _fill);
                _fill.Color = Snow;
                c.DrawRoundRect(x - r * 0.82f, top - r * 0.1f, r * 1.64f, r * 0.24f, r * 0.12f, r * 0.12f, _fill);
                c.DrawCircle(x - r * 0.95f, top - r * 0.9f, r * 0.17f, _fill);
                break;
            }
        }
    }

    // ------------------------------------------------------------------ presents

    private void DrawPresent(SKCanvas c, Present p)
    {
        float fade = p.Age > 880 ? Math.Max(0, (900 - p.Age) / 20) : 1;
        float s = p.Size;
        if (p.Coal)
        {
            using var lump = Furry(p.X, p.Y, s * 0.6f, s * 0.45f, 7, 0.9f);
            _fill.Color = A(new SKColor(34, 34, 40), fade);
            c.DrawPath(lump, _fill);
            _fill.Color = A(new SKColor(120, 125, 140), 0.6f * fade);
            c.DrawCircle(p.X - s * 0.18f, p.Y - s * 0.15f, s * 0.08f, _fill);
            c.DrawCircle(p.X + s * 0.12f, p.Y - s * 0.2f, s * 0.05f, _fill);
            return;
        }
        _fill.Color = A(p.Box, fade);
        c.DrawRoundRect(p.X - s / 2, p.Y - s / 2, s, s, 2, 2, _fill);
        _fill.Color = A(new SKColor(0, 0, 0), 0.15f * fade);
        c.DrawRect(p.X - s / 2, p.Y - s / 2, s, s * 0.22f, _fill);
        _fill.Color = A(p.Ribbon, fade);
        c.DrawRect(p.X - s * 0.09f, p.Y - s / 2, s * 0.18f, s, _fill);
        c.DrawRect(p.X - s / 2, p.Y - s * 0.09f, s, s * 0.18f, _fill);
        c.DrawOval(p.X - s * 0.2f, p.Y - s / 2 - s * 0.1f, s * 0.2f, s * 0.12f, _fill);
        c.DrawOval(p.X + s * 0.2f, p.Y - s / 2 - s * 0.1f, s * 0.2f, s * 0.12f, _fill);
    }

    // ------------------------------------------------------------------ tractor

    private void DrawTractor(SKCanvas c, World w)
    {
        var t = w.Tractor;
        float baseY = w.H - 1.5f;
        float bounce = MathF.Sin(t.T * 14) * 0.8f;
        c.Save();
        c.Translate(t.X, baseY);
        c.Scale(t.Dir * Tractor.Scale, Tractor.Scale);

        // pushed snow in front of the blade
        float ph = t.PileH / Tractor.Scale;
        if (ph > 1)
        {
            using var pile = new SKPath();
            pile.MoveTo(78, 0);
            pile.LineTo(80, -ph * 0.95f);
            pile.QuadTo(84 + ph * 0.5f, -ph * 1.15f, 86 + ph * 0.9f, -ph * 0.55f);
            pile.QuadTo(88 + ph * 1.4f, -ph * 0.1f, 90 + ph * 1.6f, 0);
            pile.Close();
            using var sh = SKShader.CreateLinearGradient(new SKPoint(0, -ph), new SKPoint(0, 0), new[] { Snow, SnowShade }, null, SKShaderTileMode.Clamp);
            _fill.Color = SKColors.White; // shader output is multiplied by paint alpha
            _fill.Shader = sh;
            c.DrawPath(pile, _fill);
            _fill.Shader = null;
        }

        c.Translate(0, bounce);

        // chassis
        _fill.Color = new SKColor(55, 55, 62);
        c.DrawRect(-52, -42, 108, 16, _fill);

        // hood
        _fill.Color = new SKColor(232, 113, 28);
        c.DrawRoundRect(-2, -62, 62, 30, 6, 6, _fill);
        _fill.Color = new SKColor(255, 150, 60);
        c.DrawRect(2, -60, 54, 4, _fill);
        _stroke.StrokeWidth = 1.6f;
        _stroke.Color = new SKColor(60, 40, 20, 180);
        for (int k = 0; k < 4; k++) c.DrawLine(52 + k * 2, -56, 52 + k * 2, -36, _stroke);
        _fill.Color = new SKColor(255, 245, 200);
        c.DrawCircle(58, -50, 3.5f, _fill);

        // exhaust
        _fill.Color = new SKColor(70, 70, 75);
        c.DrawRect(15, -86, 5, 26, _fill);

        // cab
        _fill.Color = new SKColor(232, 113, 28);
        c.DrawRect(-56, -104, 6, 66, _fill);
        c.DrawRect(-6, -104, 6, 66, _fill);
        c.DrawRect(-56, -48, 56, 12, _fill);
        _fill.Color = new SKColor(170, 215, 245, 170);
        c.DrawRect(-50, -98, 44, 50, _fill);
        // driver
        _fill.Color = new SKColor(245, 205, 170);
        c.DrawCircle(-26, -72, 7.5f, _fill);
        _fill.Color = new SKColor(200, 30, 40);
        using (var hat = new SKPath())
        {
            hat.MoveTo(-34, -76);
            hat.LineTo(-18, -76);
            hat.LineTo(-32, -90);
            hat.Close();
            c.DrawPath(hat, _fill);
        }
        _fill.Color = Snow;
        c.DrawCircle(-32, -90, 2.5f, _fill);
        c.DrawRect(-35, -78, 18, 3, _fill);
        _fill.Color = new SKColor(110, 70, 40);
        c.DrawRect(-31, -64, 10, 14, _fill);
        // roof
        _fill.Color = new SKColor(45, 45, 52);
        c.DrawRoundRect(-62, -110, 68, 7, 3, 3, _fill);
        // snow on roof
        _fill.Color = Snow;
        c.DrawRoundRect(-60, -114, 62, 5, 2.5f, 2.5f, _fill);

        // beacon
        bool flash = ((int)(t.T * 3)) % 2 == 0;
        var beacon = flash ? new SKColor(255, 170, 0) : new SKColor(180, 100, 0);
        if (flash)
        {
            using var glow = SKShader.CreateRadialGradient(new SKPoint(-28, -118), 26, new[] { new SKColor(255, 170, 0, 150), new SKColor(255, 170, 0, 0) }, null, SKShaderTileMode.Clamp);
            _fill.Color = SKColors.White; // shader output is multiplied by paint alpha
            _fill.Shader = glow;
            c.DrawCircle(-28, -118, 26, _fill);
            _fill.Shader = null;
        }
        _fill.Color = beacon;
        c.DrawRoundRect(-33, -122, 10, 8, 4, 4, _fill);

        // blade arms + blade
        _stroke.StrokeWidth = 4;
        _stroke.Color = new SKColor(70, 70, 75);
        c.DrawLine(50, -34, 74, -24, _stroke);
        c.DrawLine(50, -20, 74, -10, _stroke);
        using (var blade = new SKPath())
        {
            blade.MoveTo(72, -46);
            blade.QuadTo(86, -24, 84, 0);
            blade.LineTo(76, 0);
            blade.QuadTo(78, -22, 66, -44);
            blade.Close();
            _fill.Color = new SKColor(245, 196, 30);
            c.DrawPath(blade, _fill);
        }
        _stroke.StrokeWidth = 2.5f;
        _stroke.Color = new SKColor(30, 30, 30);
        c.DrawLine(73, -38, 78, -34, _stroke);
        c.DrawLine(78, -24, 81, -20, _stroke);
        c.DrawLine(80, -10, 82, -6, _stroke);

        // wheels
        DrawWheel(c, -30, -26, 26, t.Wheel * t.Dir);
        DrawWheel(c, 38, -16, 16, t.Wheel * t.Dir * 1.6f);

        c.Restore();
    }

    private void DrawWheel(SKCanvas c, float x, float y, float r, float ang)
    {
        _fill.Color = new SKColor(28, 28, 30);
        c.DrawCircle(x, y, r, _fill);
        _stroke.StrokeWidth = 3;
        _stroke.Color = new SKColor(60, 60, 65);
        for (int k = 0; k < 10; k++)
        {
            float a = ang + k * MathF.Tau / 10;
            c.DrawLine(x + MathF.Cos(a) * r * 0.82f, y + MathF.Sin(a) * r * 0.82f, x + MathF.Cos(a) * r, y + MathF.Sin(a) * r, _stroke);
        }
        _fill.Color = new SKColor(242, 193, 46);
        c.DrawCircle(x, y, r * 0.52f, _fill);
        _fill.Color = new SKColor(90, 70, 20);
        for (int k = 0; k < 5; k++)
        {
            float a = ang + k * MathF.Tau / 5;
            c.DrawCircle(x + MathF.Cos(a) * r * 0.3f, y + MathF.Sin(a) * r * 0.3f, Math.Max(1, r * 0.07f), _fill);
        }
    }

    // ------------------------------------------------------------------ grinch

    private static SKPath Furry(float cx, float cy, float rx, float ry, int spikes, float seed)
    {
        var p = new SKPath();
        for (int i = 0; i <= spikes * 2; i++)
        {
            float a = i * MathF.PI / spikes;
            float k = i % 2 == 0 ? 1f : 0.86f + 0.06f * MathF.Sin(i * 3.1f + seed);
            float x = cx + MathF.Cos(a) * rx * k, y = cy + MathF.Sin(a) * ry * k;
            if (i == 0) p.MoveTo(x, y);
            else p.LineTo(x, y);
        }
        p.Close();
        return p;
    }

    private void DrawGrinch(SKCanvas c, World w)
    {
        var g = w.Grinch;
        float feet = w.Ground.SurfaceY(g.X) + 2;
        bool steal = g.State == GrinchState.Steal;
        float swing = steal ? 0 : MathF.Sin(g.Step);
        float hop = steal ? 0 : MathF.Abs(MathF.Cos(g.Step)) * 3;

        c.Save();
        c.Translate(g.X, feet - hop);
        c.Scale(g.Dir, 1);

        var green = new SKColor(108, 180, 58);
        var dark = new SKColor(70, 130, 35);

        // sack on the back
        if (g.HasPresent)
        {
            _fill.Color = new SKColor(150, 110, 70);
            c.DrawCircle(-20, -52, 15, _fill);
            _fill.Color = g.PresentColor;
            c.DrawRect(-26, -72, 11, 9, _fill);
        }

        // legs
        _stroke.Color = dark;
        _stroke.StrokeWidth = 5;
        float l1 = swing * 0.8f, l2 = -swing * 0.8f;
        c.DrawLine(-3, -28, -3 + MathF.Sin(l1) * 22, -28 + MathF.Cos(l1) * 26, _stroke);
        c.DrawLine(3, -28, 3 + MathF.Sin(l2) * 22, -28 + MathF.Cos(l2) * 26, _stroke);
        _stroke.StrokeWidth = 4;
        c.DrawLine(-3 + MathF.Sin(l1) * 22, -28 + MathF.Cos(l1) * 26, 5 + MathF.Sin(l1) * 22, -28 + MathF.Cos(l1) * 26, _stroke);
        c.DrawLine(3 + MathF.Sin(l2) * 22, -28 + MathF.Cos(l2) * 26, 11 + MathF.Sin(l2) * 22, -28 + MathF.Cos(l2) * 26, _stroke);

        // back arm
        _stroke.Color = dark;
        _stroke.StrokeWidth = 4.5f;
        c.DrawLine(-6, -54, -6 - swing * 14, -38, _stroke);

        // body
        _fill.Color = green;
        using (var body = Furry(0, -44, 15, 19, 14, 0.3f)) c.DrawPath(body, _fill);

        // head
        using (var head = Furry(6, -72, 13, 12, 12, 1.7f)) c.DrawPath(head, _fill);

        // eyes + brows
        _fill.Color = new SKColor(250, 225, 70);
        c.DrawOval(5, -75, 3.6f, 2.6f, _fill);
        c.DrawOval(14, -75, 3.6f, 2.6f, _fill);
        _fill.Color = new SKColor(200, 30, 30);
        c.DrawCircle(6.5f, -75, 1.4f, _fill);
        c.DrawCircle(15.5f, -75, 1.4f, _fill);
        _stroke.StrokeWidth = 2;
        _stroke.Color = new SKColor(40, 80, 20);
        if (g.Reformed)
        {
            // kind brows, rosy cheeks and a heart that grew
            c.DrawLine(1, -80, 9, -81.5f, _stroke);
            c.DrawLine(18, -80, 11, -81.5f, _stroke);
            _fill.Color = new SKColor(255, 120, 140, 150);
            c.DrawCircle(2, -69, 3, _fill);
            c.DrawCircle(19, -70, 3, _fill);
            c.Save();
            float beat = 1 + 0.15f * MathF.Abs(MathF.Sin(w.Time * 5));
            c.Translate(4, -120);
            c.Scale(beat * g.Dir, beat);
            DrawHeart(c, 0, 0, 7, new SKColor(240, 60, 100));
            c.Restore();
        }
        else
        {
            c.DrawLine(1, -81, 9, -77.5f, _stroke);
            c.DrawLine(18, -81, 11, -77.5f, _stroke);
        }

        // evil grin
        using (var mouth = new SKPath())
        {
            mouth.MoveTo(-1, -68);
            mouth.QuadTo(9, -61, 20, -69);
            _stroke.StrokeWidth = 1.8f;
            _stroke.Color = new SKColor(30, 50, 15);
            c.DrawPath(mouth, _stroke);
        }

        // snowball splat on the face
        if (g.HitT < 4)
        {
            _fill.Color = A(Snow, 1 - g.HitT / 4);
            c.DrawCircle(10, -73, 6.5f, _fill);
            c.DrawCircle(4, -68, 4, _fill);
            c.DrawCircle(16, -67, 3.5f, _fill);
        }

        // santa hat
        using (var hat = new SKPath())
        {
            hat.MoveTo(-6, -80);
            hat.LineTo(18, -82);
            hat.QuadTo(8, -100, -14, -100);
            hat.Close();
            _fill.Color = new SKColor(210, 30, 40);
            c.DrawPath(hat, _fill);
        }
        _fill.Color = Snow;
        c.DrawRoundRect(-8, -84, 28, 5, 2.5f, 2.5f, _fill);
        c.DrawCircle(-14, -100, 3.5f, _fill);

        // front arm (reaching when stealing)
        _stroke.Color = green;
        _stroke.StrokeWidth = 4.5f;
        float handX, handY;
        if (steal)
        {
            float reach = Math.Min(1, g.StateT / 0.5f);
            handX = 12 + 22 * reach;
            handY = -56 - 6 * reach;
        }
        else
        {
            handX = 10 + swing * 14;
            handY = -38;
        }
        c.DrawLine(6, -54, handX, handY, _stroke);
        if (g.HasCarrot)
        {
            using var carrot = new SKPath();
            carrot.MoveTo(handX, handY - 2.5f);
            carrot.LineTo(handX + 16, handY - 6);
            carrot.LineTo(handX, handY + 2.5f);
            carrot.Close();
            _fill.Color = new SKColor(245, 130, 32);
            c.DrawPath(carrot, _fill);
        }

        c.Restore();
    }

    // ------------------------------------------------------------------ santa

    private void DrawSanta(SKCanvas c, World w)
    {
        var s = w.Santa;
        c.Save();
        c.Translate(s.X, s.Y);
        c.Scale(s.Dir * 0.9f, 0.9f);

        // reins
        _stroke.StrokeWidth = 1;
        _stroke.Color = new SKColor(230, 190, 80, 200);
        for (int k = 0; k < 3; k++) c.DrawLine(30, -22, 62 + k * 46, -24, _stroke);

        for (int k = 0; k < 3; k++) DrawReindeer(c, 70 + k * 46, s.T * 9 + k * 1.1f, k == 2);

        // sack
        _fill.Color = new SKColor(150, 105, 60);
        c.DrawCircle(-24, -30, 11, _fill);
        _fill.Color = new SKColor(37, 99, 235);
        c.DrawRect(-30, -44, 8, 7, _fill);
        _fill.Color = new SKColor(22, 163, 74);
        c.DrawRect(-22, -46, 7, 8, _fill);

        // santa
        _fill.Color = new SKColor(200, 25, 35);
        c.DrawCircle(-6, -30, 11, _fill);
        _fill.Color = new SKColor(245, 205, 170);
        c.DrawCircle(-2, -45, 6, _fill);
        _fill.Color = Snow;
        c.DrawCircle(1, -41, 5.5f, _fill);
        using (var hat = new SKPath())
        {
            hat.MoveTo(-8, -48);
            hat.LineTo(4, -49);
            hat.LineTo(-10, -60);
            hat.Close();
            _fill.Color = new SKColor(200, 25, 35);
            c.DrawPath(hat, _fill);
        }
        _fill.Color = Snow;
        c.DrawCircle(-10, -60, 2.5f, _fill);
        c.DrawRect(-9, -50, 14, 3, _fill);

        // sleigh
        using (var sleigh = new SKPath())
        {
            sleigh.MoveTo(-34, -32);
            sleigh.LineTo(-30, -8);
            sleigh.LineTo(26, -8);
            sleigh.QuadTo(36, -14, 34, -28);
            sleigh.LineTo(22, -24);
            sleigh.LineTo(-18, -24);
            sleigh.Close();
            _fill.Color = new SKColor(185, 20, 30);
            c.DrawPath(sleigh, _fill);
        }
        _stroke.StrokeWidth = 2.5f;
        _stroke.Color = new SKColor(235, 190, 70);
        using (var runner = new SKPath())
        {
            runner.MoveTo(-36, 0);
            runner.LineTo(30, 0);
            runner.QuadTo(42, 0, 40, -10);
            c.DrawPath(runner, _stroke);
        }
        c.DrawLine(-22, -8, -22, 0, _stroke);
        c.DrawLine(16, -8, 16, 0, _stroke);

        c.Restore();
    }

    private void DrawReindeer(SKCanvas c, float x, float phase, bool lead)
    {
        var brown = new SKColor(140, 90, 50);
        _stroke.Color = new SKColor(110, 70, 40);
        _stroke.StrokeWidth = 2.2f;
        float a = MathF.Sin(phase) * 0.7f;
        c.DrawLine(x - 9, -16, x - 9 + MathF.Sin(a) * 12, -2, _stroke);
        c.DrawLine(x - 5, -16, x - 5 - MathF.Sin(a) * 12, -2, _stroke);
        c.DrawLine(x + 7, -16, x + 7 - MathF.Sin(a) * 12, -2, _stroke);
        c.DrawLine(x + 10, -16, x + 10 + MathF.Sin(a) * 12, -2, _stroke);
        _fill.Color = brown;
        c.DrawOval(x, -20, 14, 7, _fill);
        c.DrawLine(x + 11, -24, x + 16, -32, _stroke);
        c.DrawOval(x + 19, -33, 6, 4, _fill);
        _stroke.Color = new SKColor(200, 170, 120);
        _stroke.StrokeWidth = 1.5f;
        c.DrawLine(x + 15, -36, x + 11, -46, _stroke);
        c.DrawLine(x + 13, -41, x + 8, -43, _stroke);
        c.DrawLine(x + 18, -36, x + 19, -47, _stroke);
        c.DrawLine(x + 19, -42, x + 23, -45, _stroke);
        if (lead)
        {
            using var glow = SKShader.CreateRadialGradient(new SKPoint(x + 25, -33), 9, new[] { new SKColor(255, 40, 40, 200), new SKColor(255, 40, 40, 0) }, null, SKShaderTileMode.Clamp);
            _fill.Color = SKColors.White; // shader output is multiplied by paint alpha
            _fill.Shader = glow;
            c.DrawCircle(x + 25, -33, 9, _fill);
            _fill.Shader = null;
            _fill.Color = new SKColor(255, 50, 50);
            c.DrawCircle(x + 25, -33, 2.5f, _fill);
        }
        else
        {
            _fill.Color = new SKColor(40, 30, 25);
            c.DrawCircle(x + 25, -33, 1.6f, _fill);
        }
    }

    // ------------------------------------------------------------------ extras

    private void DrawHeart(SKCanvas c, float x, float y, float size, SKColor col)
    {
        using var path = new SKPath();
        float s = size;
        path.MoveTo(x, y + s * 0.9f);
        path.CubicTo(x - s * 1.4f, y - s * 0.1f, x - s * 0.6f, y - s * 1.1f, x, y - s * 0.35f);
        path.CubicTo(x + s * 0.6f, y - s * 1.1f, x + s * 1.4f, y - s * 0.1f, x, y + s * 0.9f);
        path.Close();
        _fill.Color = col;
        c.DrawPath(path, _fill);
    }

    private void DrawFlyingHat(SKCanvas c, FlyingHat h)
    {
        float fade = h.Owner == null && h.Age > 110 ? Math.Max(0, (120 - h.Age) / 10) : 1;
        if (fade <= 0) return;
        c.Save();
        c.Translate(h.X, h.Y);
        c.RotateRadians(h.Rot);
        if (fade < 1) c.SaveLayer(new SKPaint { Color = SKColors.White.WithAlpha((byte)(fade * 255)) });
        DrawHatAt(c, 0, 0, h.R, h.Kind, h.Color);
        if (fade < 1) c.Restore();
        c.Restore();
    }

    private void DrawIcicles(SKCanvas c, WindowPile p)
    {
        foreach (var ic in p.Icicles)
        {
            if (ic.Len < 0.8f || !p.VisibleAt(ic.X)) continue;
            float half = 1.2f + ic.Len * 0.14f, y0 = p.BaseY - 1, y1 = p.BaseY + ic.Len;
            using var path = new SKPath();
            path.MoveTo(ic.X - half, y0);
            path.LineTo(ic.X + half, y0);
            path.QuadTo(ic.X + half * 0.4f, y0 + ic.Len * 0.5f, ic.X, y1);
            path.QuadTo(ic.X - half * 0.5f, y0 + ic.Len * 0.5f, ic.X - half, y0);
            path.Close();
            using var sh = SKShader.CreateLinearGradient(new SKPoint(0, y0), new SKPoint(0, y1),
                new[] { new SKColor(245, 250, 255, 240), new SKColor(190, 220, 250, 200), new SKColor(170, 205, 245, 120) },
                null, SKShaderTileMode.Clamp);
            _fill.Color = SKColors.White; // shader output is multiplied by paint alpha
            _fill.Shader = sh;
            c.DrawPath(path, _fill);
            _fill.Shader = null;
            _stroke.StrokeWidth = 0.7f;
            _stroke.Color = new SKColor(255, 255, 255, 200);
            c.DrawLine(ic.X - half * 0.35f, y0 + 1, ic.X - half * 0.1f, y0 + ic.Len * 0.6f, _stroke);
        }
    }

    private void DrawBird(SKCanvas c, Bird b)
    {
        bool flying = b.State != BirdState.Perch;
        float hop = b.HopT > 0 ? MathF.Sin(MathF.PI * (1 - b.HopT / 0.25f)) * 4 : 0;
        c.Save();
        c.Translate(b.X, b.Y - hop);
        c.Scale(b.Dir, 1);

        var back = new SKColor(112, 118, 132);
        var black = new SKColor(28, 28, 34);
        var breast = new SKColor(226, 78, 58);

        if (!flying)
        {
            _stroke.StrokeWidth = 0.9f;
            _stroke.Color = new SKColor(60, 50, 50);
            c.DrawLine(-1.2f, -1.5f, -1.8f, 0, _stroke);
            c.DrawLine(1.2f, -1.5f, 1.6f, 0, _stroke);
        }

        // tail
        _stroke.StrokeWidth = 2.2f;
        _stroke.Color = black;
        c.DrawLine(-4.5f, -5, -9, flying ? -5 : -2.5f, _stroke);

        // body and red breast
        _fill.Color = back;
        c.DrawOval(0, -5.5f, 5.8f, 4.6f, _fill);
        _fill.Color = breast;
        c.DrawOval(1.6f, -4.4f, 4.2f, 3.4f, _fill);

        // head (pecks down now and then)
        c.Save();
        if (b.Peck > 0) c.RotateDegrees(MathF.Sin(MathF.PI * b.Peck / 0.35f) * 35, 3, -8);
        _fill.Color = black;
        c.DrawCircle(3.6f, -9.2f, 3.3f, _fill);
        _fill.Color = new SKColor(70, 70, 76);
        using (var beak = new SKPath())
        {
            beak.MoveTo(6.4f, -10.2f);
            beak.LineTo(8.6f, -9.2f);
            beak.LineTo(6.4f, -8.2f);
            beak.Close();
            c.DrawPath(beak, _fill);
        }
        c.Restore();

        // wing: folded, or flapping in flight
        if (flying)
        {
            float f = MathF.Sin(b.Flap);
            using var wing = new SKPath();
            wing.MoveTo(-2.5f, -6.5f);
            wing.LineTo(2.5f, -6.5f);
            wing.LineTo(-3.5f, -6.5f - f * 8);
            wing.Close();
            _fill.Color = black;
            c.DrawPath(wing, _fill);
        }
        else
        {
            _fill.Color = black;
            c.DrawOval(-1.4f, -5.8f, 3.6f, 2.4f, _fill);
            _stroke.StrokeWidth = 0.9f;
            _stroke.Color = new SKColor(235, 235, 240);
            c.DrawLine(-3.6f, -5.6f, 0.6f, -5.2f, _stroke);
        }
        c.Restore();
    }

    private static readonly SKColor[] AuroraColors = { new(80, 255, 170), new(90, 210, 255), new(190, 120, 255) };

    private void DrawAurora(SKCanvas c, World w)
    {
        float t = w.Time, a = w.Aurora;
        for (int k = 0; k < AuroraColors.Length; k++)
        {
            using var path = new SKPath();
            float minY = float.MaxValue, maxY = 0;
            var tops = new List<SKPoint>();
            var bottoms = new List<SKPoint>();
            for (float x = -30; x <= w.W + 30; x += 24)
            {
                float top = w.MenuBarH + 30 + k * 28 + 28 * MathF.Sin(x * 0.0035f + t * 0.25f + k * 1.7f) + 12 * MathF.Sin(x * 0.011f - t * 0.4f + k);
                float h = 110 + 45 * MathF.Sin(x * 0.006f + t * 0.3f + k * 2.1f);
                tops.Add(new SKPoint(x, top));
                bottoms.Add(new SKPoint(x, top + h));
                minY = Math.Min(minY, top);
                maxY = Math.Max(maxY, top + h);
            }
            path.MoveTo(tops[0]);
            foreach (var pt in tops) path.LineTo(pt);
            for (int i = bottoms.Count - 1; i >= 0; i--) path.LineTo(bottoms[i]);
            path.Close();

            var col = AuroraColors[k];
            using var sh = SKShader.CreateLinearGradient(new SKPoint(0, minY), new SKPoint(0, maxY),
                new[] { A(col, 0), A(col, 0.2f * a), A(col, 0.06f * a), A(col, 0) }, new[] { 0f, 0.18f, 0.6f, 1f }, SKShaderTileMode.Clamp);
            _fill.Color = SKColors.White; // shader output is multiplied by paint alpha
            _fill.Shader = sh;
            c.DrawPath(path, _fill);
            _fill.Shader = null;
        }
    }

    // ------------------------------------------------------------------ text

    private void DrawBubbles(SKCanvas c, World w)
    {
        foreach (var b in w.Bubbles)
        {
            var p = b.Anchor();
            float alpha = Math.Clamp(b.Life * 3, 0, 1);
            c.Save();
            if (b.Scale != 1) c.Scale(b.Scale, b.Scale, p.X, p.Y);
            float tw = Measure(_font, b.Text);
            float bw = tw + 16, bh = 22;
            float x = Math.Clamp(p.X - bw / 2, 4, w.W - bw - 4), y = Math.Max(w.MenuBarH + 4, p.Y - bh);
            _fill.Color = A(new SKColor(20, 40, 70), 0.2f * alpha);
            c.DrawRoundRect(x + 1, y + 2, bw, bh, 11, 11, _fill);
            _fill.Color = A(SKColors.White, 0.95f * alpha);
            c.DrawRoundRect(x, y, bw, bh, 11, 11, _fill);
            using (var tail = new SKPath())
            {
                float tx = Math.Clamp(p.X, x + 10, x + bw - 10);
                tail.MoveTo(tx - 5, y + bh - 1);
                tail.LineTo(tx + 5, y + bh - 1);
                tail.LineTo(tx, y + bh + 6);
                tail.Close();
                c.DrawPath(tail, _fill);
            }
            _fill.Color = A(new SKColor(30, 40, 60), alpha);
            c.DrawText(b.Text, x + 8, y + 15.5f, _font, _fill);
            c.Restore();
        }
    }

    private void DrawHint(SKCanvas c, World w)
    {
        if (!w.MouseInside || !w.OptionDown || w.Held != null) return;
        if (w.My < w.Ground.SurfaceY(w.Mx) - 34) return;
        string text = w.Ground.HeightAt(w.Mx) >= 2.5f ? "Зажми и катай ком по сугробу" : "Тут мало снега — подожди сугроб";
        float tw = Measure(_hintFont, text);
        float x = Math.Clamp(w.Mx + 14, 4, w.W - tw - 14), y = w.My - 26;
        _fill.Color = new SKColor(15, 25, 45, 190);
        c.DrawRoundRect(x - 6, y - 13, tw + 12, 19, 9, 9, _fill);
        _fill.Color = SKColors.White;
        c.DrawText(text, x, y + 1, _hintFont, _fill);
    }
}
