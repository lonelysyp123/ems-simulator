namespace DesktopFlyTheme.Core.Tests;

public sealed class ThemeBehaviorTests
{
    private static readonly RectD Wide = new(0, 0, 1920, 1080);
    private static readonly RectD Field = new(0, 0, 4000, 3000);

    [Fact]
    public void Mating_StartsAfterCourtship_AndRadiusInterruptsWithoutKill()
    {
        var world = New(Field, new SequenceRandom(0.5));
        world.Reset(0);
        var male = Male(world);
        var female = Female(world);
        male.MatureAt = 0;
        female.MatureAt = 0;
        PlacePair(male, female, new Vec2(2000, 1500), 20);
        var pointer = Far();

        world.AdvanceSteps(30, pointer);

        Assert.NotNull(world.Mating);
        Assert.Equal(FlyState.Mating, male.State);
        Assert.Equal(FlyState.Mating, female.State);

        pointer.Cursor = male.Pos + new Vec2(40, 0);
        world.AdvanceSteps(1, pointer);

        Assert.Null(world.Mating);
        Assert.Equal(2, world.Flies.Count);
        Assert.Contains(world.Flies, fly => fly.Id == male.Id);
        Assert.Contains(world.Flies, fly => fly.Id == female.Id);
        Assert.Null(male.CooldownUntil);
        Assert.Null(female.CooldownUntil);
        Assert.Empty(male.EggTimes);
        Assert.Empty(female.EggTimes);
        Assert.Empty(world.Stains);
        world.AdvanceSteps(300, Far());
        Assert.Empty(male.EggTimes);
        Assert.Empty(female.EggTimes);
        Assert.Empty(world.Stains);
        Assert.Equal(2, world.LiveCount);
    }

    [Fact]
    public void Mating_ClickOnBodyInterruptsInsteadOfKilling()
    {
        var world = New(Field, new SequenceRandom(0.5));
        world.Reset(0);
        var male = Male(world);
        var female = Female(world);
        male.MatureAt = 0;
        female.MatureAt = 0;
        PlacePair(male, female, new Vec2(2000, 1500), 20);
        var pointer = Far();
        world.AdvanceSteps(30, pointer);
        Assert.NotNull(world.Mating);

        pointer.Cursor = male.Pos;
        pointer.LeftDown = true;
        pointer.Hit = HitClass.DesktopEmpty;
        world.AdvanceSteps(1, pointer);

        Assert.Null(world.Mating);
        Assert.Contains(world.Flies, fly => fly.Id == male.Id);
        Assert.Contains(world.Flies, fly => fly.Id == female.Id);
        Assert.Null(male.CooldownUntil);
        Assert.Null(female.CooldownUntil);
        Assert.Empty(female.EggTimes);
        Assert.Empty(world.Stains);
    }

