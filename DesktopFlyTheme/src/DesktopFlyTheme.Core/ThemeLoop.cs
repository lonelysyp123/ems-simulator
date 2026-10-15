namespace DesktopFlyTheme;

public sealed class ThemeLoop
{
    private readonly IRandomSource _random;
    private readonly WorkAreaMap _work = new();
    private readonly List<RectD> _displays = new();
    private readonly List<Fly> _flies = new();
    private readonly List<Maggot> _maggots = new();
    private readonly List<Stain> _stains = new();
    private readonly IconCache _icons = new();
    private readonly Dictionary<(int A, int B), int> _courtshipSteps = new();
    private readonly List<int> _idScratch = new();

    private RectD _primary;
    private MatingSession? _mating;
    private int _nextId = 1;
    private long _tick;
    private double _origin;
    private bool _clockStarted;
    private bool _leftDown;
    private Vec2 _cursor;
    private bool _abortedMatingThisStep;

    public ThemeLoop(IRandomSource random)
    {
        _random = random;
        var screen = new RectD(0, 0, 1920, 1080);
        ConfigureSpace(new[] { screen }, screen, new[] { screen });
    }

    public bool SurfaceAvailable { get; set; } = true;

    public bool IsShutdown { get; private set; }

    public bool RagMode { get; private set; }

    public double? SuppressionUntil { get; private set; }

    public double SimulationTime { get; private set; }

    public bool RagVisible { get; private set; }

    public Vec2 RagCursor { get; private set; }

    public int LiveCount => _flies.Count + _maggots.Count;

    public IReadOnlyList<Fly> Flies => _flies;

    public IReadOnlyList<Maggot> Maggots => _maggots;

    public IReadOnlyList<Stain> Stains => _stains;

    public MatingSession? Mating => _mating;

    public IconCache Icons => _icons;

    public void ConfigureSpace(IReadOnlyList<RectD> workAreas, RectD primaryWork, IReadOnlyList<RectD> displays)
    {
        _work.Set(workAreas);
        _primary = primaryWork;
        _displays.Clear();
        _displays.AddRange(displays);
    }

    public void SubmitIcons(IReadOnlyList<RectD> rects) => _icons.Submit(rects);

    public void Reset(double now)
    {
        if (IsShutdown)
            return;

        ClearScene();
        StartClock(now);
        var (malePos, femalePos) = InitialPlacement.Place(_primary);
        SpawnFly(Sex.Male, malePos, now, 0);
        SpawnFly(Sex.Female, femalePos, now, Math.PI);
    }

    public void Step(double now, IPointerSource pointer)
    {
        if (IsShutdown || double.IsNaN(now) || double.IsInfinity(now))
            return;
        if (!_clockStarted)
        {
            StartClock(now);
            return;
        }

        var target = (long)Math.Floor((now - _origin) * 60.0 + 1e-9);
        var guard = 0;
        while (_tick < target && guard++ < 60 * 60)
        {
            _tick++;
            StepOnce(_origin + _tick / 60.0, pointer);
        }
    }

    public void AdvanceSteps(int steps, IPointerSource pointer)
    {
        if (IsShutdown)
            return;
        if (!_clockStarted)
            StartClock(0);

        for (var i = 0; i < steps; i++)
        {
            _tick++;
            StepOnce(_origin + _tick / 60.0, pointer);
        }
    }

    public void AdvanceTo(double time, IPointerSource pointer)
    {
        if (!_clockStarted)
            StartClock(0);
        var target = (long)Math.Ceiling((time - _origin) * 60.0 - 1e-9);
        while (_tick < target)
        {
            _tick++;
            StepOnce(_origin + _tick / 60.0, pointer);
        }
    }

    public IReadOnlyList<DrawCommand> Draw()
    {
        var commands = new List<DrawCommand>();
        for (var i = 0; i < _displays.Count; i++)
            commands.Add(new FillRectDraw(_displays[i], ThemeRules.Table));

        for (var i = 0; i < _stains.Count; i++)
        {
            var stain = _stains[i];
            commands.Add(stain.Kind == StainKind.Fly
                ? new FlyStainDraw(stain.Pos, stain.Radius)
                : new MaggotStainDraw(stain.Pos, stain.Radius));
        }

        _maggots.Sort(CompareDrawOrder);
        for (var i = 0; i < _maggots.Count; i++)
            AddMaggotDraw(commands, _maggots[i]);

        _flies.Sort(CompareFlyDraw);
        for (var i = 0; i < _flies.Count; i++)
            AddFlyDraw(commands, _flies[i]);

        if (_mating != null)
        {
            var male = FindFly(_mating.MaleId);
            var female = FindFly(_mating.FemaleId);
            if (male != null && female != null)
            {
                var radius = 9 + Math.Max(male.BodyRadius(), female.BodyRadius()) + 4;
                commands.Add(new StrokeCircleDraw(_mating.Midpoint, radius, 2, ThemeRules.MatingRing));
            }
        }

        if (RagVisible)
        {
            var origin = RagCursor + new Vec2(6, 8);
            commands.Add(new FillRectDraw(new RectD(origin.X, origin.Y, origin.X + 18, origin.Y + 12), ThemeRules.Rag));
        }

        return commands;
    }

    public void Shutdown() => IsShutdown = true;

    public void RequestInitialize(double now)
    {
        if (!SurfaceAvailable || IsShutdown)
            return;
        Reset(now);
    }

    public void RequestToggleRag()
    {
        if (!SurfaceAvailable || IsShutdown)
            return;
        RagMode = !RagMode;
        if (!RagMode)
            RagVisible = false;
    }

    public void RequestClose() => Shutdown();

    internal void Begin(double now)
    {
        ClearScene();
        StartClock(now);
    }

    internal Fly SpawnFly(Sex sex, Vec2 pos, double now, double heading)
    {
        var fly = new Fly
        {
            Id = _nextId++,
            Sex = sex,
            Scale = FlyMotion.ScaleOf(_random.NextDouble()),
            Pos = pos,
            Heading = heading,
            State = FlyState.Wander,
            MatureAt = now + ThemeRules.MatureSeconds,
            NextHeadingChangeAt = now + ThemeRules.HeadingInterval,
        };
        fly.Vel = Vec2.FromAngle(heading) * FlyMotion.CruiseSpeed;
        fly.HeldVelocity = fly.Vel;
        _flies.Add(fly);
        return fly;
    }

