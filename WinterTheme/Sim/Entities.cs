using SkiaSharp;

namespace WinterTheme.Sim;

public sealed class Flake
{
    public float X, Y, Size, Depth, Phase, Freq, Amp, Spin;
}

public enum PKind
{
    Powder,
    Clump,
    Sparkle,
    Smoke,
    Confetti,
    Rocket,
    Star,
}

public sealed class Particle
{
    public PKind Kind;
    public float X, Y, VX, VY, Life, MaxLife, Size, Amount, Rot;
    public SKColor Color;
}

public sealed class Snowman
{
    public int Hat;
    public SKColor Scarf;
    public SKColor HatColor;
    public bool HasNose = true;
    public float NoseLostAt;
    public float NextBlink = 3;
    public float Blink;
    public float ArmL = -2.5f, ArmR = -0.6f;
    public float Wave;
}

public sealed class Ball
{
    public float X, Y, R, Angle, VX, VY;
    public Ball? Below, Above;
    public Snowman? Man;
    public bool Held;

    /// <summary>Thrown snowball in flight: ignores the stack logic until it splats.</summary>
    public bool Flying;

    public Ball Root
    {
        get
        {
            var b = this;
            while (b.Below != null) b = b.Below;
            return b;
        }
    }

    public Ball Top
    {
        get
        {
            var b = this;
            while (b.Above != null) b = b.Above;
            return b;
        }
    }

    public int StackCount
    {
        get
        {
            int n = 1;
            var b = Root;
            while (b.Above != null) { b = b.Above; n++; }
            return n;
        }
    }

    public IEnumerable<Ball> StackFromRoot()
    {
        for (var b = Root; b != null; b = b.Above) yield return b;
    }
}

public sealed class Present
{
    public float X, Y, VX, VY, Size, Age;
    public bool Landed;
    public SKColor Box, Ribbon;
}

public sealed class Bubble
{
    public string Text = "";
    public Func<SKPoint> Anchor = () => default;
    public float Life = 2.5f;
    public float Scale = 1;
}

public sealed class Tractor
{
    public bool Active;
    public float X;
    public int Dir = 1;
    public float Speed = 85;
    public float Load;
    public float Wheel;
    public float SmokeT;
    public float T;
    public const float Scale = 0.95f;
    public float BladeX => X + Dir * 84 * Scale;
    public float PileH => Math.Min(62, Load <= 0 ? 0 : 4 + MathF.Sqrt(Load) * 1.15f);
}

public enum GrinchState
{
    Run,
    Steal,
    Escape,
}

public sealed class Grinch
{
    public bool Active;
    public float X;
    public int Dir = 1;
    public GrinchState State;
    public float StateT;
    public float Step;
    public float LastStepSin;
    public object? Target;
    public bool HasCarrot, HasPresent;
    public SKColor PresentColor;

    /// <summary>Snowman whose nose is in the Grinch's hand (gets it back if the Grinch is hit).</summary>
    public Snowman? Victim;

    /// <summary>Seconds since the last snowball hit (drawn as a snow splat on the face).</summary>
    public float HitT = 99;
}

public sealed class Santa
{
    public bool Active;
    public float X, Y, Y0;
    public int Dir = -1;
    public float T;
    public float DropX;
    public bool Dropped;
    public float TrailT;
    public int Extra;
    public float ClickCd;
}