    [Fact]
    public void PopulationCap_SuppressesThirtyMinutes_WithoutKillingThePair()
    {
        var world = New(Field, new RandomSource(3));
        world.Begin(0);
        world.SubmitIcons(new[] { new RectD(0, 0, 80, 80) });
        var pointer = new ManualPointer { Cursor = new Vec2(3900, 2900), Hit = HitClass.DesktopEmpty };

        var mother = world.SpawnFly(Sex.Female, new Vec2(2000, 1500), 0, 0);
        mother.MatureAt = 0;
        mother.EggTimes.Add(1.0 / 60.0);

        var mateMale = world.SpawnFly(Sex.Male, new Vec2(2400, 1500), 0, 0);
        var mateFemale = world.SpawnFly(Sex.Female, new Vec2(2420, 1500), 0, Math.PI);
        mateMale.MatureAt = 0;
        mateFemale.MatureAt = 0;
        world.StartMatingForTest(mateMale, mateFemale);

        for (var i = 0; i < 96; i++)
            HideMaggot(world.SpawnMaggot(new Vec2(40, 40), 1_000_000));

        Assert.Equal(99, world.LiveCount);
        world.AdvanceSteps(1, pointer);
        Assert.Equal(100, world.LiveCount);
        Assert.NotNull(world.SuppressionUntil);
        Assert.Equal(world.SimulationTime + 1800, world.SuppressionUntil!.Value, 6);
        Assert.Null(world.Mating);
        Assert.Contains(world.Flies, fly => fly.Id == mateMale.Id);
        Assert.Contains(world.Flies, fly => fly.Id == mateFemale.Id);
        Assert.Null(mateMale.CooldownUntil);
        Assert.Null(mateFemale.CooldownUntil);
        Assert.Empty(mateFemale.EggTimes);
        Assert.Empty(world.Stains);

        mateMale.Pos = new Vec2(2100, 1800);
        mateFemale.Pos = new Vec2(2116, 1800);
        Calm(mateMale);
        Calm(mateFemale);
        world.AdvanceSteps(120, pointer);
        Assert.Null(world.Mating);
        Assert.NotNull(world.SuppressionUntil);

        var until = world.SuppressionUntil!.Value;
        world.RemoveLastMaggot();
        Assert.True(world.LiveCount < 100);
        mother.EggTimes.Add(world.SimulationTime + 1.0 / 60.0);
        world.AdvanceSteps(1, pointer);
        Assert.Equal(100, world.LiveCount);
        Assert.Equal(until, world.SuppressionUntil);

        world.RemoveLastMaggot();
        Assert.True(world.LiveCount < 100);
        world.AdvanceTo(until - 1.0 / 60.0, pointer);
        Assert.NotNull(world.SuppressionUntil);
        world.AdvanceTo(until, pointer);
        Assert.Null(world.SuppressionUntil);
    }

    [Fact]
    public void Suppression_ExtendsAnotherThirtyMinutesWhenStillAtCap()
    {
        var world = New(Field, new RandomSource(4));
        world.Begin(0);
        world.SubmitIcons(new[] { new RectD(0, 0, 80, 80) });
        var pointer = new ManualPointer { Cursor = new Vec2(3900, 2900), Hit = HitClass.DesktopEmpty };
        var mother = world.SpawnFly(Sex.Female, new Vec2(2000, 1500), 0, 0);
        mother.EggTimes.Add(1.0 / 60.0);
        for (var i = 0; i < 98; i++)
            HideMaggot(world.SpawnMaggot(new Vec2(40, 40), 1_000_000));

        Assert.Equal(99, world.LiveCount);
        world.AdvanceSteps(1, pointer);
        Assert.Equal(100, world.LiveCount);
        var firstUntil = world.SuppressionUntil;
        Assert.NotNull(firstUntil);

        world.AdvanceTo(firstUntil.Value, pointer);
        Assert.NotNull(world.SuppressionUntil);
        Assert.True(world.SuppressionUntil > firstUntil);
        Assert.Equal(world.SimulationTime + 1800, world.SuppressionUntil!.Value, 6);
        Assert.True(world.LiveCount >= 100);
    }