    internal Maggot SpawnMaggot(Vec2 pos, double bornAt)
    {
        var heading = _random.NextDouble() * Math.PI * 2;
        var maggot = new Maggot
        {
            Id = _nextId++,
            Pos = pos,
            Heading = heading,
            BornAt = bornAt,
            State = MaggotState.Crawl,
            NextHeadingChangeAt = bornAt + ThemeRules.HeadingInterval,
        };
        maggot.Vel = Vec2.FromAngle(heading) * MaggotMotion.CruiseSpeed;
        maggot.HeldVelocity = maggot.Vel;
        _maggots.Add(maggot);
        return maggot;
    }

    internal void StartMatingForTest(Fly a, Fly b) => StartMating(a, b);

    internal void RemoveLastMaggot()
    {
        if (_maggots.Count > 0)
            _maggots.RemoveAt(_maggots.Count - 1);
    }

    private void ClearScene()
    {
        _flies.Clear();
        _maggots.Clear();
        _stains.Clear();
        _courtshipSteps.Clear();
        _mating = null;
        SuppressionUntil = null;
        RagMode = false;
        RagVisible = false;
        _abortedMatingThisStep = false;
    }

    private void StartClock(double now)
    {
        _origin = now;
        _tick = 0;
        _clockStarted = true;
        SimulationTime = now;
    }

    private void StepOnce(double now, IPointerSource pointer)
    {
        const double dt = ThemeRules.StepSeconds;
        SimulationTime = now;
        _icons.Sample(now);

        var sample = pointer.Poll(now);
        var edge = sample.LeftDown && !_leftDown;
        _leftDown = sample.LeftDown;
        _cursor = sample.Cursor;
        _abortedMatingThisStep = false;

        for (var i = 0; i < _maggots.Count; i++)
        {
            var maggot = _maggots[i];
            if (maggot.HitWindowEndsAt is double ends && now >= ends)
            {
                maggot.HitCount = 0;
                maggot.HitWindowEndsAt = null;
            }
        }

        if (RagMode && HitClassifier.IsOverDesktop(sample.Hit))
        {
            _stains.RemoveAll(stain => stain.IntersectsClean(_cursor));
            RagCursor = _cursor;
            RagVisible = true;
        }
        else
        {
            RagVisible = false;
        }

        if (edge && sample.Hit == HitClass.DesktopEmpty)
            ResolveClick(now);

        IntegrateMovement(now, dt);
        UpdateMatingTimer(now, dt);
        UpdateCourtship(now, dt);
        LayEggs(now);
        MetamorphoseDue(now);
        ExpireSuppression(now);
    }

    private void ResolveClick(double now)
    {
        var fly = TopFlyAt(_cursor);
        if (fly != null)
        {
            if (fly.State == FlyState.Mating
                || (_mating != null && (fly.Id == _mating.MaleId || fly.Id == _mating.FemaleId)))
            {
                AbortMating(now);
            }
            else
            {
                KillFly(fly);
            }

            return;
        }

        var maggot = TopMaggotAt(_cursor);
        if (maggot == null)
            return;

        if (maggot.HitCount == 1 && maggot.HitWindowEndsAt is double ends && now < ends)
        {
            _stains.Add(Stain.ForMaggot(maggot));
            _maggots.Remove(maggot);
            return;
        }

        FirstMaggotHit(maggot, now);
    }

    private void FirstMaggotHit(Maggot maggot, double now)
    {
        if (maggot.State == MaggotState.Hide && maggot.HideAnchor is Vec2 anchor)
        {
            maggot.Pos = anchor;
            maggot.HideAnchor = null;
        }

        var direction = maggot.Pos - _cursor;
        if (direction.Length < 1e-9)
            direction = Vec2.FromAngle(maggot.Heading);
        else
            direction = direction.Normalized();

        var distance = MaggotMotion.BounceDistanceMin
            + _random.NextDouble() * (MaggotMotion.BounceDistanceMax - MaggotMotion.BounceDistanceMin);
        maggot.HitCount = 1;
        maggot.HitWindowEndsAt = now + MaggotMotion.HitWindow;
        maggot.State = MaggotState.Bounce;
        maggot.BounceRemaining = distance;
        maggot.Heading = Math.Atan2(direction.Y, direction.X);
        maggot.Vel = direction * MaggotMotion.BounceSpeed;
        maggot.FleeEpisode = null;
        maggot.ReactionArmedAt = null;
    }

    private void KillFly(Fly fly)
    {
        _stains.Add(Stain.ForFly(fly));
        fly.CancelEggs();
        if (_mating != null && (fly.Id == _mating.MaleId || fly.Id == _mating.FemaleId))
            _mating = null;
        _flies.Remove(fly);
    }

    private Fly? TopFlyAt(Vec2 point)
    {
        Fly? best = null;
        for (var i = 0; i < _flies.Count; i++)
        {
            var fly = _flies[i];
            if (!fly.Contains(point))
                continue;
            if (best == null || fly.Pos.Y > best.Pos.Y || (fly.Pos.Y == best.Pos.Y && fly.Id > best.Id))
                best = fly;
        }

        return best;
    }

    private Maggot? TopMaggotAt(Vec2 point)
    {
        Maggot? best = null;
        for (var i = 0; i < _maggots.Count; i++)
        {
            var maggot = _maggots[i];
            if (!maggot.Contains(point))
                continue;
            if (best == null || maggot.Pos.Y > best.Pos.Y || (maggot.Pos.Y == best.Pos.Y && maggot.Id > best.Id))
                best = maggot;
        }

        return best;
    }

    private void IntegrateMovement(double now, double dt)
    {
        var snapPos = new Dictionary<int, Vec2>(_flies.Count);
        var snapState = new Dictionary<int, FlyState>(_flies.Count);
        for (var i = 0; i < _flies.Count; i++)
        {
            snapPos[_flies[i].Id] = _flies[i].Pos;
            snapState[_flies[i].Id] = _flies[i].State;
        }

        var handled = new HashSet<int>();
        if (_mating != null)
        {
            MoveMating(dt);
            handled.Add(_mating.MaleId);
            handled.Add(_mating.FemaleId);
        }

        _flies.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        for (var i = 0; i < _flies.Count; i++)
        {
            var fly = _flies[i];
            if (handled.Contains(fly.Id) || fly.State == FlyState.Mating)
                continue;
            MoveFly(fly, now, dt, snapPos, snapState, handled);
        }

        for (var i = 0; i < _maggots.Count; i++)
            MoveMaggot(_maggots[i], now, dt);
    }

