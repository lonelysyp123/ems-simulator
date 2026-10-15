namespace DesktopFlyTheme;

/// <summary>
/// 第 8 节苍蝇运动常量。命中率仿真只允许改这一组。
/// 数值来自脚本化人手追击仿真，不是人手实测。
/// </summary>
public static class FlyMotion
{
    public const double BodyDiameter = 22;
    public const double AlertRadius = 150;
    public const double ReactionDelay = 0.020;
    public const double CruiseSpeed = 70;
    public const double FleeAccelDuration = 0.028;
    public const double FleePeakSpeed = 4850;
    public const double FleeHoldDuration = 1.200;
    public const double FleeDecelDuration = 2.0;
    public const double FleeAngleDegrees = 20;

    public static double FleeAngleRadians => FleeAngleDegrees * Math.PI / 180.0;

    public static double FleeSegmentDuration =>
        FleeAccelDuration + FleeHoldDuration + FleeDecelDuration;

    public static double ScaleOf(double unitRandom) => 0.95 + unitRandom * 0.10;

    public static double SpeedAt(double elapsed)
    {
        var accel = FleeAccelDuration;
        var hold = FleeHoldDuration;
        var decel = FleeDecelDuration;
        if (elapsed < accel)
            return Lerp(CruiseSpeed, FleePeakSpeed, accel <= 0 ? 1 : elapsed / accel);
        elapsed -= accel;
        if (elapsed < hold)
            return FleePeakSpeed;
        elapsed -= hold;
        if (elapsed < decel)
            return Lerp(FleePeakSpeed, CruiseSpeed, elapsed / decel);
        return CruiseSpeed;
    }

    public static double DistanceDuring(double elapsed, double duration)
    {
        var distance = 0.0;
        var time = elapsed;
        var left = duration;
        var segment = FleeSegmentDuration;
        while (left > 1e-12 && time < segment)
        {
            var phase = PhaseContaining(time);
            var step = Math.Min(left, phase.End - time);
            var u0 = SpeedAt(time);
            var u1 = SpeedAt(time + step);
            distance += (u0 + u1) * 0.5 * step;
            time += step;
            left -= step;
        }

        return distance;
    }

    public static string Describe() =>
        $"""
        身体基准直径: {BodyDiameter}
        警觉半径: {AlertRadius}
        反应延迟: {ReactionDelay * 1000:0.###} 毫秒
        巡航: {CruiseSpeed} 像素/秒
        逃离加速: {FleeAccelDuration * 1000:0.###} 毫秒内线性升到 {FleePeakSpeed} 像素/秒
        高速保持: {FleeHoldDuration * 1000:0.###} 毫秒
        回到巡航: {FleeDecelDuration:0.###} 秒内线性降到 {CruiseSpeed} 像素/秒
        逃离方向: 背离光标，加 [-{FleeAngleDegrees:0.###}°, +{FleeAngleDegrees:0.###}°]
        """;

    private readonly record struct Phase(double Start, double End);

    private static Phase PhaseContaining(double elapsed)
    {
        if (elapsed < FleeAccelDuration)
            return new Phase(0, FleeAccelDuration);
        if (elapsed < FleeAccelDuration + FleeHoldDuration)
            return new Phase(FleeAccelDuration, FleeAccelDuration + FleeHoldDuration);
        return new Phase(
            FleeAccelDuration + FleeHoldDuration,
            FleeSegmentDuration);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}

/// <summary>
/// 蛆虫运动常量。不参与第 8 节校准。
/// </summary>
public static class MaggotMotion
{
    public const double Length = 14;
    public const double Width = 6;
    public const double AlertRadius = 150;
    public const double ReactionDelay = 0.040;
    public const double CruiseSpeed = 36;
    public const double FleeSpeed = 70;
    public const double FleeAccelDuration = 0.080;
    public const double RecoverDuration = 0.400;
    public const double BounceSpeed = 480;
    public const double BounceDistanceMin = 100;
    public const double BounceDistanceMax = 160;
    public const double HitWindow = 1;
    public const double MetamorphosisSeconds = 600;
    public const double PushSpeed = 36;
    public const double FleeAngleDegrees = 20;