    [Fact]
    public void Eggs_LayTenThenMotherDeathDestroysTheRest()
    {
        var world = New(Field, new RandomSource(11));
        world.Reset(0);
        var male = Male(world);
        var female = Female(world);
        male.MatureAt = 0;
        female.MatureAt = 0;
        PlacePair(male, female, new Vec2(2000, 1500), 16);
        var pointer = Far();
        world.AdvanceSteps(30, pointer);
        Assert.NotNull(world.Mating);
        world.AdvanceSteps(600, pointer);
        Assert.Null(world.Mating);
        Assert.Equal(10, female.EggTimes.Count);
        var scheduledAt = world.SimulationTime;
        double previous = double.NegativeInfinity;
        foreach (var time in female.EggTimes)
        {
            Assert.True(time > scheduledAt);
            Assert.True(time <= scheduledAt + 300);
            Assert.True(time - previous >= 0.2 - 1e-9);
            previous = time;
        }

        Assert.Equal(scheduledAt + 1200, female.CooldownUntil);
        Assert.Equal(scheduledAt + 1200, male.CooldownUntil);

        var born = 0;
        var guard = 0;
        while ((world.Maggots.Count < 1 || female.EggTimes.Count == 0) && guard++ < 400)
            world.AdvanceSteps(60, pointer);
        born = world.Maggots.Count;
        var remaining = female.EggTimes.Count;
        Assert.True(born >= 1);
        Assert.True(remaining >= 1);
        Assert.Equal(10, born + remaining);

        pointer.LeftDown = false;
        world.AdvanceSteps(1, pointer);
        female.Pos = new Vec2(80, 80);
        female.Vel = Vec2.Zero;
        pointer.Cursor = female.Pos;
        pointer.LeftDown = true;
        pointer.Hit = HitClass.DesktopEmpty;
        world.AdvanceSteps(1, pointer);

        Assert.DoesNotContain(world.Flies, fly => fly.Id == female.Id);
        Assert.Equal(born, world.Maggots.Count);
        Assert.Single(world.Stains);
        Assert.Equal(StainKind.Fly, world.Stains[0].Kind);
        Assert.All(world.Flies, fly => Assert.Empty(fly.EggTimes));
    }

    [Fact]
    public void FullClutch_SpawnsExactlyTenMaggots()
    {
        var world = New(Field, new RandomSource(12));
        world.Reset(0);
        var male = Male(world);
        var female = Female(world);
        male.MatureAt = 0;
        female.MatureAt = 0;
        PlacePair(male, female, new Vec2(2000, 1500), 16);
        var pointer = Far();
        world.AdvanceSteps(30, pointer);
        Assert.NotNull(world.Mating);
        world.AdvanceSteps(600, pointer);
        Assert.Null(world.Mating);
        Assert.Equal(10, female.EggTimes.Count);
        var done = world.SimulationTime;
        world.AdvanceTo(done + 300, pointer);
        Assert.Equal(10, world.Maggots.Count);
        Assert.Empty(female.EggTimes);
        Assert.Contains(world.Flies, fly => fly.Id == female.Id);
        Assert.Empty(world.Stains);
    }

    [Fact]
    public void Maggot_FirstHitBounces_SecondHitInsideWindowKills_LateHitDoesNot()
    {
        var world = New(Field, new SequenceRandom(0.25));
        world.Begin(0);
        var pointer = Far();
        var maggot = world.SpawnMaggot(new Vec2(500, 500), 0);
        maggot.Heading = 0;
        maggot.Vel = new Vec2(MaggotMotion.CruiseSpeed, 0);
        var origin = maggot.Pos;

        pointer.Cursor = maggot.Pos;
        pointer.LeftDown = true;
        world.AdvanceSteps(1, pointer);

        Assert.Contains(world.Maggots, item => item.Id == maggot.Id);
        Assert.Equal(MaggotState.Bounce, maggot.State);
        Assert.Equal(1, maggot.HitCount);
        Assert.Empty(world.Stains);
        Assert.True(Vec2.Distance(origin, maggot.Pos) > 1);

        pointer.LeftDown = false;
        world.AdvanceSteps(1, pointer);
        pointer.Cursor = maggot.Pos;
        pointer.LeftDown = true;
        world.AdvanceSteps(1, pointer);

        Assert.DoesNotContain(world.Maggots, item => item.Id == maggot.Id);
        Assert.Single(world.Stains);
        Assert.Equal(StainKind.Maggot, world.Stains[0].Kind);
        Assert.Equal(5.6, world.Stains[0].Radius, 6);

        var late = New(Field, new SequenceRandom(0.25));
        late.Begin(0);
        var slow = late.SpawnMaggot(new Vec2(500, 500), 0);
        slow.Heading = 0;
        var latePointer = Far();
        latePointer.Cursor = slow.Pos;
        latePointer.LeftDown = true;
        late.AdvanceSteps(1, latePointer);
        latePointer.LeftDown = false;
        latePointer.Cursor = new Vec2(3900, 2900);
        late.AdvanceSteps(70, latePointer);
        latePointer.Cursor = slow.Pos;
        latePointer.LeftDown = true;
        late.AdvanceSteps(1, latePointer);
        Assert.Contains(late.Maggots, item => item.Id == slow.Id);
        Assert.Empty(late.Stains);
    }