    private void MoveMating(double dt)
    {
        if (_mating == null)
            return;
        var male = FindFly(_mating.MaleId);
        var female = FindFly(_mating.FemaleId);
        if (male == null || female == null)
        {
            _mating = null;
            return;
        }

        var direction = _mating.Direction;
        var midpoint = _mating.Midpoint;
        var axis = _mating.Axis;
        var half = ThemeRules.MatingSeparation * 0.5;
        var distance = ThemeRules.MatingSpeed * dt;

        bool Outside(Vec2 candidate) =>
            !_work.Contains(candidate + axis * half) || !_work.Contains(candidate - axis * half);

        var next = midpoint + direction * distance;
        if (Outside(next))
        {
            var nx = direction.X;
            var ny = direction.Y;
            if (Math.Abs(direction.X) > 1e-9 && Outside(midpoint + new Vec2(direction.X, 0) * distance))
                nx = -direction.X;
            if (Math.Abs(direction.Y) > 1e-9 && Outside(midpoint + new Vec2(0, direction.Y) * distance))
                ny = -direction.Y;
            direction = new Vec2(nx, ny);
            if (direction.Length > 1e-9)
                direction = direction.Normalized();
            next = midpoint + direction * distance;
            if (Outside(next))
                next = SlideMidpoint(midpoint, next, axis, half);
        }

        _mating.Direction = direction;
        _mating.Midpoint = next;
        var velocity = direction * ThemeRules.MatingSpeed;
        male.Pos = next - axis * half;
        female.Pos = next + axis * half;
        male.Vel = velocity;
        female.Vel = velocity;
        male.State = FlyState.Mating;
        female.State = FlyState.Mating;
    }

    private Vec2 SlideMidpoint(Vec2 from, Vec2 to, Vec2 axis, double half)
    {
        var lo = 0.0;
        var hi = 1.0;
        for (var i = 0; i < 24; i++)
        {
            var mid = (lo + hi) * 0.5;
            var point = from + (to - from) * mid;
            var inside = _work.Contains(point + axis * half) && _work.Contains(point - axis * half);
            if (inside)
                lo = mid;
            else
                hi = mid;
        }

        return from + (to - from) * lo;
    }

    private void MoveFly(
        Fly fly,
        double now,
        double dt,
        Dictionary<int, Vec2> snapPos,
        Dictionary<int, FlyState> snapState,
        HashSet<int> handled)
    {
        if (fly.State == FlyState.Hide)
        {
            if (fly.HideAnchor is Vec2 anchor && _icons.Covers(anchor))
            {
                fly.Pos = anchor;
                fly.Vel = Vec2.Zero;
                FinishFlyAlert(fly);
                return;
            }

            var anchorPoint = fly.HideAnchor ?? fly.Pos;
            fly.HideAnchor = null;
            fly.Pos = anchorPoint;
            if (Vec2.Distance(_cursor, anchorPoint) <= FlyMotion.AlertRadius)
                BeginFlyFlee(fly);
            else
                EnterFlyWander(fly);
        }

        if (fly.State != FlyState.Flee)
        {
            var inAlert = Vec2.Distance(_cursor, fly.Pos) <= FlyMotion.AlertRadius;
            if (!inAlert)
            {
                fly.ReactionArmedAt = null;
            }
            else if (fly.ReactionArmedAt == null && !fly.InAlertLastStep)
            {
                fly.ReactionArmedAt = now - dt + FlyMotion.ReactionDelay;
                fly.HeldVelocity = fly.Vel;
            }

            if (fly.ReactionArmedAt is double due)
            {
                var stepStart = now - dt;
                if (now < due)
                {
                    MoveHeld(fly, fly.HeldVelocity, dt);
                    FinishFlyAlert(fly);
                    return;
                }

                if (due > stepStart)
                    MoveHeld(fly, fly.HeldVelocity, due - stepStart);
                BeginFlyFlee(fly);
                MoveFlyFlee(fly, Math.Max(0, now - Math.Max(due, stepStart)));
                FinishFlyAlert(fly);
                return;
            }
        }

        if (fly.State == FlyState.Flee)
        {
            MoveFlyFlee(fly, dt);
            FinishFlyAlert(fly);
            return;
        }

        var partner = FindSeekPartner(fly, snapPos, snapState, handled);
        var selfMateable = Mateable(fly, fly.State, fly.Pos, now);
        if (!IsSuppressed && selfMateable && partner != null
            && Vec2.Distance(snapPos[fly.Id], snapPos[partner.Id]) <= ThemeRules.SeekRadius)
        {
            fly.State = FlyState.SeekMate;
        }
        else if (fly.State == FlyState.SeekMate)
        {
            fly.State = FlyState.Wander;
            fly.OrbitSpacing = null;
            partner = null;
        }

        if (fly.State == FlyState.SeekMate && partner != null)
        {
            MoveSeek(fly, partner, snapPos, dt, handled);
            FinishFlyAlert(fly);
            return;
        }

        MoveCruise(fly, now, dt, FlyMotion.CruiseSpeed, ThemeRules.IconPushSpeed, FlyMotion.CruiseSpeed);
        FinishFlyAlert(fly);
    }

    private void MoveSeek(Fly fly, Fly partner, Dictionary<int, Vec2> snapPos, double dt, HashSet<int> handled)
    {
        var from = snapPos[fly.Id];
        var target = snapPos[partner.Id];
        var distance = Vec2.Distance(from, target);
        if (distance < ThemeRules.CourtshipDistance && !handled.Contains(partner.Id))
        {
            fly.OrbitSpacing ??= Math.Max(distance, 1e-3);
            partner.OrbitSpacing ??= fly.OrbitSpacing;
            var spacing = fly.OrbitSpacing ?? distance;
            Orbit(fly, partner, from, target, spacing, dt);
            partner.State = FlyState.SeekMate;
            handled.Add(partner.Id);
            handled.Add(fly.Id);
            FinishFlyAlert(partner);
            return;
        }

        fly.OrbitSpacing = null;
        if (distance < 1e-9)
            return;

        var direction = (target - from).Normalized();
        fly.Heading = Math.Atan2(direction.Y, direction.X);
        fly.Vel = direction * FlyMotion.CruiseSpeed;
        ApplyPush(fly, ThemeRules.IconPushSpeed, FlyMotion.CruiseSpeed);
        fly.Pos = _work.Slide(from, from + fly.Vel * dt);
    }

