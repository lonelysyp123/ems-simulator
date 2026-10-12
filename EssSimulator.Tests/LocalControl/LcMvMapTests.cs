using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssSimModelApi;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;
using EssSimulator.EssSimModelApi.Mappers;
using EssSimulator.LocalControl;

namespace EssSimulator.Tests.LocalControl;

public class LcMvMapTests
{
    [Fact]
    public void PointMap_CoversPccMeterAndHvCommand()
    {
        Assert.True(File.Exists(CsvPath), CsvPath);
        var g1 = LcPointMapExpander.ExpandFile(CsvPath, 1);
        var g2 = LcPointMapExpander.ExpandFile(CsvPath, 2);
        var names = g1.Select(e => e.ParamName ?? "").ToList();
        Assert.Equal(g1.Count, g2.Count);
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        Assert.Contains(g1, e => e.FunctionCode == 4 && e.Address == 25306 && e.ParamName == LcMvMap.LineVoltageAb && e.Type == "float");
        Assert.Contains(g1, e => e.FunctionCode == 4 && e.Address == 25318 && e.ParamName == LcMvMap.NeutralCurrent);
        Assert.Contains(g1, e => e.FunctionCode == 4 && e.Address == 25326 && e.ParamName == LcMvMap.TotalActivePower);
        Assert.Contains(g1, e => e.FunctionCode == 4 && e.Address == 25352 && e.ParamName == LcMvMap.Frequency);
        Assert.Contains(g1, e => e.FunctionCode == 4 && e.Address == 25362 && e.ParamName == LcMvMap.ForwardActiveEnergySecondary);
        Assert.Contains(g1, e => e.FunctionCode == 4 && e.Address == 25370 && e.ParamName == LcMvMap.ReverseReactiveEnergySecondary);
        Assert.Contains(g1, e =>
            e.FunctionCode == 6 && e.Address == 33200 && e.ParamName == LcMvMap.HvBreakerCommand);
        Assert.Equal(1, g1.Count(e => e.ParamName == LcMvMap.HvBreakerCommand));
        Assert.DoesNotContain(g1, e => e.Address == 25030);
        Assert.DoesNotContain(g1, e => e.Address == 33201);
    }

    [Fact]
    public void PointMap_ModelSimIsUnbound()
    {
        var rows = LcPointMapExpander.LoadTemplate(CsvPath);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("0", r.ModelSim));
    }

    [Theory]
    [InlineData(0xAA, 0xAA, true)]
    [InlineData(0xEE, 0xEE, false)]
    public void EncodeAndInterpret_RoundTrip(int raw, int expectedNorm, bool closed)
    {
        Assert.Equal(expectedNorm, (int)LcMvControl.Encode(closed));
        Assert.True(LcMvControl.TryInterpret(raw, out var normalized, out var isClosed));
        Assert.Equal(expectedNorm, (int)normalized);
        Assert.Equal(closed, isClosed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(0xAB)]
    public void TryInterpret_RejectsInvalid(int raw)
    {
        Assert.False(LcMvControl.TryInterpret(raw, out _, out _));
    }

    private static string CsvPath =>
        Path.Combine(FindRepoRoot(), "pointmaps", "models", "lc", "mv", "lc.csv");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EssSimulator.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("找不到仓库根目录");
    }
}

public class LcMvControlApplyTests : SimulatorHostTestBase
{
    [Fact]
    public void TryApply_AaClosesUnitBreaker()
    {
        var (ess, emu) = Build();
        using (ess)
        {
            emu.Breaker.Closed = 0;
            emu.Emu.PowerOnOff = 0;
            Assert.True(LcMvControl.TryApply(1, 0xAA, out var normalized, out _));
            Assert.Equal(0xAA, (int)normalized);
            Assert.True(ess.IsUnitBreakerClosed(0));
            Assert.Equal(1, emu.Breaker.Closed);
            Assert.Equal(1, emu.Emu.PowerOnOff);
        }
    }

    [Fact]
    public void TryApply_EeOpensUnitBreaker()
    {
        var (ess, emu) = Build();
        using (ess)
        {
            Assert.True(LcMvControl.TryApply(1, 0xEE, out var normalized, out _));
            Assert.Equal(0xEE, (int)normalized);
            Assert.False(ess.IsUnitBreakerClosed(0));
            Assert.Equal(0, emu.Breaker.Closed);
            Assert.Equal(0, emu.Emu.PowerOnOff);
        }
    }

