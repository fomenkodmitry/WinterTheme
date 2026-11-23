using Avalonia.Controls;
using SkiaSharp;

namespace WinterTheme.UI;

public static class IconFactory
{
    /// <summary>Writes icon_*.png files in the layout `iconutil -c icns` expects.</summary>
    public static void ExportIconset(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (int s in new[] { 16, 32, 128, 256, 512 })
        {
            File.WriteAllBytes(Path.Combine(dir, $"icon_{s}x{s}.png"), AppIconPng(s));
            File.WriteAllBytes(Path.Combine(dir, $"icon_{s}x{s}@2x.png"), AppIconPng(s * 2));
        }
    }

    /// <summary>App icon: night sky tile with a snowman and falling snow.</summary>
    public static byte[] AppIconPng(int size)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(SKColors.Transparent);
        c.Scale(size / 1024f);

        var tile = new SKRect(100, 100, 924, 924);
        using var paint = new SKPaint { IsAntialias = true };
        using (var sky = SKShader.CreateLinearGradient(new SKPoint(0, 100), new SKPoint(0, 924),
                   new[] { new SKColor(18, 32, 74), new SKColor(52, 92, 160) }, null, SKShaderTileMode.Clamp))
        {
            paint.Shader = sky;
            c.DrawRoundRect(tile, 185, 185, paint);
            paint.Shader = null;
        }

        c.Save();
        using (var clip = new SKPath())
        {
            clip.AddRoundRect(tile, 185, 185);
            c.ClipPath(clip, antialias: true);
        }

        // drift
        paint.Color = SKColors.White;
        using (var drift = new SKPath())
        {
            drift.MoveTo(100, 760);
            drift.QuadTo(300, 700, 512, 735);
            drift.QuadTo(740, 770, 924, 715);
            drift.LineTo(924, 924);
            drift.LineTo(100, 924);
            drift.Close();
            c.DrawPath(drift, paint);
        }

        // snowman
        void Ball(float x, float y, float r)
        {
            using var sh = SKShader.CreateRadialGradient(new SKPoint(x - r * 0.35f, y - r * 0.4f), r * 1.5f,
                new[] { SKColors.White, new SKColor(196, 213, 233) }, null, SKShaderTileMode.Clamp);
            paint.Shader = sh;
            c.DrawCircle(x, y, r, paint);
            paint.Shader = null;
        }
        Ball(512, 680, 150);
        Ball(512, 485, 108);
        Ball(512, 335, 78);

        paint.Color = new SKColor(30, 30, 34);
        c.DrawCircle(484, 320, 10, paint);
        c.DrawCircle(538, 320, 10, paint);
        foreach (var y in new[] { 450f, 495f, 540f }) c.DrawCircle(512, y, 11, paint);
        // top hat
        c.DrawRoundRect(432, 252, 160, 18, 8, 8, paint);
        c.DrawRect(462, 172, 100, 88, paint);
        paint.Color = new SKColor(210, 30, 40);
        c.DrawRect(462, 236, 100, 16, paint);
        // scarf
        c.DrawRoundRect(450, 398, 124, 26, 13, 13, paint);
        c.DrawRect(540, 410, 24, 70, paint);
        // carrot
        using (var nose = new SKPath())
        {
            nose.MoveTo(510, 335);
            nose.LineTo(590, 350);
            nose.LineTo(510, 352);
            nose.Close();
            paint.Color = new SKColor(245, 130, 32);
            c.DrawPath(nose, paint);
        }

        // flakes
        var rng = new Random(7);
        paint.Color = new SKColor(255, 255, 255, 220);
        for (int i = 0; i < 40; i++)
        {
            float x = 120 + (float)rng.NextDouble() * 780, y = 120 + (float)rng.NextDouble() * 560;
            if (MathF.Abs(x - 512) < 170 && y > 160) continue;
            c.DrawCircle(x, y, 5 + (float)rng.NextDouble() * 9, paint);
        }
        c.Restore();

        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Menu bar snowflake, drawn at runtime so no asset files are needed.</summary>
    public static WindowIcon Snowflake()
    {
        const int size = 64;
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(SKColors.Transparent);
        using var outline = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeWidth = 9, Color = new SKColor(30, 60, 110) };
        using var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeWidth = 5, Color = new SKColor(150, 210, 255) };
        foreach (var p in new[] { outline, line })
        {
            for (int k = 0; k < 6; k++)
            {
                float a = k * MathF.PI / 3;
                float cx = 32, cy = 32, r = 26;
                float ex = cx + MathF.Cos(a) * r, ey = cy + MathF.Sin(a) * r;
                c.DrawLine(cx, cy, ex, ey, p);
                float mx = cx + MathF.Cos(a) * r * 0.6f, my = cy + MathF.Sin(a) * r * 0.6f;
                c.DrawLine(mx, my, mx + MathF.Cos(a + 0.8f) * 9, my + MathF.Sin(a + 0.8f) * 9, p);
                c.DrawLine(mx, my, mx + MathF.Cos(a - 0.8f) * 9, my + MathF.Sin(a - 0.8f) * 9, p);
            }
        }
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return new WindowIcon(new MemoryStream(data.ToArray()));
    }
}