    [Fact]
    public void HideAnchor_StaysPutWhenTheIconMoves()
    {
        var world = New(new RectD(0, 0, 2000, 2000), new SequenceRandom(0.5));
        world.Begin(0);
        world.SubmitIcons(new[] { new RectD(90, 480, 200, 520) });
        var fly = world.SpawnFly(Sex.Male, new Vec2(100, 500), 0, 0);
        fly.State = FlyState.Flee;
        fly.FleeEpisode = new FleeEpisode
        {
            DirectionAngle = 0,
            Elapsed = FlyMotion.FleeAccelDuration,
        };
        fly.Vel = new Vec2(FlyMotion.FleePeakSpeed, 0);
        var pointer = new ManualPointer { Cursor = new Vec2(1900, 1900), Hit = HitClass.DesktopEmpty };

        world.AdvanceSteps(1, pointer);
        Assert.Equal(FlyState.Hide, fly.State);
        var anchor = fly.HideAnchor!.Value;
        Assert.Equal(anchor, fly.Pos);
        Assert.True(anchor.X > 100);
        Assert.Equal(500, anchor.Y, 3);

        world.SubmitIcons(new[] { new RectD(anchor.X - 20, anchor.Y - 30, anchor.X + 80, anchor.Y + 30) });
        world.AdvanceSteps(15, pointer);
        Assert.Equal(FlyState.Hide, fly.State);
        Assert.Equal(anchor, fly.Pos);
        Assert.Equal(anchor, fly.HideAnchor);

        world.SubmitIcons(new[] { new RectD(800, 480, 1000, 520) });
        world.AdvanceSteps(15, pointer);
        Assert.NotEqual(FlyState.Hide, fly.State);
        Assert.Null(fly.HideAnchor);
        Assert.True(fly.Pos.X < 400);
    }

    [Fact]
    public void Stain_IsLeftOnlyForARealKill()
    {
        var world = New(Field, new SequenceRandom(0.5));
        world.Begin(0);
        var fly = world.SpawnFly(Sex.Female, new Vec2(800, 800), 0, 0);
        fly.MatureAt = 0;
        fly.ScheduleEggs(0, new SequenceRandom(0.2, 0.4, 0.6, 0.8, 0.15, 0.35, 0.55, 0.75, 0.25, 0.45, 0.9, 0.1));
        Assert.NotEmpty(fly.EggTimes);
        var pointer = Far();
        pointer.LeftDown = false;
        world.AdvanceSteps(1, pointer);
        fly.Pos = new Vec2(800, 800);
        pointer.Cursor = fly.Pos;
        pointer.LeftDown = true;
        world.AdvanceSteps(1, pointer);
        Assert.DoesNotContain(world.Flies, item => item.Id == fly.Id);
        Assert.Single(world.Stains);
        Assert.Equal(StainKind.Fly, world.Stains[0].Kind);
        Assert.Equal(fly.BodyDiameter * 0.8, world.Stains[0].Radius, 6);

        var capped = New(Field, new SequenceRandom(0.5));
        capped.Begin(0);
        var mother = capped.SpawnFly(Sex.Female, new Vec2(300, 300), 0, 0);
        for (var i = 0; i < 99; i++)
            capped.SpawnMaggot(new Vec2(20, 20), 1_000_000);
        Assert.Equal(100, capped.LiveCount);
        mother.EggTimes.Add(1.0 / 60.0);
        capped.AdvanceSteps(1, Far());
        Assert.Equal(100, capped.LiveCount);
        Assert.Equal(99, capped.Maggots.Count);
        Assert.Empty(capped.Stains);
        Assert.Empty(mother.EggTimes);
    }

