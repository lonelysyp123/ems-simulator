using EssSimulator.EmsStrategy.Domain;

namespace EssSimulator.EmsStrategy.Application;

/// <summary>
/// 电站 EMS/PPC 策略配置。功率符号：放电为正、充电为负。
/// </summary>
public sealed class EmsStrategyConfig
{
    public bool Enabled { get; set; }
    public bool SystemSwitch { get; set; } = true;
    public bool ActiveEnable { get; set; } = true;
    public bool IsMaster { get; set; } = true;
    public bool BypassMasterCheck { get; set; } = true;
    public bool ReactiveEnable { get; set; } = true;
    public ActiveMode ActiveMode { get; set; } = ActiveMode.CloseLoopFixed;
    public ReactiveMode ReactiveMode { get; set; } = ReactiveMode.CloseLoopFixed;
    public LocalRemote LocalRemote { get; set; } = LocalRemote.Local;
    public double LocalActiveSetKw { get; set; }
    public double RemoteActiveSetKw { get; set; }
    public double LocalReactiveSetKvar { get; set; }
    public double RemoteReactiveSetKvar { get; set; }
    public double PowerFactorSet { get; set; } = 1;
    public double PfSign { get; set; } = 1;
    public double VoltageSetV { get; set; } = 35000;
    public double VoltageKp { get; set; } = 0.5;
    /// <summary>恒压调差系数 Voltage_Fixed_K：Qset = (u_out − Umeas)·K + Qmeas。</summary>
    public double VoltageFixedK { get; set; } = 1;
    public double ApparentRatedKva { get; set; } = 5000;
    public double PlantRatedKw { get; set; } = 5000;
    public bool ApparentLimitEnabled { get; set; } = true;
    public bool DampEnabled { get; set; } = true;

    public SlopeConfig Slope { get; set; } = new();
    public SlopeConfig ReactiveSlope { get; set; } = new();
    public PidConfig ActivePid { get; set; } = new();
    public PidConfig ReactivePid { get; set; } = new();
    public PrimaryFrequencyConfig PrimaryFrequency { get; set; } = new();
    public InertiaConfig Inertia { get; set; } = new();
    public VoltageDroopConfig VoltageDroop { get; set; } = new();
    public DistributionConfig Distribution { get; set; } = new();

    /// <summary>旧 JSON 的 CloseLoopCurve 落地为 CloseLoopFixed。</summary>
    public static void NormalizeModes(EmsStrategyConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        if (cfg.ActiveMode == ActiveMode.CloseLoopCurve)
            cfg.ActiveMode = ActiveMode.CloseLoopFixed;
        if (cfg.ReactiveMode == ReactiveMode.CloseLoopCurve)
            cfg.ReactiveMode = ReactiveMode.CloseLoopFixed;
    }

    public static EmsStrategyConfig CreateDefault() => new()
    {
        Enabled = false,
        SystemSwitch = true,
        ActiveEnable = true,
        ReactiveEnable = true,
        IsMaster = true,
        BypassMasterCheck = true,
        ActiveMode = ActiveMode.CloseLoopFixed,
        ReactiveMode = ReactiveMode.CloseLoopFixed,
        LocalRemote = LocalRemote.Local,
        LocalActiveSetKw = 0,
        RemoteActiveSetKw = 0,
        LocalReactiveSetKvar = 0,
        RemoteReactiveSetKvar = 0,
        PowerFactorSet = 1,
        PfSign = 1,
        VoltageSetV = 35000,
        VoltageKp = 0.5,
        VoltageFixedK = 1,
        ApparentRatedKva = 5000,
        PlantRatedKw = 5000,
        ApparentLimitEnabled = true,
        DampEnabled = true,
        Slope = new SlopeConfig
        {
            Enabled = false,
            RiseKwPerSec = 100,
            FallKwPerSec = 100
        },
        ActivePid = new PidConfig
        {
            Enabled = true,
            Kp = 0.4,
            Ki = 0.05,
            Kb = 0.5,
            Period = TimeSpan.FromMilliseconds(3000),
            DeadbandKw = 5,
            OutMinKw = -5000,
            OutMaxKw = 5000,
            Discretization = PidDiscretization.CompatiblePeriod
        },
        PrimaryFrequency = new PrimaryFrequencyConfig
        {
            Enabled = true,
            RatedFrequencyHz = 50,
            Deadband1Percent = 0.2,
            Deadband2Percent = 0.5,
            DroopPercent = 3,
            Droop2Percent = 5,
            SegmentCount = 3,
            OverFreqEnable = true,
            UnderFreqEnable = true,
            ControlCycle = TimeSpan.FromMilliseconds(1000),
            ResetTime = TimeSpan.FromMilliseconds(2000),
            MaxOutputKw = 5000,
            MaxAbsorbKw = 5000,
            LimitCoefficient = 1
        },
        Inertia = new InertiaConfig(),
        VoltageDroop = new VoltageDroopConfig(),
        ReactiveSlope = new SlopeConfig { Enabled = false, RiseKwPerSec = 100, FallKwPerSec = 100 },
        ReactivePid = new PidConfig
        {
            Enabled = true,
            Kp = 0.4,
            Ki = 0.05,
            Kb = 0.5,
            Period = TimeSpan.FromMilliseconds(3000),
            DeadbandKw = 5,
            OutMinKw = -5000,
            OutMaxKw = 5000,
            Discretization = PidDiscretization.CompatiblePeriod
        },
        Distribution = new DistributionConfig
        {
            Enabled = true,
            SocBalance = false,
            SocMin = 0.1,
            SocMax = 0.9
        }
    };