    private void Orbit(Fly a, Fly b, Vec2 pa, Vec2 pb, double spacing, double dt)
    {
        var midpoint = (pa + pb) * 0.5;
        var radius = Math.Max(spacing * 0.5, 1e-3);
        var theta = ThemeRules.OrbitSpeed / radius * dt;
        var offset = (pa - midpoint).Normalized() * radius;
        var rotated = offset.Rotated(theta);
        var tangent = offset.Rotated(Math.PI * 0.5).Normalized();
        var velocity = tangent * ThemeRules.OrbitSpeed;

        Vec2 PushDelta(Fly fly, Vec2 start, Vec2 delta)
        {
            if (!CenterInIcon(start))
                return delta;
            var away = AwayFromIcon(start, fly.Heading);
            delta += away * ThemeRules.IconPushSpeed * dt;
            var max = ThemeRules.OrbitSpeed * dt;
            if (delta.Length > max)
                delta = delta.Normalized() * max;
            return delta;
        }

        var deltaA = PushDelta(a, pa, midpoint + rotated - pa);
        var deltaB = PushDelta(b, pb, midpoint - rotated - pb);
        a.Pos = _work.Slide(pa, pa + deltaA);
        b.Pos = _work.Slide(pb, pb + deltaB);
        a.Vel = velocity;
        b.Vel = velocity;
        a.Heading = Math.Atan2(velocity.Y, velocity.X);
        b.Heading = a.Heading;
        a.State = FlyState.SeekMate;
        b.State = FlyState.SeekMate;
    }

    private void MoveFlyFlee(Fly fly, double dt)
    {
        var left = dt;
        while (left > 1e-12)
        {
            var episode = fly.FleeEpisode;
            if (episode == null || fly.State != FlyState.Flee)
                return;

            var segmentLeft = FlyMotion.FleeSegmentDuration - episode.Elapsed;
            if (segmentLeft <= 1e-12)
            {
                if (Vec2.Distance(_cursor, fly.Pos) <= FlyMotion.AlertRadius)
                {
                    BeginFlyFlee(fly);
                    continue;
                }

                ExitFlyFlee(fly);
                MoveCruise(fly, SimulationTime, left, FlyMotion.CruiseSpeed, ThemeRules.IconPushSpeed, FlyMotion.CruiseSpeed);
                return;
            }

            var step = Math.Min(left, segmentLeft);
            var distance = FlyMotion.DistanceDuring(episode.Elapsed, step);
            var direction = Vec2.FromAngle(episode.DirectionAngle);
            var delta = ClipDelta(fly.Pos, direction * distance);
            if (TryHideFly(fly, fly.Pos, delta))
                return;

            fly.Pos += delta;
            fly.Heading = episode.DirectionAngle;
            fly.Vel = direction * FlyMotion.SpeedAt(episode.Elapsed);
            episode.Elapsed += step;
            left -= step;
        }
    }

    private void ExitFlyFlee(Fly fly)
    {
        fly.FleeEpisode = null;
        fly.State = FlyState.Wander;
        fly.ReactionArmedAt = null;
        if (!IsSuppressed && Mateable(fly, FlyState.Wander, fly.Pos, SimulationTime))
        {
            var partner = FindSeekPartner(fly, LivePositions(), LiveStates(), new HashSet<int>());
            if (partner != null && Vec2.Distance(fly.Pos, partner.Pos) <= ThemeRules.SeekRadius)
                fly.State = FlyState.SeekMate;
        }
    }

    private bool TryHideFly(Fly fly, Vec2 start, Vec2 delta)
    {
        var length = delta.Length;
        if (length + 1e-9 < ThemeRules.HideSampleSpacing)
            return false;

        var direction = delta / length;
        for (var distance = ThemeRules.HideSampleSpacing; distance <= length + 1e-6; distance += ThemeRules.HideSampleSpacing)
        {
            var point = start + direction * Math.Min(distance, length);
            if (!FlyBodyInsideIcon(fly, point))
                continue;

            fly.State = FlyState.Hide;
            fly.HideAnchor = point;
            fly.Pos = point;
            fly.Vel = Vec2.Zero;
            fly.FleeEpisode = null;
            fly.ReactionArmedAt = null;
            return true;
        }

        return false;
    }

    private void MoveMaggot(Maggot maggot, double now, double dt)
    {
        if (maggot.State == MaggotState.Hide)
        {
            if (maggot.HideAnchor is Vec2 anchor && _icons.Covers(anchor))
            {
                maggot.Pos = anchor;
                maggot.Vel = Vec2.Zero;
                maggot.InAlertLastStep = false;
                return;
            }

            var anchorPoint = maggot.HideAnchor ?? maggot.Pos;
            maggot.HideAnchor = null;
            maggot.Pos = anchorPoint;
            if (Vec2.Distance(_cursor, anchorPoint) <= MaggotMotion.AlertRadius)
                BeginMaggotFlee(maggot);
            else
                EnterMaggotCrawl(maggot);
        }

        if (maggot.State == MaggotState.Bounce)
        {
            MoveBounce(maggot, dt);
            maggot.InAlertLastStep = Vec2.Distance(_cursor, maggot.Pos) <= MaggotMotion.AlertRadius;
            return;
        }

        if (maggot.State != MaggotState.Flee)
        {
            var inAlert = Vec2.Distance(_cursor, maggot.Pos) <= MaggotMotion.AlertRadius;
            if (!inAlert)
            {
                maggot.ReactionArmedAt = null;
            }
            else if (maggot.ReactionArmedAt == null && !maggot.InAlertLastStep)
            {
                maggot.ReactionArmedAt = now - dt + MaggotMotion.ReactionDelay;
                maggot.HeldVelocity = maggot.Vel;
            }

            if (maggot.ReactionArmedAt is double due)
            {
                var stepStart = now - dt;
                if (now < due)
                {
                    MoveMaggotHeld(maggot, dt);
                    FinishMaggotAlert(maggot);
                    return;
                }

                if (due > stepStart)
                {
                    var held = maggot.HeldVelocity;
                    var partial = _work.Slide(maggot.Pos, maggot.Pos + held * (due - stepStart));
                    maggot.Pos = partial;
                }

                BeginMaggotFlee(maggot);
                MoveMaggotFlee(maggot, Math.Max(0, now - Math.Max(due, stepStart)));
                FinishMaggotAlert(maggot);
                return;
            }
        }

        if (maggot.State == MaggotState.Flee)
        {
            MoveMaggotFlee(maggot, dt);
            FinishMaggotAlert(maggot);
            return;
        }

        MoveMaggotCruise(maggot, now, dt);
        FinishMaggotAlert(maggot);
    }