    [Fact]
    public void TryApply_Invalid_DoesNotTouchBreaker()
    {
        var (ess, emu) = Build();
        using (ess)
        {
            Assert.True(ess.IsUnitBreakerClosed(0));
            Assert.False(LcMvControl.TryApply(1, 0xAB, out _, out var message));
            Assert.Contains("AA", message);
            Assert.True(ess.IsUnitBreakerClosed(0));
            Assert.Equal(1, emu.Breaker.Closed);
        }
    }

    [Fact]
    public void TryApply_AaWhileTripped_StaysOpenUntilReset()
    {
        var (ess, emu) = Build();
        using (ess)
        {
            ess.ElectricalNetwork.UnitBreakers[0].SwitchState.IsTripped = true;
            ess.ElectricalNetwork.UnitBreakers[0].SwitchState.IsClosed = false;

            Assert.True(LcMvControl.TryApply(1, 0xAA, out var normalized, out var message));
            Assert.Equal(0xAA, (int)normalized);
            Assert.Contains("跳闸", message);
            Assert.True(ess.IsUnitBreakerTripped(0));
            Assert.False(ess.IsUnitBreakerClosed(0));
            Assert.Equal(0, emu.Breaker.Closed);

            Assert.True(DeviceControlFacade.TryResetUnitBreakerTrip(1, out _));
            Assert.False(ess.IsUnitBreakerTripped(0));
            Assert.False(ess.IsUnitBreakerClosed(0));

            Assert.True(LcMvControl.TryApply(1, 0xAA, out _, out _));
            Assert.True(ess.IsUnitBreakerClosed(0));
            Assert.False(ess.IsUnitBreakerTripped(0));
            Assert.Equal(1, emu.Breaker.Closed);
        }
    }

    [Fact]
    public void EncodeHvStatus_TrippedOrOpen_IsEe()
    {
        Assert.Equal(0xEE, LcSystemTelemetry.EncodeHvStatus(0));
        Assert.Equal(0xEE, LcSystemTelemetry.EncodeHvStatus(0xEE));
        Assert.Equal(0xAA, LcSystemTelemetry.EncodeHvStatus(1));
        Assert.Equal(0xAA, LcSystemTelemetry.EncodeHvStatus(0xAA));
    }

    [Fact]
    public void SyncUnit_AfterTrip_BreakerClosedIsOpen()
    {
        var (ess, emu) = Build();
        using (ess)
        {
            emu.Emu.PowerOnOff = 1;
            ess.ElectricalNetwork.UnitBreakers[0].SwitchState.IsTripped = true;
            ess.ElectricalNetwork.UnitBreakers[0].SwitchState.IsClosed = false;
            PcsEmuSynchronizer.SyncUnit(ess, emu, 0, 0);
            Assert.Equal(0, emu.Breaker.Closed);
            Assert.Equal(0xEE, LcSystemTelemetry.EncodeHvStatus(emu.Breaker.Closed));
        }
    }

    private static (EnergyStorageSystem ess, EnergyManagementData emu) Build()
    {
        var cfg = new SimulatorConfig
        {
            Devices =
            {
                new EssUnitConfig
                {
                    Pcs =
                    {
                        new EssSimulator.Configuration.PcsDeviceConfig(),
                        new EssSimulator.Configuration.PcsDeviceConfig()
                    }
                }
            }
        };
        var pcsPhy = new PcsPhysicalConfig { AcVoltageNominal = 690 };
        var ess = new EnergyStorageSystem(
            cfg,
            pcsPhy,
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            new PccConfig(),
            new MeterConfig());
        var emu = PcsDataServer.BuildEmuMirror(cfg.Devices[0], pcsPhy);
        SimulatorHost.Instance.RegisterEss(ess);
        SimulatorHost.Instance.RegisterEmu(1, emu);
        return (ess, emu);
    }
}

public class LcMvTelemetryTests
{
    [Fact]
    public void Collect_ParamNames_MatchMvCsv()
    {
        var names = LcPointMapExpander.ExpandFile(
                Path.Combine(FindRepoRoot(), "pointmaps", "models", "lc", "mv", "lc.csv"), 1)
            .Select(e => e.ParamName)
            .ToHashSet();
        foreach (var (param, _) in LcMvTelemetry.Collect(null))
            Assert.Contains(param, names);
    }