    public EmsStrategyConfig Clone() => new()
    {
        Enabled = Enabled,
        SystemSwitch = SystemSwitch,
        ActiveEnable = ActiveEnable,
        ReactiveEnable = ReactiveEnable,
        IsMaster = IsMaster,
        BypassMasterCheck = BypassMasterCheck,
        ActiveMode = ActiveMode,
        ReactiveMode = ReactiveMode,
        LocalRemote = LocalRemote,
        LocalActiveSetKw = LocalActiveSetKw,
        RemoteActiveSetKw = RemoteActiveSetKw,
        LocalReactiveSetKvar = LocalReactiveSetKvar,
        RemoteReactiveSetKvar = RemoteReactiveSetKvar,
        PowerFactorSet = PowerFactorSet,
        PfSign = PfSign,
        VoltageSetV = VoltageSetV,
        VoltageKp = VoltageKp,
        VoltageFixedK = VoltageFixedK,
        ApparentRatedKva = ApparentRatedKva,
        PlantRatedKw = PlantRatedKw,
        ApparentLimitEnabled = ApparentLimitEnabled,
        DampEnabled = DampEnabled,
        Slope = (Slope ?? new SlopeConfig()).Clone(),
        ActivePid = (ActivePid ?? new PidConfig()).Clone(),
        PrimaryFrequency = (PrimaryFrequency ?? new PrimaryFrequencyConfig()).Clone(),
        Inertia = (Inertia ?? new InertiaConfig()).Clone(),
        VoltageDroop = (VoltageDroop ?? new VoltageDroopConfig()).Clone(),
        ReactiveSlope = (ReactiveSlope ?? Slope ?? new SlopeConfig()).Clone(),
        ReactivePid = (ReactivePid ?? ActivePid ?? new PidConfig()).Clone(),
        Distribution = (Distribution ?? new DistributionConfig()).Clone()
    };
}

public sealed class SlopeConfig
{
    public bool Enabled { get; set; }
    /// <summary>JSON 名沿用 RiseKwPerSec，语义是 C 的 kW/min。</summary>
    public double RiseKwPerSec { get; set; } = 100;
    /// <summary>JSON 名沿用 FallKwPerSec，语义是 C 的 kW/min。</summary>
    public double FallKwPerSec { get; set; } = 100;

    public SlopeConfig Clone() => new()
    {
        Enabled = Enabled,
        RiseKwPerSec = RiseKwPerSec,
        FallKwPerSec = FallKwPerSec
    };
}

public sealed class PidConfig
{
    public bool Enabled { get; set; } = true;
    public double Kp { get; set; } = 0.4;
    public double Ki { get; set; } = 0.05;
    public double Kb { get; set; } = 0.5;
    public TimeSpan Period { get; set; } = TimeSpan.FromMilliseconds(3000);
    public double DeadbandKw { get; set; }
    public double OutMinKw { get; set; } = -5000;
    public double OutMaxKw { get; set; } = 5000;
    public PidDiscretization Discretization { get; set; } = PidDiscretization.CompatiblePeriod;

    public PidConfig Clone() => new()
    {
        Enabled = Enabled,
        Kp = Kp,
        Ki = Ki,
        Kb = Kb,
        Period = Period,
        DeadbandKw = DeadbandKw,
        OutMinKw = OutMinKw,
        OutMaxKw = OutMaxKw,
        Discretization = Discretization
    };
}

public sealed class PrimaryFrequencyConfig
{
    public bool Enabled { get; set; } = true;
    public double RatedFrequencyHz { get; set; } = 50;
    public double Deadband1Percent { get; set; } = 0.2;
    public double Deadband2Percent { get; set; } = 0.5;
    public double DroopPercent { get; set; } = 3;
    public double Droop2Percent { get; set; } = 5;
    /// <summary>过频 droop%；0 表示沿用 DroopPercent。</summary>
    public double OverDroopPercent { get; set; }
    /// <summary>欠频 droop%；0 表示沿用 DroopPercent。</summary>
    public double UnderDroopPercent { get; set; }
    public double OverDroop2Percent { get; set; }
    public double UnderDroop2Percent { get; set; }
    public int SegmentCount { get; set; } = 3;
    public bool OverFreqEnable { get; set; } = true;
    public bool UnderFreqEnable { get; set; } = true;
    public TimeSpan ControlCycle { get; set; } = TimeSpan.FromMilliseconds(1000);
    public TimeSpan ResetTime { get; set; } = TimeSpan.FromMilliseconds(2000);
    public double MaxOutputKw { get; set; } = 5000;
    public double MaxAbsorbKw { get; set; } = 5000;
    public double LimitCoefficient { get; set; } = 1;