    private void MoveBounce(Maggot maggot, double dt)
    {
        if (maggot.BounceRemaining <= 1e-6)
        {
            EndBounce(maggot);
            return;
        }

        var step = Math.Min(MaggotMotion.BounceSpeed * dt, maggot.BounceRemaining);
        var direction = Vec2.FromAngle(maggot.Heading);
        var target = maggot.Pos + direction * step;
        var next = _work.Slide(maggot.Pos, target);
        var moved = Vec2.Distance(maggot.Pos, next);
        maggot.Pos = next;
        maggot.BounceRemaining -= moved;
        if (moved + 1e-4 < step)
            maggot.BounceRemaining = 0;
        maggot.Vel = direction * MaggotMotion.BounceSpeed;
        if (maggot.BounceRemaining <= 1e-6)
        {
            maggot.BounceRemaining = 0;
            EndBounce(maggot);
        }
    }

    private void EndBounce(Maggot maggot)
    {
        var windowOpen = maggot.HitCount > 0 && maggot.HitWindowEndsAt != null;
        if (windowOpen && Vec2.Distance(_cursor, maggot.Pos) <= MaggotMotion.AlertRadius)
            BeginMaggotFlee(maggot);
        else
            EnterMaggotCrawl(maggot);
    }

    private void MoveMaggotFlee(Maggot maggot, double dt)
    {
        var episode = maggot.FleeEpisode ?? new FleeEpisode();
        maggot.FleeEpisode = episode;
        var inAlert = Vec2.Distance(_cursor, maggot.Pos) <= MaggotMotion.AlertRadius;
        if (inAlert)
        {
            if (episode.Recovering)
            {
                episode.Recovering = false;
                episode.Elapsed = 0;
            }

            var away = maggot.Pos - _cursor;
            var baseAngle = away.Length < 1e-9 ? maggot.Heading : Math.Atan2(away.Y, away.X);
            var angle = baseAngle + episode.OffsetAngle;
            var speed = MaggotMotion.ChaseSpeed(episode.Elapsed);
            var direction = Vec2.FromAngle(angle);
            var delta = ClipDelta(maggot.Pos, direction * speed * dt);
            if (TryHideMaggot(maggot, maggot.Pos, delta))
                return;
            maggot.Pos += delta;
            maggot.Heading = angle;
            maggot.Vel = direction * speed;
            episode.Elapsed += dt;
            return;
        }

        if (!episode.Recovering)
        {
            episode.Recovering = true;
            episode.RecoverElapsed = 0;
            episode.RecoverFromSpeed = maggot.Vel.Length;
        }

        var recoverT = MaggotMotion.RecoverDuration <= 0
            ? 1
            : Math.Clamp(episode.RecoverElapsed / MaggotMotion.RecoverDuration, 0, 1);
        var recoverSpeed = episode.RecoverFromSpeed + (MaggotMotion.CruiseSpeed - episode.RecoverFromSpeed) * recoverT;
        var recoverDir = Vec2.FromAngle(maggot.Heading);
        var recoverDelta = ClipDelta(maggot.Pos, recoverDir * recoverSpeed * dt);
        if (TryHideMaggot(maggot, maggot.Pos, recoverDelta))
            return;
        maggot.Pos += recoverDelta;
        maggot.Vel = recoverDir * recoverSpeed;
        episode.RecoverElapsed += dt;
        if (episode.RecoverElapsed + 1e-12 >= MaggotMotion.RecoverDuration)
            EnterMaggotCrawl(maggot);
    }

    private bool TryHideMaggot(Maggot maggot, Vec2 start, Vec2 delta)
    {
        if (maggot.State != MaggotState.Flee)
            return false;
        var length = delta.Length;
        if (length + 1e-9 < ThemeRules.HideSampleSpacing)
            return false;

        var direction = delta / length;
        for (var distance = ThemeRules.HideSampleSpacing; distance <= length + 1e-6; distance += ThemeRules.HideSampleSpacing)
        {
            var point = start + direction * Math.Min(distance, length);
            if (!MaggotBodyInsideIcon(maggot, point))
                continue;
            maggot.State = MaggotState.Hide;
            maggot.HideAnchor = point;
            maggot.Pos = point;
            maggot.Vel = Vec2.Zero;
            maggot.FleeEpisode = null;
            maggot.ReactionArmedAt = null;
            return true;
        }

        return false;
    }

    private void MoveCruise(Fly fly, double now, double dt, double speed, double push, double maxSpeed)
    {
        if (now >= fly.NextHeadingChangeAt)
        {
            fly.Heading = _random.NextDouble() * Math.PI * 2;
            while (fly.NextHeadingChangeAt <= now)
                fly.NextHeadingChangeAt += ThemeRules.HeadingInterval;
        }

        if (TryInward(fly.Pos, out var inward))
            fly.Heading = inward;

        fly.Vel = Vec2.FromAngle(fly.Heading) * speed;
        ApplyPush(fly, push, maxSpeed);
        fly.Pos = _work.Slide(fly.Pos, fly.Pos + fly.Vel * dt);
    }

    private void MoveMaggotCruise(Maggot maggot, double now, double dt)
    {
        if (now >= maggot.NextHeadingChangeAt)
        {
            maggot.Heading = _random.NextDouble() * Math.PI * 2;
            while (maggot.NextHeadingChangeAt <= now)
                maggot.NextHeadingChangeAt += ThemeRules.HeadingInterval;
        }

        if (TryInward(maggot.Pos, out var inward))
            maggot.Heading = inward;

        maggot.Vel = Vec2.FromAngle(maggot.Heading) * MaggotMotion.CruiseSpeed;
        ApplyMaggotPush(maggot);
        maggot.Pos = _work.Slide(maggot.Pos, maggot.Pos + maggot.Vel * dt);
    }

    private void MoveHeld(Fly fly, Vec2 velocity, double dt)
    {
        fly.Vel = velocity;
        fly.Pos = _work.Slide(fly.Pos, fly.Pos + velocity * dt);
    }

    private void MoveMaggotHeld(Maggot maggot, double dt)
    {
        maggot.Vel = maggot.HeldVelocity;
        maggot.Pos = _work.Slide(maggot.Pos, maggot.Pos + maggot.HeldVelocity * dt);
    }