    public static double FleeAngleRadians => FleeAngleDegrees * Math.PI / 180.0;

    public static double ChaseSpeed(double elapsed)
    {
        if (elapsed < FleeAccelDuration)
            return CruiseSpeed + (FleeSpeed - CruiseSpeed) * (elapsed / FleeAccelDuration);
        return FleeSpeed;
    }
}

public static class ThemeRules
{
    public const double StepSeconds = 1.0 / 60.0;
    public const double SeekRadius = 220;
    public const double CourtshipDistance = 28;
    public const double CourtshipSeconds = 0.5;
    public const int CourtshipSteps = 30;
    public const double OrbitSpeed = 20;
    public const double MatingSpeed = 30;
    public const double MatingSeconds = 10;
    public const double MatingSeparation = 18;
    public const double InterruptRadius = 72;
    public const double MatureSeconds = 60;
    public const double CooldownSeconds = 1200;
    public const double EggWindowSeconds = 300;
    public const int EggCount = 10;
    public const double EggMinGap = 0.2;
    public const int PopulationCap = 100;
    public const double SuppressionSeconds = 1800;
    public const double IconSampleInterval = 0.25;
    public const double RagRadius = 56;
    public const double BoundaryProbe = 36;
    public const double HideSampleSpacing = 10;
    public const double InitialSeparation = 180;
    public const double InitialEdgeMargin = 40;
    public const double HeadingInterval = 1.5;
    public const double IconPushSpeed = 70;
    public const int MatingSteps = 600;

    public static readonly Rgba Table = Rgba.FromRgb(0x3A, 0x78, 0xE8);
    public static readonly Rgba FlyBody = Rgba.FromRgb(0x24, 0x24, 0x24);
    public static readonly Rgba Wing = Rgba.FromRgb(0xF2, 0xF2, 0xF2, 102);
    public static readonly Rgba MaleDot = Rgba.FromRgb(0x7E, 0xB6, 0xFF);
    public static readonly Rgba FemaleDot = Rgba.FromRgb(0xE2, 0xB3, 0x4A);
    public static readonly Rgba MaggotFill = Rgba.FromRgb(0xE7, 0xD7, 0xA4);
    public static readonly Rgba MaggotStroke = Rgba.FromRgb(0x8C, 0x73, 0x40);
    public static readonly Rgba StainBase = Rgba.FromRgb(0xC6, 0xA1, 0x5A);
    public static readonly Rgba StainRed = Rgba.FromRgb(0xC4, 0x47, 0x38);
    public static readonly Rgba StainGreen = Rgba.FromRgb(0x6A, 0x8F, 0x48);
    public static readonly Rgba MatingRing = Rgba.FromRgb(0xF4, 0xF7, 0xFB, 178);
    public static readonly Rgba Rag = Rgba.FromRgb(0xE8, 0xEE, 0xF6);
    public static readonly Rgba ButtonFill = Rgba.FromRgb(0x1F, 0x4F, 0x99);
    public static readonly Rgba ButtonText = Rgba.FromRgb(0xFF, 0xFF, 0xFF);
    public static readonly Rgba RagButtonFill = Rgba.FromRgb(0xE2, 0xB3, 0x4A);
    public static readonly Rgba RagButtonText = Rgba.FromRgb(0x1A, 0x1A, 0x1A);

    public static readonly Vec2[] FlyStainRedOffsets =
    [
        new(0.35, 0.15),
        new(-0.40, 0.25),
        new(0.10, -0.45),
        new(-0.20, -0.15),
    ];

    public static readonly Vec2[] FlyStainGreenOffsets =
    [
        new(0.20, 0.40),
        new(-0.35, -0.35),
        new(0.45, -0.10),
    ];
}
