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
        c.Clear(SKColors.Transparent);
        DrawFlakes(c, w, far: true);
        if (w.Santa.Active) DrawSanta(c, w);
        foreach (var p in w.PileOrder) DrawPile(c, p, true, w.Time, 15);
        if (Settings.Current.Garland) DrawGarland(c, w);
        DrawPile(c, w.Ground, false, w.Time, 60);
        foreach (var p in w.Presents) DrawPresent(c, p);
        if (w.Tractor.Active) DrawTractor(c, w);
        DrawBalls(c, w);
        if (w.Grinch.Active) DrawGrinch(c, w);
        DrawParticles(c, w);
        DrawFlakes(c, w, far: false);
        DrawBubbles(c, w);
        DrawHint(c, w);
    }

    // ------------------------------------------------------------------ snow

    private void DrawFlakes(SKCanvas c, World w, bool far)
    {
        _stroke.StrokeWidth = 0.8f;
        foreach (var f in w.Flakes)
        {
            if (far != f.Depth < 0.4f) continue;
            float a = far ? 0.35f + f.Depth : 0.75f + f.Depth * 0.25f;
            if (!far && f.Size > 3.0f)
            {
                _stroke.Color = A(Snow, a);
                float r = f.Size * 1.25f;
                for (int k = 0; k < 3; k++)
                {
                    float ang = f.Spin + k * MathF.PI / 3;
                    float dx = MathF.Cos(ang) * r, dy = MathF.Sin(ang) * r;
                    c.DrawLine(f.X - dx, f.Y - dy, f.X + dx, f.Y + dy, _stroke);
                }
                _fill.Color = A(Snow, a);
                c.DrawCircle(f.X, f.Y, 1.1f, _fill);
            }
            else
            {
                _fill.Color = A(Snow, a);
                c.DrawCircle(f.X, f.Y, f.Size * 0.6f, _fill);
            }
        }
    }

    private void DrawPile(SKCanvas c, Pile p, bool useVis, float time, float shadeDepth)
    {
        using var path = new SKPath();
        int n = p.N;
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
        if (path.IsEmpty) return;

        // soft drop shadow so snow reads on white windows too
        c.Save();
        c.Translate(0, 1.2f);
        _fill.Color = new SKColor(40, 60, 90, 45);
        c.DrawPath(path, _fill);
        c.Restore();

        using (var sh = SKShader.CreateLinearGradient(
                   new SKPoint(0, p.BaseY - maxH), new SKPoint(0, p.BaseY + 1),
                   new[] { Snow, Snow, SnowShade }, new[] { 0f, Math.Clamp(1 - shadeDepth / Math.Max(maxH, 1), 0, 0.95f), 1f },
                   SKShaderTileMode.Clamp))
        {
            _fill.Shader = sh;
            c.DrawPath(path, _fill);
            _fill.Shader = null;
        }

        _stroke.StrokeWidth = 0.9f;
        _stroke.Color = new SKColor(150, 175, 210, 110);
        c.DrawPath(path, _stroke);

        // glints
        for (int k = 0; k < n; k++)
        {
            if (p.H[k] < 4 || (useVis && !p.Visible[k])) continue;
            uint h = (uint)(k * 2654435761u);
            if (h % 23 != 0) continue;
            float tw = MathF.Sin(time * 2.2f + h % 100);
            if (tw < 0.55f) continue;
            float gx = p.BinCenter(k), gy = p.BaseY - p.H[k] + 2 + h % 5;
            float r = 2.2f * (tw - 0.5f) * 2;
            _stroke.StrokeWidth = 0.8f;
            _stroke.Color = new SKColor(255, 255, 255, 230);
            c.DrawLine(gx - r, gy, gx + r, gy, _stroke);
            c.DrawLine(gx, gy - r, gx, gy + r, _stroke);
            _fill.Color = new SKColor(190, 220, 255, 200);
            c.DrawCircle(gx, gy, 0.9f, _fill);
        }
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
            }
        }
    }

    // ------------------------------------------------------------------ garland

    private static readonly SKColor[] Bulbs =
    {
        new(255, 70, 70), new(255, 200, 60), new(70, 220, 110), new(80, 160, 255), new(255, 110, 200),
    };

    private void DrawGarland(SKCanvas c, World w)
    {
        float y = w.MenuBarH + 1;
        const float span = 150, sag = 13;
        int segs = (int)MathF.Ceiling(w.W / span);
        int mode = (int)(w.Time / 12) % 3;
        _stroke.StrokeWidth = 1.3f;
        _stroke.Color = new SKColor(25, 60, 35, 220);
        int bulb = 0;
        for (int s = 0; s < segs; s++)
        {
            float x0 = s * span, x1 = x0 + span, cx = (x0 + x1) / 2;
            using (var path = new SKPath())
            {
                path.MoveTo(x0, y);
                path.QuadTo(cx, y + sag * 2, x1, y);
                c.DrawPath(path, _stroke);
            }
            for (int k = 1; k <= 5; k++)
            {
                float t = k / 6f;
                float bx = (1 - t) * (1 - t) * x0 + 2 * (1 - t) * t * cx + t * t * x1;
                float by = (1 - t) * (1 - t) * y + 2 * (1 - t) * t * (y + sag * 2) + t * t * y;
                var col = Bulbs[bulb % Bulbs.Length];
                float on = mode switch
                {
                    0 => (bulb + (int)(w.Time * 3)) % 3 == 0 ? 1f : 0.35f,
                    1 => 0.4f + 0.6f * MathF.Max(0, MathF.Sin(w.Time * 4 + bulb * 2.39f)),
                    _ => 0.55f + 0.45f * MathF.Sin(w.Time * 1.5f),
                };
                DrawBulb(c, bx, by, col, on);
                bulb++;
            }
        }
    }

    private void DrawBulb(SKCanvas c, float x, float y, SKColor col, float on)
    {
        using (var glow = SKShader.CreateRadialGradient(new SKPoint(x, y + 6), 11,
                   new[] { A(col, 0.55f * on), A(col, 0) }, null, SKShaderTileMode.Clamp))
        {
            _fill.Shader = glow;
            c.DrawCircle(x, y + 6, 11, _fill);
            _fill.Shader = null;
        }
        _fill.Color = new SKColor(40, 70, 45);
        c.DrawRect(x - 1.8f, y, 3.6f, 3, _fill);
        var lit = new SKColor((byte)(col.Red * (0.45f + 0.55f * on)), (byte)(col.Green * (0.45f + 0.55f * on)), (byte)(col.Blue * (0.45f + 0.55f * on)));
        _fill.Color = lit;
        c.DrawOval(x, y + 6.5f, 2.8f, 4f, _fill);
        _fill.Color = new SKColor(255, 255, 255, (byte)(160 * on));
        c.DrawCircle(x - 0.9f, y + 5.2f, 0.9f, _fill);
    }

    // ------------------------------------------------------------------ snowballs & snowmen

    private void DrawBalls(SKCanvas c, World w)
    {
        foreach (var root in w.Balls.Where(b => b.Below == null).OrderBy(b => b.R))
        {
            float gy = w.Ground.SurfaceY(root.X);
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
                DrawFace(c, head, man);
                DrawHat(c, head, man);
            }
        }
    }

    private void DrawBall(SKCanvas c, Ball b)
    {
        using (var sh = SKShader.CreateRadialGradient(new SKPoint(b.X - b.R * 0.35f, b.Y - b.R * 0.4f), b.R * 1.5f,
                   new[] { Snow, new SKColor(236, 243, 251), new SKColor(196, 213, 233) }, new[] { 0f, 0.55f, 1f },
                   SKShaderTileMode.Clamp))
        {
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

    private void DrawFace(SKCanvas c, Ball head, Snowman m)
    {
        float r = head.R;
        _fill.Color = Coal;
        float eyeR = Math.Max(1.4f, r * 0.1f);
        float eyeH = m.Blink > 0 ? eyeR * 0.2f : eyeR;
        c.DrawOval(head.X - r * 0.32f, head.Y - r * 0.18f, eyeR, eyeH, _fill);
        c.DrawOval(head.X + r * 0.26f, head.Y - r * 0.18f, eyeR, eyeH, _fill);
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

    private void DrawHat(SKCanvas c, Ball head, Snowman m)
    {
        float r = head.R;
        float top = head.Y - r * 0.78f;
        switch (m.Hat)
        {
            case 0: // top hat
                _fill.Color = new SKColor(25, 25, 30);
                c.DrawRoundRect(head.X - r * 0.8f, top - r * 0.12f, r * 1.6f, r * 0.18f, r * 0.08f, r * 0.08f, _fill);
                c.DrawRect(head.X - r * 0.52f, top - r * 1.0f, r * 1.04f, r * 0.92f, _fill);
                _fill.Color = m.HatColor;
                c.DrawRect(head.X - r * 0.52f, top - r * 0.3f, r * 1.04f, r * 0.17f, _fill);
                break;
            case 1: // bucket
            {
                using var path = new SKPath();
                path.MoveTo(head.X - r * 0.72f, top + r * 0.05f);
                path.LineTo(head.X + r * 0.72f, top + r * 0.05f);
                path.LineTo(head.X + r * 0.5f, top - r * 0.85f);
                path.LineTo(head.X - r * 0.5f, top - r * 0.85f);
                path.Close();
                using var sh = SKShader.CreateLinearGradient(new SKPoint(head.X - r, 0), new SKPoint(head.X + r, 0),
                    new[] { new SKColor(120, 130, 145), new SKColor(205, 212, 222), new SKColor(110, 118, 132) }, null, SKShaderTileMode.Clamp);
                _fill.Shader = sh;
                c.DrawPath(path, _fill);
                _fill.Shader = null;
                _stroke.StrokeWidth = Math.Max(1, r * 0.05f);
                _stroke.Color = new SKColor(80, 88, 100);
                c.DrawLine(head.X - r * 0.7f, top - r * 0.02f, head.X + r * 0.7f, top - r * 0.02f, _stroke);
                break;
            }
            case 2: // beanie
            {
                var rect = new SKRect(head.X - r * 0.82f, top - r * 0.6f, head.X + r * 0.82f, top + r * 0.6f);
                using var path = new SKPath();
                path.AddArc(rect, 180, 180);
                path.Close();
                _fill.Color = m.HatColor;
                c.DrawPath(path, _fill);
                _fill.Color = new SKColor(255, 255, 255, 200);
                c.DrawRoundRect(head.X - r * 0.86f, top - r * 0.08f, r * 1.72f, r * 0.22f, r * 0.1f, r * 0.1f, _fill);
                _fill.Color = Snow;
                c.DrawCircle(head.X, top - r * 0.66f, r * 0.2f, _fill);
                break;
            }
            default: // santa hat
            {
                using var path = new SKPath();
                path.MoveTo(head.X - r * 0.75f, top + r * 0.05f);
                path.QuadTo(head.X - r * 0.1f, top - r * 1.3f, head.X - r * 0.95f, top - r * 0.9f);
                path.QuadTo(head.X + r * 0.2f, top - r * 1.5f, head.X + r * 0.75f, top + r * 0.05f);
                path.Close();
                _fill.Color = new SKColor(210, 30, 40);
                c.DrawPath(path, _fill);
                _fill.Color = Snow;
                c.DrawRoundRect(head.X - r * 0.82f, top - r * 0.1f, r * 1.64f, r * 0.24f, r * 0.12f, r * 0.12f, _fill);
                c.DrawCircle(head.X - r * 0.95f, top - r * 0.9f, r * 0.17f, _fill);
                break;
            }
        }
    }

    // ------------------------------------------------------------------ presents

    private void DrawPresent(SKCanvas c, Present p)
    {
        float fade = p.Age > 880 ? Math.Max(0, (900 - p.Age) / 20) : 1;
        float s = p.Size;
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
        c.DrawLine(1, -81, 9, -77.5f, _stroke);
        c.DrawLine(18, -81, 11, -77.5f, _stroke);

        // evil grin
        using (var mouth = new SKPath())
        {
            mouth.MoveTo(-1, -68);
            mouth.QuadTo(9, -61, 20, -69);
            _stroke.StrokeWidth = 1.8f;
            _stroke.Color = new SKColor(30, 50, 15);
            c.DrawPath(mouth, _stroke);
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

    // ------------------------------------------------------------------ text

    private void DrawBubbles(SKCanvas c, World w)
    {
        foreach (var b in w.Bubbles)
        {
            var p = b.Anchor();
            float alpha = Math.Clamp(b.Life * 3, 0, 1);
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
