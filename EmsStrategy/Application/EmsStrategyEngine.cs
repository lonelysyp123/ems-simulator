using EssSimulator.EmsStrategy.Domain;
using EssSimulator.EmsStrategy.Domain.Algorithms;
using EssSimulator.EmsStrategy.Domain.Services;

namespace EssSimulator.EmsStrategy.Application;

/// <summary>
/// 电站 EMS 策略编排。差异相对原 C：无双线程 flag；用显式 dt 步进；
/// PI 默认兼容 Period 采样，可切 Dt 模式。
/// </summary>
public sealed class EmsStrategyEngine : IEmsStrategyEngine
{
    private readonly ActivePowerStrategy _active = new();
    private readonly ReactivePowerStrategy _reactive = new();
    private EmsStrategyConfig _config = EmsStrategyConfig.CreateDefault();
    private PlantMeasurements _meas = new();
    private EmsControlOutput _output = new();
    private bool _initialized;

    public void Initialize(EmsStrategyConfig config)
    {
        _config = config.Clone();
        EmsStrategyConfig.NormalizeModes(_config);
        _active.ApplyConfig(_config);
        _reactive.ApplyConfig(_config);
        _active.Reset();
        _reactive.Reset();
        _output = new EmsControlOutput();
        _initialized = true;
    }

    public void UpdateConfig(EmsStrategyConfig config)
    {
        var next = config.Clone();
        EmsStrategyConfig.NormalizeModes(next);
        bool modeChanged = next.ActiveMode != _config.ActiveMode || next.ReactiveMode != _config.ReactiveMode;
        _config = next;
        _active.ApplyConfig(_config);
        _reactive.ApplyConfig(_config);
        if (modeChanged)
        {
            _active.Reset();
            _reactive.Reset();
        }
    }

    public void UpdateMeasurements(PlantMeasurements meas) =>
        _meas = meas ?? new PlantMeasurements();

    public EmsControlOutput Step(TimeSpan dt)
    {
        if (!_initialized)
            Initialize(_config);

        bool canOutput = _config.Enabled
            && _config.SystemSwitch
            && (_config.IsMaster || _config.BypassMasterCheck);

        if (!canOutput)
        {
            _output = ZeroOutput(_meas, outputEnabled: false);
            return _output;
        }

        double pCmd = _config.ActiveEnable ? _active.Step(_config, _meas, dt, _reactive.LastTarget) : 0;
        double qCmd = _config.ReactiveEnable ? _reactive.Step(_config, _meas, dt, _active.LastTarget) : 0;
        if (!_config.ActiveEnable)
            _active.Reset();
        if (!_config.ReactiveEnable)
            _reactive.Reset();

        var branches = PowerDistributor.Distribute(pCmd, qCmd, _meas.Branches, _config.Distribution);
        _output = new EmsControlOutput
        {
            PlantActiveTargetKw = _active.LastTarget,
            PlantActiveCommandKw = pCmd,
            PlantReactiveCommandKvar = qCmd,
            PlantReactiveTargetKvar = _reactive.LastTarget,
            FrequencyDeltaKw = _active.FrequencyDeltaKw,
            FrequencyAction = _active.FrequencyAction,
            InertiaDeltaKw = _active.InertiaDeltaKw,
            InertiaAction = _active.InertiaAction,
            DroopDeltaKvar = _reactive.DroopDeltaKvar,
            DroopAction = _reactive.DroopAction,
            PBaseKw = _active.LastPBase,
            QBaseKvar = _reactive.LastQBase,
            BeforeLimitKw = _active.LastBeforeLimit,
            AfterLimitKw = _active.LastAfterLimit,
            PiOutputKw = _active.LastPiOutput,
            OutputEnabled = true,
            Branches = branches
        };
        return _output;
    }

    public EmsStrategySnapshot GetSnapshot() => new()
    {
        Enabled = _config.Enabled,
        OutputEnabled = _output.OutputEnabled,
        ActiveMode = _config.ActiveMode,
        ReactiveMode = _config.ReactiveMode,
        LocalRemote = _config.LocalRemote,
        FrequencyHz = _meas.FrequencyHz,
        PccActivePowerKw = _meas.PccActivePowerKw,
        PccReactivePowerKvar = _meas.PccReactivePowerKvar,
        PccLineVoltageV = _meas.PccLineVoltageV,
        StationBus35LineVoltageV = _meas.StationBus35LineVoltageV,
        PBaseKw = _output.PBaseKw,
        QBaseKvar = _output.QBaseKvar,
        FrequencyDeltaKw = _output.FrequencyDeltaKw,
        FrequencyAction = _output.FrequencyAction,
        InertiaDeltaKw = _output.InertiaDeltaKw,
        InertiaAction = _output.InertiaAction,
        DroopDeltaKvar = _output.DroopDeltaKvar,
        DroopAction = _output.DroopAction,
        PlantActiveTargetKw = _output.PlantActiveTargetKw,
        PlantActiveCommandKw = _output.PlantActiveCommandKw,
        PlantReactiveTargetKvar = _output.PlantReactiveTargetKvar,
        PlantReactiveCommandKvar = _output.PlantReactiveCommandKvar,
        PiOutputKw = _output.PiOutputKw,
        BeforeLimitKw = _output.BeforeLimitKw,
        AfterLimitKw = _output.AfterLimitKw,
        Branches = _output.Branches
    };

    private static EmsControlOutput ZeroOutput(PlantMeasurements meas, bool outputEnabled)
    {
        var branches = meas.Branches.Select(b => new BranchCommand
        {
            Index = b.Index,
            UnitIndex0 = b.UnitIndex0,
            PcsIndexInUnit = b.PcsIndexInUnit,
            ActivePowerKw = 0,
            ReactivePowerKvar = 0
        }).ToArray();
        return new EmsControlOutput { OutputEnabled = outputEnabled, Branches = branches };
    }
}