    private void ApplyPush(Fly fly, double pushSpeed, double maxSpeed)
    {
        if (fly.State is FlyState.Flee or FlyState.Hide or FlyState.Mating)
            return;
        if (!CenterInIcon(fly.Pos))
            return;
        fly.Vel += AwayFromIcon(fly.Pos, fly.Heading) * pushSpeed;
        if (fly.Vel.Length > maxSpeed)
            fly.Vel = fly.Vel.Normalized() * maxSpeed;
    }

    private void ApplyMaggotPush(Maggot maggot)
    {
        if (maggot.State is MaggotState.Flee or MaggotState.Hide or MaggotState.Bounce)
            return;
        if (!CenterInIcon(maggot.Pos))
            return;
        maggot.Vel += AwayFromIcon(maggot.Pos, maggot.Heading) * MaggotMotion.PushSpeed;
        if (maggot.Vel.Length > MaggotMotion.CruiseSpeed)
            maggot.Vel = maggot.Vel.Normalized() * MaggotMotion.CruiseSpeed;
    }

    private bool CenterInIcon(Vec2 point)
    {
        var rects = _icons.Rects;
        for (var i = 0; i < rects.Count; i++)
        {
            if (rects[i].Contains(point))
                return true;
        }

        return false;
    }

    private Vec2 AwayFromIcon(Vec2 point, double heading)
    {
        var rects = _icons.Rects;
        for (var i = 0; i < rects.Count; i++)
        {
            if (!rects[i].Contains(point))
                continue;
            var away = point - rects[i].Center;
            return away.Length < 1e-6 ? Vec2.FromAngle(heading) : away.Normalized();
        }

        return Vec2.FromAngle(heading);
    }

    private bool FlyBodyInsideIcon(Fly fly, Vec2 point)
    {
        var rects = _icons.Rects;
        var radius = fly.BodyRadius();
        for (var i = 0; i < rects.Count; i++)
        {
            if (rects[i].ContainsCircle(point, radius))
                return true;
        }

        return false;
    }

    private bool MaggotBodyInsideIcon(Maggot maggot, Vec2 point)
    {
        var rects = _icons.Rects;
        for (var i = 0; i < rects.Count; i++)
        {
            if (maggot.FullyInside(rects[i], point))
                return true;
        }

        return false;
    }

    private Vec2 ClipDelta(Vec2 pos, Vec2 delta) => _work.Slide(pos, pos + delta) - pos;

    private bool TryInward(Vec2 point, out double heading)
    {
        var x = 0.0;
        var y = 0.0;
        var probe = ThemeRules.BoundaryProbe;
        if (!_work.Contains(new Vec2(point.X - probe, point.Y)))
            x += 1;
        if (!_work.Contains(new Vec2(point.X + probe, point.Y)))
            x -= 1;
        if (!_work.Contains(new Vec2(point.X, point.Y - probe)))
            y += 1;
        if (!_work.Contains(new Vec2(point.X, point.Y + probe)))
            y -= 1;
        if (Math.Abs(x) < 1e-9 && Math.Abs(y) < 1e-9)
        {
            heading = 0;
            return false;
        }

        heading = Math.Atan2(y, x);
        return true;
    }

    private void BeginFlyFlee(Fly fly)
    {
        var away = fly.Pos - _cursor;
        var baseAngle = away.Length < 1e-9 ? fly.Heading : Math.Atan2(away.Y, away.X);
        var offset = (_random.NextDouble() * 2 - 1) * FlyMotion.FleeAngleRadians;
        fly.State = FlyState.Flee;
        fly.ReactionArmedAt = null;
        fly.HideAnchor = null;
        fly.OrbitSpacing = null;
        fly.MateId = null;
        fly.FleeEpisode = new FleeEpisode
        {
            DirectionAngle = baseAngle + offset,
            Elapsed = 0,
        };
        fly.Heading = fly.FleeEpisode.DirectionAngle;
    }

    private void BeginMaggotFlee(Maggot maggot)
    {
        var offset = (_random.NextDouble() * 2 - 1) * MaggotMotion.FleeAngleRadians;
        maggot.State = MaggotState.Flee;
        maggot.ReactionArmedAt = null;
        maggot.HideAnchor = null;
        maggot.FleeEpisode = new FleeEpisode
        {
            OffsetAngle = offset,
            Elapsed = 0,
        };
    }

    private void EnterFlyWander(Fly fly)
    {
        fly.State = FlyState.Wander;
        fly.FleeEpisode = null;
        fly.ReactionArmedAt = null;
        fly.OrbitSpacing = null;
        fly.MateId = null;
        fly.Vel = Vec2.FromAngle(fly.Heading) * FlyMotion.CruiseSpeed;
    }

    private void EnterMaggotCrawl(Maggot maggot)
    {
        maggot.State = MaggotState.Crawl;
        maggot.FleeEpisode = null;
        maggot.ReactionArmedAt = null;
        maggot.Vel = Vec2.FromAngle(maggot.Heading) * MaggotMotion.CruiseSpeed;
    }

    private void FinishFlyAlert(Fly fly)
    {
        var inAlert = fly.State != FlyState.Hide
            && Vec2.Distance(_cursor, fly.Pos) <= FlyMotion.AlertRadius;
        if (!inAlert && fly.State != FlyState.Flee)
            fly.ReactionArmedAt = null;
        fly.InAlertLastStep = inAlert;
    }

    private void FinishMaggotAlert(Maggot maggot)
    {
        var inAlert = maggot.State != MaggotState.Hide
            && Vec2.Distance(_cursor, maggot.Pos) <= MaggotMotion.AlertRadius;
        if (!inAlert && maggot.State != MaggotState.Flee)
            maggot.ReactionArmedAt = null;
        maggot.InAlertLastStep = inAlert;
    }

    private Fly? FindSeekPartner(
        Fly self,
        Dictionary<int, Vec2> snapPos,
        Dictionary<int, FlyState> snapState,
        HashSet<int> handled)
    {
        if (!snapPos.ContainsKey(self.Id))
            return null;

        Fly? best = null;
        var bestDistance = ThemeRules.SeekRadius + 1;
        for (var i = 0; i < _flies.Count; i++)
        {
            var other = _flies[i];
            if (other.Id == self.Id || other.Sex == self.Sex || handled.Contains(other.Id))
                continue;
            if (!snapState.TryGetValue(other.Id, out var state) || !snapPos.TryGetValue(other.Id, out var pos))
                continue;
            if (!Mateable(other, state, pos, SimulationTime))
                continue;
            var distance = Vec2.Distance(snapPos[self.Id], pos);
            if (distance <= ThemeRules.SeekRadius && (distance < bestDistance || (Math.Abs(distance - bestDistance) < 1e-9 && (best == null || other.Id < best.Id))))
            {
                best = other;
                bestDistance = distance;
            }
        }

        return best;
    }