    [Fact]
    public void Scale_StaysWithinFivePercent()
    {
        var world = New(Wide, new RandomSource(99));
        world.Begin(0);
        var scales = new List<double>();
        for (var i = 0; i < 400; i++)
            scales.Add(world.SpawnFly(i % 2 == 0 ? Sex.Male : Sex.Female, new Vec2(100, 100), 0, 0).Scale);

        Assert.All(scales, scale => Assert.InRange(scale, 0.95, 1.05));
        Assert.True(scales.Min() <= 0.96);
        Assert.True(scales.Max() >= 1.04);
    }

    [Fact]
    public void Opening_IsOneMaleAndOneFemale_AndTheyCannotMateForOneMinute()
    {
        var world = New(Wide, new SequenceRandom(0));
        world.Reset(0);
        Assert.Equal(2, world.LiveCount);
        Assert.Single(world.Flies, fly => fly.Sex == Sex.Male);
        Assert.Single(world.Flies, fly => fly.Sex == Sex.Female);
        var male = Male(world);
        var female = Female(world);
        Assert.Equal(780, male.Pos.X, 6);
        Assert.Equal(540, male.Pos.Y, 6);
        Assert.Equal(1140, female.Pos.X, 6);
        Assert.Equal(540, female.Pos.Y, 6);
        Assert.Equal(0, male.Heading, 6);
        Assert.Equal(Math.PI, female.Heading, 6);
        Assert.InRange(male.Scale, 0.95, 1.05);
        Assert.InRange(female.Scale, 0.95, 1.05);
        Assert.False(male.IsMature(0));
        Assert.False(female.IsMature(59));
        Assert.True(male.IsMature(60));
        Assert.True(female.IsMature(60));

        var pointer = Far();
        world.AdvanceSteps(59 * 60, pointer);
        Assert.Null(world.Mating);
        Assert.False(male.IsMature(world.SimulationTime));

        PlacePair(male, female, new Vec2(960, 540), 15);
        male.Heading = 0;
        female.Heading = 0;
        Calm(male);
        Calm(female);
        world.AdvanceSteps(120, pointer);
        Assert.NotNull(world.Mating);
        Assert.Equal(male.Id, world.Mating!.MaleId);
        Assert.Equal(female.Id, world.Mating.FemaleId);
    }

    [Fact]
    public void NarrowWorkArea_UsesTheLongEdgeFallback()
    {
        var work = new RectD(0, 0, 200, 100);
        var world = New(work, new SequenceRandom(0.5));
        world.Reset(0);
        var male = Male(world);
        var female = Female(world);
        Assert.Equal(60, male.Pos.X, 6);
        Assert.Equal(50, male.Pos.Y, 6);
        Assert.Equal(140, female.Pos.X, 6);
        Assert.Equal(50, female.Pos.Y, 6);
        Assert.Equal(0, male.Heading, 6);
        Assert.Equal(Math.PI, female.Heading, 6);
    }

    [Fact]
    public void ButtonLayout_UsesDipsAndClearsTheTopTaskbar()
    {
        var work = new RectD(0, 0, 1920, 1080);
        var laid = ButtonLayout.Layout(work, null, 96);
        Assert.Equal(1722, laid.Initialize.Left, 6);
        Assert.Equal(10, laid.Initialize.Top, 6);
        Assert.Equal(72, laid.Initialize.Width, 6);
        Assert.Equal(26, laid.Initialize.Height, 6);
        Assert.Equal(1800, laid.Rag.Left, 6);
        Assert.Equal(52, laid.Rag.Width, 6);
        Assert.Equal(1858, laid.Close.Left, 6);
        Assert.Equal(1910, laid.Close.Right, 6);
        Assert.Equal(6, laid.Rag.Left - laid.Initialize.Right, 6);

        var raised = ButtonLayout.Layout(work, new RectD(0, 0, 1920, 40), 96);
        Assert.Equal(48, raised.Initialize.Top, 6);

        var hidpi = ButtonLayout.Layout(work, null, 192);
        Assert.Equal(144, hidpi.Initialize.Width, 6);
        Assert.Equal(52, hidpi.Initialize.Height, 6);
    }

