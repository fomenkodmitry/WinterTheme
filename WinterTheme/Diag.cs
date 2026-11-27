using System.Diagnostics;

namespace WinterTheme;

/// <summary>Temporary profiling counters (enabled with WINTERTHEME_DIAG=path).</summary>
public static class Diag
{
    private static readonly string? Path = Environment.GetEnvironmentVariable("WINTERTHEME_DIAG");
    public static bool On => Path != null;
    private static long _frames, _updates, _draws, _drawTicks, _updTicks;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static long _last;

    public static readonly string[] Names = { "farFlakes", "santa", "winPiles", "garland", "ground", "presents+tractor", "balls", "hats+birds+grinch", "particles", "nearFlakes", "bubbles" };
    private static readonly long[] Sec = new long[16];
    private static long _secStart;
    public static void Begin() { if (On) _secStart = Stopwatch.GetTimestamp(); }
    public static void Mark(int i) { if (!On) return; long t = Stopwatch.GetTimestamp(); Interlocked.Add(ref Sec[i], t - _secStart); _secStart = t; }

    public static void Frame() { if (On) Interlocked.Increment(ref _frames); }
    public static void Update(long ticks) { if (!On) return; Interlocked.Increment(ref _updates); Interlocked.Add(ref _updTicks, ticks); Flush(); }
    public static void Draw(long ticks) { if (!On) return; Interlocked.Increment(ref _draws); Interlocked.Add(ref _drawTicks, ticks); }

    private static void Flush()
    {
        long now = Clock.ElapsedMilliseconds;
        if (now - _last < 5000) return;
        double sec = (now - _last) / 1000.0;
        _last = now;
        double ms(long t) => t * 1000.0 / Stopwatch.Frequency;
        var line = $"{DateTime.Now:HH:mm:ss} frames/s={_frames / sec:F0} updates/s={_updates / sec:F0} draws/s={_draws / sec:F0} " +
                   $"update={ms(_updTicks) / Math.Max(1, _updates):F2}ms draw={ms(_drawTicks) / Math.Max(1, _draws):F2}ms\n";
        var parts = string.Join(" ", Names.Select((n, i) => $"{n}={ms(Sec[i]) / Math.Max(1, _draws):F2}"));
        File.AppendAllText(Path!, line + "   " + parts + "\n");
        Array.Clear(Sec);
        _frames = _updates = _draws = _drawTicks = _updTicks = 0;
    }
}
