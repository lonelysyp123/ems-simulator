namespace DesktopFlyTheme;

public sealed class Fly
{
    public int Id { get; init; }

    public Sex Sex { get; init; }

    public double Scale { get; init; }

    public Vec2 Pos { get; set; }

    public Vec2 Vel { get; set; }

    public double Heading { get; set; }

    public FlyState State { get; set; }

    public double MatureAt { get; set; }

    public double? CooldownUntil { get; set; }

    public List<double> EggTimes { get; } = new();

    public Vec2? HideAnchor { get; set; }

    public FleeEpisode? FleeEpisode { get; set; }

    public double? ReactionArmedAt { get; set; }

    public int? MateId { get; set; }

    public double NextHeadingChangeAt { get; set; }

    public double? OrbitSpacing { get; set; }

    public Vec2 HeldVelocity { get; set; }

    public bool InAlertLastStep { get; set; }

    public double AlertRadius => FlyMotion.AlertRadius;

    public double ReactionDelay => FlyMotion.ReactionDelay;

    public double BodyDiameter => FlyMotion.BodyDiameter * Scale;

    public double BodyRadius() => BodyDiameter * 0.5;

    public bool IsMature(double now) => now >= MatureAt;

    public bool IsOnCooldown(double now) => CooldownUntil is double until && now < until;

    public bool Contains(Vec2 point) => Vec2.Distance(Pos, point) <= BodyRadius();

    public void CancelEggs() => EggTimes.Clear();

    public void ScheduleEggs(double now, IRandomSource random)
    {
        EggTimes.Clear();
        var times = new List<double>(ThemeRules.EggCount);
        var guard = 0;
        while (times.Count < ThemeRules.EggCount)
        {
            if (++guard > 100000)
                throw new InvalidOperationException("无法在 5 分钟内抽出间隔足够的产卵时刻。");

            var offset = (1.0 - random.NextDouble()) * ThemeRules.EggWindowSeconds;
            if (offset <= 0)
                continue;

            var time = now + offset;
            var tooClose = false;
            for (var i = 0; i < times.Count; i++)
            {
                if (Math.Abs(times[i] - time) < ThemeRules.EggMinGap)
                {
                    tooClose = true;
                    break;
                }
            }

            if (!tooClose)
                times.Add(time);
        }

        times.Sort();
        EggTimes.AddRange(times);
    }
}

public sealed class Maggot
{
    public int Id { get; init; }

    public Vec2 Pos { get; set; }

    public Vec2 Vel { get; set; }

    public double Heading { get; set; }

    public double BornAt { get; init; }

    public MaggotState State { get; set; }

    public Vec2? HideAnchor { get; set; }

    public int HitCount { get; set; }

    public double? HitWindowEndsAt { get; set; }

    public double BounceRemaining { get; set; }

    public FleeEpisode? FleeEpisode { get; set; }

    public double? ReactionArmedAt { get; set; }

    public double NextHeadingChangeAt { get; set; }

    public Vec2 HeldVelocity { get; set; }

    public bool InAlertLastStep { get; set; }

    public double AlertRadius => MaggotMotion.AlertRadius;

    public double ReactionDelay => MaggotMotion.ReactionDelay;

    public bool Contains(Vec2 point)
    {
        var dx = point.X - Pos.X;
        var dy = point.Y - Pos.Y;
        var cos = Math.Cos(Heading);
        var sin = Math.Sin(Heading);
        var localX = dx * cos + dy * sin;
        var localY = -dx * sin + dy * cos;
        var a = MaggotMotion.Length * 0.5;
        var b = MaggotMotion.Width * 0.5;
        return (localX * localX) / (a * a) + (localY * localY) / (b * b) <= 1.0;
    }

    public bool FullyInside(RectD rect, Vec2 at)
    {
        var a = MaggotMotion.Length * 0.5;
        var b = MaggotMotion.Width * 0.5;
        var cos = Math.Cos(Heading);
        var sin = Math.Sin(Heading);
        var extentX = Math.Sqrt(a * cos * a * cos + b * sin * b * sin);
        var extentY = Math.Sqrt(a * sin * a * sin + b * cos * b * cos);
        const double tolerance = 1e-4;
        return at.X - extentX >= rect.Left - tolerance
            && at.X + extentX <= rect.Right + tolerance
            && at.Y - extentY >= rect.Top - tolerance
            && at.Y + extentY <= rect.Bottom + tolerance;
    }
}

public sealed class Stain
{
    public StainKind Kind { get; init; }

    public Vec2 Pos { get; init; }

    public double Radius { get; init; }

    public static Stain ForFly(Fly fly)
    {
        var diameter = fly.BodyDiameter * 1.6;
        return new Stain
        {
            Kind = StainKind.Fly,
            Pos = fly.Pos,
            Radius = diameter * 0.5,
        };
    }

    public static Stain ForMaggot(Maggot maggot) => new()
    {
        Kind = StainKind.Maggot,
        Pos = maggot.Pos,
        Radius = 11.2 * 0.5,
    };

    public bool IntersectsClean(Vec2 cursor) =>
        Vec2.Distance(Pos, cursor) <= Radius + ThemeRules.RagRadius;
}