    private bool Mateable(Fly fly, FlyState state, Vec2 pos, double now)
    {
        if (IsSuppressed)
            return false;
        if (!fly.IsMature(now) || fly.IsOnCooldown(now))
            return false;
        if (state is not (FlyState.Wander or FlyState.SeekMate))
            return false;
        if (Vec2.Distance(_cursor, pos) <= FlyMotion.AlertRadius)
            return false;
        if (fly.ReactionArmedAt is double armed && now < armed)
            return false;
        return true;
    }

    private Dictionary<int, Vec2> LivePositions()
    {
        var map = new Dictionary<int, Vec2>(_flies.Count);
        for (var i = 0; i < _flies.Count; i++)
            map[_flies[i].Id] = _flies[i].Pos;
        return map;
    }

    private Dictionary<int, FlyState> LiveStates()
    {
        var map = new Dictionary<int, FlyState>(_flies.Count);
        for (var i = 0; i < _flies.Count; i++)
            map[_flies[i].Id] = _flies[i].State;
        return map;
    }

    private void UpdateMatingTimer(double now, double dt)
    {
        if (_mating == null || _abortedMatingThisStep)
            return;

        var male = FindFly(_mating.MaleId);
        var female = FindFly(_mating.FemaleId);
        if (male == null || female == null)
        {
            _mating = null;
            return;
        }

        if (Vec2.Distance(_cursor, male.Pos) <= ThemeRules.InterruptRadius
            || Vec2.Distance(_cursor, female.Pos) <= ThemeRules.InterruptRadius)
        {
            AbortMating(now);
            return;
        }

        _mating.Elapsed += dt;
        if (_mating.Elapsed + 1e-9 >= ThemeRules.MatingSeconds)
            SucceedMating(now);
    }

    private void UpdateCourtship(double now, double dt)
    {
        var next = new Dictionary<(int A, int B), int>();
        for (var i = 0; i < _flies.Count; i++)
        {
            for (var j = i + 1; j < _flies.Count; j++)
            {
                var a = _flies[i];
                var b = _flies[j];
                if (a.Sex == b.Sex)
                    continue;
                if (IsInMating(a) || IsInMating(b))
                    continue;

                var key = a.Id < b.Id ? (a.Id, b.Id) : (b.Id, a.Id);
                if (!CourtshipHolds(a, b))
                    continue;

                var steps = _courtshipSteps.GetValueOrDefault(key) + 1;
                if (steps >= ThemeRules.CourtshipSteps)
                {
                    if (Vec2.Distance(_cursor, a.Pos) <= ThemeRules.InterruptRadius
                        || Vec2.Distance(_cursor, b.Pos) <= ThemeRules.InterruptRadius)
                    {
                        SettleAfterMating(a, now);
                        SettleAfterMating(b, now);
                    }
                    else
                    {
                        StartMating(a, b);
                    }

                    continue;
                }

                next[key] = steps;
            }
        }

        _courtshipSteps.Clear();
        foreach (var pair in next)
            _courtshipSteps[pair.Key] = pair.Value;

        _ = dt;
    }

    private bool CourtshipHolds(Fly a, Fly b)
    {
        if (IsSuppressed)
            return false;
        if (a.Sex == b.Sex)
            return false;
        if (!a.IsMature(SimulationTime) || !b.IsMature(SimulationTime))
            return false;
        if (a.IsOnCooldown(SimulationTime) || b.IsOnCooldown(SimulationTime))
            return false;
        if (a.State is not (FlyState.Wander or FlyState.SeekMate))
            return false;
        if (b.State is not (FlyState.Wander or FlyState.SeekMate))
            return false;
        if (Vec2.Distance(a.Pos, b.Pos) >= ThemeRules.CourtshipDistance)
            return false;
        if (Vec2.Distance(_cursor, a.Pos) <= FlyMotion.AlertRadius
            || Vec2.Distance(_cursor, b.Pos) <= FlyMotion.AlertRadius)
            return false;
        if (a.ReactionArmedAt is double armA && SimulationTime < armA)
            return false;
        if (b.ReactionArmedAt is double armB && SimulationTime < armB)
            return false;
        return true;
    }

    private void StartMating(Fly a, Fly b)
    {
        var male = a.Sex == Sex.Male ? a : b;
        var female = a.Sex == Sex.Female ? a : b;
        var midpoint = (male.Pos + female.Pos) * 0.5;
        var delta = female.Pos - male.Pos;
        var axis = delta.Length < 1e-6 ? new Vec2(1, 0) : delta.Normalized();
        var half = ThemeRules.MatingSeparation * 0.5;
        male.Pos = midpoint - axis * half;
        female.Pos = midpoint + axis * half;
        var angle = _random.NextDouble() * Math.PI * 2;
        _mating = new MatingSession
        {
            MaleId = male.Id,
            FemaleId = female.Id,
            Elapsed = 0,
            Direction = Vec2.FromAngle(angle),
            Axis = axis,
            Midpoint = midpoint,
        };
        male.State = FlyState.Mating;
        female.State = FlyState.Mating;
        male.MateId = female.Id;
        female.MateId = male.Id;
        male.FleeEpisode = null;
        female.FleeEpisode = null;
        male.ReactionArmedAt = null;
        female.ReactionArmedAt = null;
        male.HideAnchor = null;
        female.HideAnchor = null;
        _courtshipSteps.Clear();
    }

    private void SucceedMating(double now)
    {
        if (_mating == null)
            return;
        var male = FindFly(_mating.MaleId);
        var female = FindFly(_mating.FemaleId);
        _mating = null;
        if (male != null)
            male.CooldownUntil = now + ThemeRules.CooldownSeconds;
        if (female != null)
        {
            female.CooldownUntil = now + ThemeRules.CooldownSeconds;
            female.ScheduleEggs(now, _random);
        }

        if (male != null)
            SettleAfterMating(male, now);
        if (female != null)
            SettleAfterMating(female, now);
    }

    private void AbortMating(double now)
    {
        if (_mating == null)
            return;
        var male = FindFly(_mating.MaleId);
        var female = FindFly(_mating.FemaleId);
        _mating = null;
        _abortedMatingThisStep = true;
        _courtshipSteps.Clear();
        if (male != null)
            SettleAfterMating(male, now);
        if (female != null)
            SettleAfterMating(female, now);
    }