    [Fact]
    public void HitClassifier_MatchesTheSpecBuckets()
    {
        var icons = new[] { new RectD(10, 10, 40, 40) };
        var buttons = new[] { new RectD(100, 0, 140, 20) };
        Assert.Equal(HitClass.Button, HitClassifier.Classify(new Vec2(110, 10), icons, buttons, WindowKind.Desktop));
        Assert.Equal(HitClass.OnIcon, HitClassifier.Classify(new Vec2(20, 20), icons, buttons, WindowKind.Desktop));
        Assert.Equal(HitClass.DesktopEmpty, HitClassifier.Classify(new Vec2(60, 60), icons, buttons, WindowKind.Desktop));
        Assert.Equal(HitClass.OtherWindow, HitClassifier.Classify(new Vec2(60, 60), icons, buttons, WindowKind.Other));
        Assert.Equal(HitClass.Button, HitClassifier.Classify(new Vec2(110, 10), icons, buttons, WindowKind.Other));
    }

    [Fact]
    public void Table_PaintsTheSpecifiedBlue()
    {
        var world = New(Wide, new SequenceRandom(0.5));
        world.Reset(0);
        var frame = SoftwareRasterizer.Render(world.Draw(), 32, 32, 0, 0);
        Assert.Equal(0xE8, frame.Bgra[0]);
        Assert.Equal(0x78, frame.Bgra[1]);
        Assert.Equal(0x3A, frame.Bgra[2]);
        Assert.Equal(255, frame.Bgra[3]);
    }

    private static ThemeLoop New(RectD work, IRandomSource random)
    {
        var world = new ThemeLoop(random);
        world.ConfigureSpace(new[] { work }, work, new[] { work });
        return world;
    }

    private static ManualPointer Far() => new()
    {
        Cursor = new Vec2(12, 12),
        Hit = HitClass.DesktopEmpty,
    };

    private static Fly Male(ThemeLoop world) => Assert.Single(world.Flies, fly => fly.Sex == Sex.Male);

    private static Fly Female(ThemeLoop world) => Assert.Single(world.Flies, fly => fly.Sex == Sex.Female);

    private static void PlacePair(Fly male, Fly female, Vec2 center, double gap)
    {
        male.Pos = center - new Vec2(gap * 0.5, 0);
        female.Pos = center + new Vec2(gap * 0.5, 0);
        Calm(male);
        Calm(female);
    }

    private static void Calm(Fly fly)
    {
        fly.State = FlyState.Wander;
        fly.ReactionArmedAt = null;
        fly.FleeEpisode = null;
        fly.HideAnchor = null;
        fly.MateId = null;
        fly.OrbitSpacing = null;
        fly.InAlertLastStep = false;
        fly.NextHeadingChangeAt = 1_000_000;
        fly.Vel = Vec2.FromAngle(fly.Heading) * FlyMotion.CruiseSpeed;
        fly.HeldVelocity = fly.Vel;
    }

    private static void HideMaggot(Maggot maggot)
    {
        maggot.State = MaggotState.Hide;
        maggot.HideAnchor = maggot.Pos;
        maggot.Vel = Vec2.Zero;
    }
}

public sealed class ManualPointer : IPointerSource
{
    public Vec2 Cursor { get; set; }

    public bool LeftDown { get; set; }

    public HitClass Hit { get; set; } = HitClass.DesktopEmpty;

    public PointerSample Poll(double now) => new(Cursor, LeftDown, Hit);
}

public sealed class SequenceRandom : IRandomSource
{
    private readonly double[] _values;
    private int _index;

    public SequenceRandom(params double[] values) =>
        _values = values.Length == 0 ? new[] { 0.5 } : values;

    public double NextDouble()
    {
        var value = _values[_index % _values.Length];
        _index++;
        return value;
    }
}