    [Fact]
    public void FromPcc_CopiesBalancedPrimaryLikeGridMeter()
    {
        var meter = new MeterSimulator("pcc_meter", new MeterInstanceConfig());
        var qty = AcQuantityConverter.FromLineVoltageAndPower(
            220_000, 880, -200, ThreePhaseConnection.Star, 50);
        meter.SampleFrom(qty, TimeSpan.FromHours(1));

        var snap = LcMvTelemetry.FromPcc(meter, systemFrequencyHz: 50);
        Assert.Equal(220_000, snap.LineVoltageAb, 0);
        Assert.Equal(snap.LineVoltageAb, snap.LineVoltageBc);
        Assert.Equal(snap.LineVoltageAb, snap.LineVoltageCa);
        Assert.Equal(snap.PhaseACurrent, snap.PhaseBCurrent);
        Assert.Equal(snap.PhaseACurrent, snap.PhaseCCurrent);
        Assert.Equal(0, snap.NeutralCurrent);
        Assert.Equal(880, snap.TotalActivePower, 3);
        Assert.Equal(-200, snap.TotalReactivePower, 3);
        Assert.Equal(50, snap.Frequency);
        Assert.True(snap.PowerFactor < 0);

        double ratio = meter.Config.Pt.Ratio * meter.Config.Ct.Ratio;
        Assert.Equal(880 / ratio, snap.ForwardActiveEnergySecondary, 9);
        Assert.Equal(0, snap.ReverseActiveEnergySecondary);
        Assert.Equal(0, snap.ForwardReactiveEnergySecondary);
        Assert.Equal(200 / ratio, snap.ReverseReactiveEnergySecondary, 9);

        var pts = LcMvTelemetry.Collect(snap).ToDictionary(p => p.Param, p => p.Value);
        Assert.Equal(snap.LineVoltageAb, pts[LcMvMap.LineVoltageAb]);
        Assert.Equal(snap.TotalActivePower, pts[LcMvMap.TotalActivePower]);
        Assert.Equal(snap.ForwardActiveEnergySecondary, pts[LcMvMap.ForwardActiveEnergySecondary]);
    }

    [Fact]
    public void FromUnitMeter_CopiesThisUnitElectricityMeter_NotPccScale()
    {
        var meter = new ElectricityMeterData
        {
            LineVoltageAB = 35000,
            LineVoltageBC = 35100,
            LineVoltageCA = 34900,
            PhaseACurrent = 80,
            PhaseBCurrent = 81,
            PhaseCCurrent = 82,
            TotalActivePower = 4200,
            TotalReactivePower = -300,
            TotalApparentPower = 4211,
            PowerFactor = 0.997f,
            Frequency = 50.02f,
            ForwardActiveEnergy = 12.5f,
            ReverseActiveEnergy = 1.25f,
            InductiveReactiveEnergy = 0.4f,
            CapacitiveReactiveEnergy = 0.1f
        };

        var snap = LcMvTelemetry.FromUnitMeter(meter);
        Assert.Equal(35000, snap.LineVoltageAb, 0);
        Assert.Equal(35100, snap.LineVoltageBc, 0);
        Assert.Equal(34900, snap.LineVoltageCa, 0);
        Assert.Equal(80, snap.PhaseACurrent, 3);
        Assert.Equal(81, snap.PhaseBCurrent, 3);
        Assert.Equal(82, snap.PhaseCCurrent, 3);
        Assert.Equal(0, snap.NeutralCurrent);
        Assert.Equal(4200, snap.TotalActivePower, 3);
        Assert.Equal(-300, snap.TotalReactivePower, 3);
        Assert.Equal(4211, snap.TotalApparentPower, 3);
        Assert.Equal(0.997, snap.PowerFactor, 3);
        Assert.Equal(50.02, snap.Frequency, 3);
        Assert.Equal(12.5, snap.ForwardActiveEnergySecondary, 3);
        Assert.Equal(1.25, snap.ReverseActiveEnergySecondary, 3);
        Assert.Equal(0.4, snap.ForwardReactiveEnergySecondary, 3);
        Assert.Equal(0.1, snap.ReverseReactiveEnergySecondary, 3);

        var pts = LcMvTelemetry.Collect(snap).ToDictionary(p => p.Param, p => p.Value);
        Assert.Equal(35000d, Convert.ToDouble(pts[LcMvMap.LineVoltageAb]));
        Assert.Equal(4200d, Convert.ToDouble(pts[LcMvMap.TotalActivePower]));
    }

    [Fact]
    public void Collect_NullMeter_WritesZerosForAllMeterParams()
    {
        var pts = LcMvTelemetry.Collect(null);
        Assert.Equal(16, pts.Count);
        Assert.All(pts, p => Assert.Equal(0d, Convert.ToDouble(p.Value)));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EssSimulator.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("找不到仓库根目录");
    }
}
