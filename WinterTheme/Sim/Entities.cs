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
    Note,
    Heart,
    Drop,
    Shard,
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
    public string Name = "";

    /// <summary>The hat was blown or knocked off and is lying somewhere (see <see cref="FlyingHat"/>).</summary>
    public bool HatOff;

    public float HopT, DanceT;
    public int Taps;
    public float LastTap = -10;
    public float ThrowCd = 1;
}

public sealed class Ball
{
    public float X, Y, R, Angle, VX, VY;
    public Ball? Below, Above;
    public Snowman? Man;
    public bool Held;

    /// <summary>Thrown snowball in flight: ignores the stack logic until it splats.</summary>
    public bool Flying;

    /// <summary>Window this (root) ball stands on, or -1 for the ground; it rides along when the window moves.</summary>
    public int SupportId = -1;
    public float SupportDx;

    /// <summary>Shaken off: falls past window caps until this time instead of landing on them again.</summary>
    public float NoCatchUntil;

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

    /// <summary>A lump of coal instead of a present.</summary>
    public bool Coal;
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
    public bool Passed;
    public float LoadSent;
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
    public bool Passed;
    public bool PresentCoal;

    /// <summary>Snowballs taken during this visit; three of them melt his heart.</summary>
    public int Hits;
    public bool Reformed;
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

    /// <summary>Display where the present is dropped (Santa crosses all linked displays).</summary>
    public object? DropWorld;

    /// <summary>Already handed over to the next display.</summary>
    public bool Passed;
}

/// <summary>A snowman's hat that the wind or a snowball knocked off.</summary>
public sealed class FlyingHat
{
    public float X, Y, VX, VY, Rot, VRot, R, Age;
    public int Kind;
    public SKColor Color;
    public Snowman? Owner;
    public bool Landed;
    public WindowPile? OnPile;

    public bool Returning;
    public float RetT, SX, SY;
}

public sealed class Icicle
{
    public float X, Len, MaxLen, Grow, DripT;
}

public enum BirdState
{
    Arrive,
    Perch,
    Leave,
}

/// <summary>A bullfinch: lands on window caps, snowman heads and the drift, flies off when startled.</summary>
public sealed class Bird
{
    public float X, Y, VX, VY;
    public BirdState State;
    public float T, Stay, Flap, Peck, HopT;
    public int Dir = 1;

    // perch: one of these is set (none = the ground drift)
    public WindowPile? Pile;
    public Ball? Head;
    public float PerchX;
}