    public PrimaryFrequencyConfig Clone() => new()
    {
        Enabled = Enabled,
        RatedFrequencyHz = RatedFrequencyHz,
        Deadband1Percent = Deadband1Percent,
        Deadband2Percent = Deadband2Percent,
        DroopPercent = DroopPercent,
        Droop2Percent = Droop2Percent,
        OverDroopPercent = OverDroopPercent,
        UnderDroopPercent = UnderDroopPercent,
        OverDroop2Percent = OverDroop2Percent,
        UnderDroop2Percent = UnderDroop2Percent,
        SegmentCount = SegmentCount,
        OverFreqEnable = OverFreqEnable,
        UnderFreqEnable = UnderFreqEnable,
        ControlCycle = ControlCycle,
        ResetTime = ResetTime,
        MaxOutputKw = MaxOutputKw,
        MaxAbsorbKw = MaxAbsorbKw,
        LimitCoefficient = LimitCoefficient
    };
}

public sealed class DistributionConfig
{
    public bool Enabled { get; set; } = true;
    public bool SocBalance { get; set; }
    public double SocMin { get; set; } = 0.1;
    public double SocMax { get; set; } = 0.9;

    public DistributionConfig Clone() => new()
    {
        Enabled = Enabled,
        SocBalance = SocBalance,
        SocMin = SocMin,
        SocMax = SocMax
    };
}

public sealed class InertiaConfig
{
    public bool Enabled { get; set; }
    public bool LockPrimaryFrequency { get; set; }
    public double RatedFrequencyHz { get; set; } = 50;
    public double InertiaTimeSec { get; set; } = 6;
    public double AmplitudeDeadbandHz { get; set; } = 0.05;
    public double RateDeadbandHzPerSec { get; set; } = 0.1;
    public double FreqMinHz { get; set; } = 47;
    public double FreqMaxHz { get; set; } = 53;
    public TimeSpan ControlCycle { get; set; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan ResetTime { get; set; } = TimeSpan.FromMilliseconds(2000);

    public InertiaConfig Clone() => new()
    {
        Enabled = Enabled,
        LockPrimaryFrequency = LockPrimaryFrequency,
        RatedFrequencyHz = RatedFrequencyHz,
        InertiaTimeSec = InertiaTimeSec,
        AmplitudeDeadbandHz = AmplitudeDeadbandHz,
        RateDeadbandHzPerSec = RateDeadbandHzPerSec,
        FreqMinHz = FreqMinHz,
        FreqMaxHz = FreqMaxHz,
        ControlCycle = ControlCycle,
        ResetTime = ResetTime
    };
}

public sealed class VoltageDroopConfig
{
    public bool Enabled { get; set; }
    public double RatedVoltageV { get; set; } = 35000;
    public double Deadband1Percent { get; set; } = 0.5;
    public double Deadband2Percent { get; set; } = 1.5;
    /// <summary>内段下垂系数 %（4 表示 4%，与一次调频 DroopPercent 同口径）。</summary>
    public double K1Percent { get; set; } = 4;
    /// <summary>五段外段下垂系数 %。</summary>
    public double K2Percent { get; set; } = 6;
    /// <summary>0=从死区边沿起算（C 曲线1）；1=从额定电压起算（C 曲线2）。</summary>
    public int VoltageCurveType { get; set; }
    public int SegmentCount { get; set; } = 3;
    public bool OverVoltEnable { get; set; } = true;
    public bool UnderVoltEnable { get; set; } = true;
    public TimeSpan ControlCycle { get; set; } = TimeSpan.FromMilliseconds(1000);
    public TimeSpan ResetTime { get; set; } = TimeSpan.FromMilliseconds(2000);
    public double MaxOutputKvar { get; set; } = 5000;
    public double MaxAbsorbKvar { get; set; } = 5000;
    public double LimitCoefficient { get; set; } = 1;

    public VoltageDroopConfig Clone() => new()
    {
        Enabled = Enabled,
        RatedVoltageV = RatedVoltageV,
        Deadband1Percent = Deadband1Percent,
        Deadband2Percent = Deadband2Percent,
        K1Percent = K1Percent,
        K2Percent = K2Percent,
        VoltageCurveType = VoltageCurveType,
        SegmentCount = SegmentCount,
        OverVoltEnable = OverVoltEnable,
        UnderVoltEnable = UnderVoltEnable,
        ControlCycle = ControlCycle,
        ResetTime = ResetTime,
        MaxOutputKvar = MaxOutputKvar,
        MaxAbsorbKvar = MaxAbsorbKvar,
        LimitCoefficient = LimitCoefficient
    };
}
