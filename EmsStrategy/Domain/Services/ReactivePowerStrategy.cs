using EssSimulator.EmsStrategy.Application;
using EssSimulator.EmsStrategy.Domain.Algorithms;

namespace EssSimulator.EmsStrategy.Domain.Services;

/// <summary>
/// 无功策略：开环无 PI/下垂；闭环固定叠加 ΔQ 下垂；PF/恒压生成 Q_base 后走闭环链路。
/// </summary>
public sealed class ReactivePowerStrategy
{
    private readonly SlopeLimiter _slope = new();
    private readonly PidController _pi = new();
    private readonly PidController _vPid = new();
    private readonly AuxActionMachine _droop = new();
    private ReactiveMode _mode = ReactiveMode.CloseLoopFixed;
    private double _lastPi;
    private double _lastQBase;
    private double _lastTarget;

    public ActionState DroopAction => _droop.State;
    public double DroopDeltaKvar => _droop.State == ActionState.Action ? _droop.Output : 0;
    public double LastPiOutput => _lastPi;
    public double LastQBase => _lastQBase;
    public double LastTarget => _lastTarget;

    public void ApplyConfig(EmsStrategyConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var slope = config.ReactiveSlope ?? config.Slope;
        _slope.Configure(slope.Enabled, slope.RiseKwPerSec, slope.FallKwPerSec);
        _pi.Configure(config.ReactivePid ?? config.ActivePid);
        _vPid.Configure(new PidConfig
        {
            Kp = Math.Max(0, config.VoltageKp),
            Ki = 0,
            Kb = 0,
            Period = TimeSpan.FromMilliseconds(200),
            DeadbandKw = 0,
            OutMinKw = 0,
            OutMaxKw = Math.Max(config.VoltageSetV * 2, 1),
            Discretization = PidDiscretization.Dt
        });
    }

    public void Reset()
    {
        _slope.Reset();
        _pi.Reset();
        _vPid.Reset();
        _droop.Reset();
        _lastPi = 0;
        _lastQBase = 0;
        _lastTarget = 0;
    }

    public double Step(EmsStrategyConfig config, PlantMeasurements meas, TimeSpan dt, double otherAxisTarget = 0)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(meas);

        if (_mode != config.ReactiveMode)
        {
            Reset();
            _mode = config.ReactiveMode;
        }

        ApplyConfig(config);

        if (config.ReactiveMode == ReactiveMode.OpenLoopFixed)
            return StepOpenLoop(config, meas, dt, otherAxisTarget);

        return StepClosedFamily(config, meas, dt, otherAxisTarget);
    }

    private double StepOpenLoop(EmsStrategyConfig config, PlantMeasurements meas, TimeSpan dt, double otherAxisTarget)
    {
        _droop.Reset();
        double set = ResolveFixed(config);
        _lastQBase = set;
        _lastTarget = set;
        double storageQ = SumMeasuredReactive(meas);
        double sloped = _slope.Step(set, storageQ, dt);
        double limited = config.ApparentLimitEnabled
            ? ApparentPowerLimiter.LimitOpen(
                meas.PccReactivePowerKvar, meas.PccActivePowerKw, sloped, storageQ, config.ApparentRatedKva)
            : sloped;
        double damped = config.DampEnabled
            ? ApparentPowerLimiter.DampOpen(
                storageQ, limited, meas.PccReactivePowerKvar, otherAxisTarget, config.ApparentRatedKva)
            : limited;
        _lastPi = damped;
        return damped;
    }

    private double StepClosedFamily(EmsStrategyConfig config, PlantMeasurements meas, TimeSpan dt, double otherAxisTarget)
    {
        double qBase = config.ReactiveMode switch
        {
            ReactiveMode.PowerFactor => PfToReactiveConverter.FromPf(
                meas.PccActivePowerKw, config.PowerFactorSet, config.PfSign),
            ReactiveMode.VoltageFixed => VoltageFixedQ(config, meas),
            _ => ResolveFixed(config)
        };

        double prevDroop = DroopDeltaKvar;
        double droopDelta = 0;
        bool droopOn = config.ReactiveEnable
            && config.VoltageDroop.Enabled
            && config.ReactiveMode == ReactiveMode.CloseLoopFixed;
        var droopCfg = config.VoltageDroop;
        if (droopOn)
        {
            double uMeas = VoltageDroopCalculator.SelectMeasuredVoltage(
                meas.PccLineVoltageV, meas.StationBus35LineVoltageV, droopCfg.RatedVoltageV);
            bool outside = VoltageDroopCalculator.IsOutsideDeadband(uMeas, droopCfg);
            double qRated = config.PlantRatedKw > 0 ? config.PlantRatedKw : config.ApparentRatedKva;
            droopDelta = _droop.Step(
                outside,
                dt,
                droopCfg.ControlCycle,
                droopCfg.ResetTime,
                () => VoltageDroopCalculator.ComputeDeltaKvar(uMeas, qRated, droopCfg));
            if (_droop.State != ActionState.Action)
                droopDelta = 0;
        }
        else
        {
            _droop.Reset();
        }

        _lastQBase = qBase;
        double target = qBase + droopDelta;
        _lastTarget = target;
        bool auxJump = Math.Abs(droopDelta - prevDroop) > 1e-6;
        double sloped = _slope.Step(target, meas.PccReactivePowerKvar, dt);
        double limited = config.ApparentLimitEnabled
            ? ApparentPowerLimiter.LimitGrid(sloped, meas.PccActivePowerKw, config.ApparentRatedKva)
            : sloped;
        double damped = config.DampEnabled
            ? ApparentPowerLimiter.Damp(
                meas.PccReactivePowerKvar, limited, sloped, otherAxisTarget, config.ApparentRatedKva)
            : limited;
        double storageQ = SumMeasuredReactive(meas);
        var pidCfg = config.ReactivePid ?? config.ActivePid;
        if (pidCfg.Enabled)
        {
            _lastPi = _pi.Compute(damped, meas.PccReactivePowerKvar, storageQ, dt, force: auxJump);
        }
        else
        {
            _pi.Reset();
            _lastPi = damped;
        }
        return _lastPi;
    }

    private double VoltageFixedQ(EmsStrategyConfig config, PlantMeasurements meas)
    {
        double uMeas = VoltageDroopCalculator.SelectMeasuredVoltage(
            meas.PccLineVoltageV, meas.StationBus35LineVoltageV, config.VoltageSetV);
        double uOut = _vPid.PCompute(config.VoltageSetV, uMeas);
        return (uOut - uMeas) * config.VoltageFixedK + meas.PccReactivePowerKvar;
    }

    private static double SumMeasuredReactive(PlantMeasurements meas)
    {
        double s = 0;
        foreach (var b in meas.Branches)
            s += b.MeasuredReactiveKvar;
        return s;
    }

    private static double ResolveFixed(EmsStrategyConfig config) =>
        config.LocalRemote == LocalRemote.Remote ? config.RemoteReactiveSetKvar : config.LocalReactiveSetKvar;
}
