namespace DesktopFlyTheme;

public enum Sex
{
    Male,
    Female,
}

public enum FlyState
{
    Wander,
    Flee,
    Hide,
    SeekMate,
    Mating,
}

public enum MaggotState
{
    Crawl,
    Flee,
    Hide,
    Bounce,
}

public enum HitClass
{
    Button,
    OtherWindow,
    DesktopEmpty,
    OnIcon,
}

public enum StainKind
{
    Fly,
    Maggot,
}

public readonly record struct Rgba(byte R, byte G, byte B, byte A)
{
    public static Rgba FromRgb(byte r, byte g, byte b, byte a = 255) => new(r, g, b, a);
}

public interface IRandomSource
{
    double NextDouble();
}

public sealed class RandomSource : IRandomSource
{
    private readonly Random _random;

    public RandomSource(int seed) => _random = new Random(seed);

    public RandomSource() => _random = new Random();

    public double NextDouble() => _random.NextDouble();
}

public readonly record struct PointerSample(Vec2 Cursor, bool LeftDown, HitClass Hit);

public interface IPointerSource
{
    PointerSample Poll(double now);
}

public sealed class FleeEpisode
{
    public double DirectionAngle { get; set; }

    public double OffsetAngle { get; set; }

    public double Elapsed { get; set; }

    public bool Recovering { get; set; }

    public double RecoverElapsed { get; set; }

    public double RecoverFromSpeed { get; set; }
}

public sealed class MatingSession
{
    public int MaleId { get; init; }

    public int FemaleId { get; init; }

    public double Elapsed { get; set; }

    public Vec2 Direction { get; set; }

    public Vec2 Axis { get; set; }

    public Vec2 Midpoint { get; set; }
}