    private void SettleAfterMating(Fly fly, double now)
    {
        fly.MateId = null;
        fly.FleeEpisode = null;
        fly.OrbitSpacing = null;
        fly.State = FlyState.Wander;
        if (Vec2.Distance(_cursor, fly.Pos) <= FlyMotion.AlertRadius)
        {
            fly.ReactionArmedAt = now + FlyMotion.ReactionDelay;
            fly.HeldVelocity = fly.Vel.Length < 1e-6
                ? Vec2.FromAngle(fly.Heading) * FlyMotion.CruiseSpeed
                : fly.Vel;
            fly.InAlertLastStep = true;
        }
        else
        {
            fly.ReactionArmedAt = null;
            fly.InAlertLastStep = false;
            fly.Vel = Vec2.FromAngle(fly.Heading) * FlyMotion.CruiseSpeed;
        }
    }

    private void LayEggs(double now)
    {
        _idScratch.Clear();
        for (var i = 0; i < _flies.Count; i++)
        {
            if (_flies[i].EggTimes.Count > 0)
                _idScratch.Add(_flies[i].Id);
        }

        _idScratch.Sort();
        for (var i = 0; i < _idScratch.Count; i++)
        {
            var mother = FindFly(_idScratch[i]);
            if (mother == null)
                continue;
            while (mother.EggTimes.Count > 0 && mother.EggTimes[0] <= now)
            {
                mother.EggTimes.RemoveAt(0);
                if (LiveCount >= ThemeRules.PopulationCap)
                    continue;

                SpawnMaggot(mother.Pos, now);
                if (!IsSuppressed && LiveCount >= ThemeRules.PopulationCap)
                    BeginSuppression(now);
            }
        }
    }

    private void MetamorphoseDue(double now)
    {
        _idScratch.Clear();
        for (var i = 0; i < _maggots.Count; i++)
        {
            if (now >= _maggots[i].BornAt + MaggotMotion.MetamorphosisSeconds)
                _idScratch.Add(_maggots[i].Id);
        }

        _idScratch.Sort();
        for (var i = 0; i < _idScratch.Count; i++)
        {
            var maggot = FindMaggot(_idScratch[i]);
            if (maggot != null)
                Metamorphose(maggot, now);
        }
    }

    private void Metamorphose(Maggot maggot, double now)
    {
        var pos = maggot.Pos;
        _maggots.Remove(maggot);
        var sex = _random.NextDouble() < 0.5 ? Sex.Male : Sex.Female;
        var heading = _random.NextDouble() * Math.PI * 2;
        var fly = SpawnFly(sex, pos, now, heading);
        fly.MatureAt = now + ThemeRules.MatureSeconds;
        fly.State = FlyState.Wander;
        if (Vec2.Distance(_cursor, pos) <= FlyMotion.AlertRadius)
        {
            fly.ReactionArmedAt = now + FlyMotion.ReactionDelay;
            fly.HeldVelocity = fly.Vel;
            fly.InAlertLastStep = true;
        }
    }

    private void BeginSuppression(double now)
    {
        if (IsSuppressed)
            return;

        SuppressionUntil = now + ThemeRules.SuppressionSeconds;
        if (_mating != null)
            AbortMating(now);
        _courtshipSteps.Clear();
        for (var i = 0; i < _flies.Count; i++)
        {
            var fly = _flies[i];
            if (fly.State != FlyState.SeekMate)
                continue;
            if (Vec2.Distance(_cursor, fly.Pos) <= FlyMotion.AlertRadius)
                BeginFlyFlee(fly);
            else
                EnterFlyWander(fly);
        }
    }

    private void ExpireSuppression(double now)
    {
        if (SuppressionUntil is not double until || now < until)
            return;
        SuppressionUntil = LiveCount >= ThemeRules.PopulationCap
            ? now + ThemeRules.SuppressionSeconds
            : null;
    }

    private bool IsSuppressed => SuppressionUntil != null;

    private bool IsInMating(Fly fly) =>
        fly.State == FlyState.Mating
        || (_mating != null && (fly.Id == _mating.MaleId || fly.Id == _mating.FemaleId));

    private Fly? FindFly(int id)
    {
        for (var i = 0; i < _flies.Count; i++)
        {
            if (_flies[i].Id == id)
                return _flies[i];
        }

        return null;
    }

    private Maggot? FindMaggot(int id)
    {
        for (var i = 0; i < _maggots.Count; i++)
        {
            if (_maggots[i].Id == id)
                return _maggots[i];
        }

        return null;
    }

    private static int CompareDrawOrder(Maggot a, Maggot b)
    {
        var byY = a.Pos.Y.CompareTo(b.Pos.Y);
        return byY != 0 ? byY : a.Id.CompareTo(b.Id);
    }

    private static int CompareFlyDraw(Fly a, Fly b)
    {
        var byY = a.Pos.Y.CompareTo(b.Pos.Y);
        return byY != 0 ? byY : a.Id.CompareTo(b.Id);
    }

    private static void AddFlyDraw(List<DrawCommand> commands, Fly fly)
    {
        var diameter = fly.BodyDiameter;
        var forward = Vec2.FromAngle(fly.Heading);
        var perp = new Vec2(-forward.Y, forward.X);
        var offset = perp * (diameter * 0.18);
        var rx = diameter * 0.35;
        var ry = diameter * 0.175;
        commands.Add(new FillEllipseDraw(fly.Pos + offset, rx, ry, fly.Heading, ThemeRules.Wing));
        commands.Add(new FillEllipseDraw(fly.Pos - offset, rx, ry, fly.Heading, ThemeRules.Wing));
        commands.Add(new FillCircleDraw(fly.Pos, fly.BodyRadius(), ThemeRules.FlyBody));
        var dot = fly.Sex == Sex.Male ? ThemeRules.MaleDot : ThemeRules.FemaleDot;
        commands.Add(new FillCircleDraw(fly.Pos, diameter * 0.11, dot));
    }

    private static void AddMaggotDraw(List<DrawCommand> commands, Maggot maggot)
    {
        commands.Add(new FillEllipseDraw(
            maggot.Pos,
            MaggotMotion.Length * 0.5,
            MaggotMotion.Width * 0.5,
            maggot.Heading,
            ThemeRules.MaggotFill));
        commands.Add(new StrokeEllipseDraw(
            maggot.Pos,
            MaggotMotion.Length * 0.5,
            MaggotMotion.Width * 0.5,
            maggot.Heading,
            1,
            ThemeRules.MaggotStroke));
    }
}
