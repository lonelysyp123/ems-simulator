namespace DesktopFlyTheme;

public sealed class HitRateReport
{
    public required int Hits { get; init; }

    public required int Trials { get; init; }

    public required int Seed { get; init; }

    public required int[] SpeedBuckets { get; init; }

    public required int[] SpeedBucketHits { get; init; }

    public double Rate => Trials == 0 ? 0 : (double)Hits / Trials;

    public string Format()
    {
        var buckets = new System.Text.StringBuilder();
        var edges = new[] { 1500, 2000, 2500, 3000, 3500, 4000 };
        for (var i = 0; i < SpeedBucketHits.Length; i++)
        {
            buckets.Append("  ")
                .Append(edges[i])
                .Append('-')
                .Append(edges[i + 1])
                .Append(": ")
                .Append(SpeedBucketHits[i])
                .Append('/')
                .Append(SpeedBuckets[i])
                .Append('\n');
        }

        return $"""
            苍蝇运动常量（第 8 节，脚本化仿真校准后）
            {FlyMotion.Describe()}
            仿真次数: {Trials}
            随机种子: {Seed}
            命中: {Hits}
            命中率: {Rate * 100:0.###}%
            速度分段命中:
            {buckets.ToString().TrimEnd()}
            说明: 这是脚本化人手追击仿真（反应 200 毫秒、起点 180 像素、追击上限 1.5 秒、速度在 [1500, 4000] 均匀抽取），不是人手实测。
            """;
    }
}

public static class HitRateSimulator
{
    public static HitRateReport Run(int trials, int seed)
    {
        var hits = 0;
        var buckets = new int[5];
        var bucketHits = new int[5];
        for (var i = 0; i < trials; i++)
        {
            var rng = new RandomSource(seed + i * 17 + 1);
            var hit = RunTrial(rng, out var speed);
            var bucket = Math.Clamp((int)((speed - 1500) / 500), 0, 4);
            buckets[bucket]++;
            if (hit)
            {
                hits++;
                bucketHits[bucket]++;
            }
        }

        return new HitRateReport
        {
            Hits = hits,
            Trials = trials,
            Seed = seed,
            SpeedBuckets = buckets,
            SpeedBucketHits = bucketHits,
        };
    }

    private static bool RunTrial(IRandomSource random, out double speed)
    {
        var world = new ThemeLoop(random);
        var area = new RectD(-1_000_000, -1_000_000, 1_000_000, 1_000_000);
        world.ConfigureSpace(new[] { area }, area, new[] { area });
        world.Begin(0);

        var heading = random.NextDouble() * Math.PI * 2;
        var fly = world.SpawnFly(Sex.Male, Vec2.Zero, 0, heading);
        var cursor = Vec2.FromAngle(random.NextDouble() * Math.PI * 2) * 180;
        speed = 1500 + random.NextDouble() * 2500;
        var pointer = new SimPointer();

        for (var i = 0; i < 12; i++)
        {
            pointer.Cursor = cursor;
            pointer.LeftDown = false;
            world.AdvanceSteps(1, pointer);
        }

        var id = fly.Id;
        var attempted = false;
        var dt = ThemeRules.StepSeconds;
        for (var i = 0; i < 90; i++)
        {
            cursor = MoveToward(cursor, fly.Pos, speed * dt);
            var inside = Vec2.Distance(cursor, fly.Pos) <= fly.BodyRadius();
            pointer.Cursor = cursor;
            pointer.LeftDown = inside && !attempted;
            if (inside && !attempted)
                attempted = true;
            world.AdvanceSteps(1, pointer);
            if (!ContainsFly(world, id))
                return true;
        }

        return false;
    }

    private static bool ContainsFly(ThemeLoop world, int id)
    {
        var flies = world.Flies;
        for (var i = 0; i < flies.Count; i++)
        {
            if (flies[i].Id == id)
                return true;
        }

        return false;
    }

    private static Vec2 MoveToward(Vec2 from, Vec2 to, double distance)
    {
        var delta = to - from;
        var length = delta.Length;
        if (length < 1e-9)
            return from;
        return from + delta / length * distance;
    }

    private sealed class SimPointer : IPointerSource
    {
        public Vec2 Cursor { get; set; }

        public bool LeftDown { get; set; }

        public PointerSample Poll(double now) => new(Cursor, LeftDown, HitClass.DesktopEmpty);
    }
}
