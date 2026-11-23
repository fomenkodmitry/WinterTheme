namespace WinterTheme.Sim;

/// <summary>Snow heightmap sitting on a horizontal base line (screen bottom or a window top).</summary>
public class Pile
{
    public readonly float[] H;
    public readonly bool[] Visible;
    public readonly float Left;
    public readonly float BinW;
    public float BaseY;
    public readonly float MaxH;
    public readonly float MaxSlope;
    public readonly bool EdgeSpill;

    /// <summary>Optional per-bin height limit (window caps are uneven, not a flat slab).</summary>
    protected float[]? Caps;

    public float CapAt(float x) => Caps?[BinOf(x)] ?? MaxH;

    /// <summary>Relaxation does not move snow across this bin boundary (used by the tractor blade).</summary>
    public int Wall = -1;

    public Pile(float left, float width, float baseY, float maxH, float maxSlope, bool edgeSpill)
    {
        int n = Math.Max(2, (int)MathF.Round(width / 4f));
        H = new float[n];
        Visible = new bool[n];
        Array.Fill(Visible, true);
        Left = left;
        BinW = width / n;
        BaseY = baseY;
        MaxH = maxH;
        MaxSlope = maxSlope;
        EdgeSpill = edgeSpill;
    }

    public int N => H.Length;
    public float Right => Left + N * BinW;
    public bool Contains(float x) => x >= Left && x < Right;
    public int BinOf(float x) => Math.Clamp((int)((x - Left) / BinW), 0, N - 1);
    public float BinCenter(int i) => Left + (i + 0.5f) * BinW;

    private float Get(int i) => H[Math.Clamp(i, 0, N - 1)];

    public float HeightAt(float x)
    {
        float f = (x - Left) / BinW - 0.5f;
        int i = (int)MathF.Floor(f);
        float t = f - i;
        float a = Get(i), b = Get(i + 1);
        return a + (b - a) * t;
    }

    public float SurfaceY(float x) => BaseY - HeightAt(x);
    public bool VisibleAt(float x) => Visible[BinOf(x)];

    public void Add(float x, float amount)
    {
        int i = BinOf(x);
        AddBin(i, amount * 0.5f);
        AddBin(i - 1 >= 0 ? i - 1 : i, amount * 0.25f);
        AddBin(i + 1 < N ? i + 1 : i, amount * 0.25f);
    }

    public void AddBin(int i, float amount)
    {
        if ((uint)i >= (uint)N) return;
        H[i] = Math.Min(Caps?[i] ?? MaxH, H[i] + amount);
    }

    /// <summary>Remove up to <paramref name="perBin"/> from every bin in [x0, x1]; returns total removed.</summary>
    public float Take(float x0, float x1, float perBin)
    {
        if (x1 < Left || x0 >= Right) return 0;
        int a = BinOf(x0), b = BinOf(x1);
        float sum = 0;
        for (int i = a; i <= b; i++)
        {
            float t = Math.Min(H[i], perBin);
            H[i] -= t;
            sum += t;
        }
        return sum;
    }

    public float Total
    {
        get
        {
            float s = 0;
            foreach (var h in H) s += h;
            return s;
        }
    }

    public float Average => Total / N;

    public float Max
    {
        get
        {
            float m = 0;
            foreach (var h in H) m = Math.Max(m, h);
            return m;
        }
    }

    /// <summary>Let steep slopes slide. Returns the amounts that slid off the left and right ends.</summary>
    public (float left, float right) Relax(bool forward)
    {
        float s = MaxSlope;
        int n = N;
        if (forward)
        {
            for (int i = 0; i < n - 1; i++) Pair(i, s);
        }
        else
        {
            for (int i = n - 2; i >= 0; i--) Pair(i, s);
        }

        float l = 0, r = 0;
        if (EdgeSpill)
        {
            float edge = s * 1.1f;
            if (H[0] > edge) { l = (H[0] - edge) * 0.5f; H[0] -= l; }
            if (H[n - 1] > edge) { r = (H[n - 1] - edge) * 0.5f; H[n - 1] -= r; }
        }
        return (l, r);
    }

    private void Pair(int i, float s)
    {
        if (i + 1 == Wall) return;
        float d = H[i] - H[i + 1];
        if (d > s)
        {
            float m = (d - s) * 0.5f;
            H[i] -= m;
            H[i + 1] += m;
        }
        else if (d < -s)
        {
            float m = (-d - s) * 0.5f;
            H[i] += m;
            H[i + 1] -= m;
        }
    }

    public void Melt(float amount)
    {
        for (int i = 0; i < N; i++) H[i] = Math.Max(0, H[i] - amount);
    }
}

public sealed class WindowPile : Pile
{
    public readonly int Id;
    public readonly float Bx, By, Bw, Bh;
    public float SpillL, SpillR;

    public WindowPile(int id, float x, float y, float w, float h)
        : base(x + 2, w - 4, y, 15, 1.5f, true)
    {
        Id = id;
        Bx = x; By = y; Bw = w; Bh = h;
        Caps = new float[N];
        float seed = id * 0.37f;
        for (int i = 0; i < N; i++)
            Caps[i] = MaxH * (0.62f + 0.2f * MathF.Sin(i * 0.045f + seed) + 0.12f * MathF.Sin(i * 0.17f + seed * 3) + 0.06f);
    }
}
