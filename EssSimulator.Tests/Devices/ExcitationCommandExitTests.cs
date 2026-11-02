using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Tests.Devices;

public class ExcitationCommandExitTests
{
    [Fact]
    public void UnlockedForming_StillReceivesLineLossShare()
    {
        var (pcs, unit, main) = EnergizedFormingPcs(out var pcsCfg);
        pcs._currentState.ActivePower = 1000;
        pcs._currentState.AcVoltage = 690;

        Sync(pcs, unit, main, pcsCfg);

        double lineKw = 1000 * 0.11;
        Assert.InRange(pcs.BlackStartSharedLossActivePowerKw, lineKw * 0.5, lineKw + 5);
    }

    [Fact]
    public void LockedForming_DropsLineLossShare()
    {
        var (pcs, unit, main) = EnergizedFormingPcs(out var pcsCfg);
        pcs.AcTerminalLockedToNetwork = true;
        pcs._currentState.ActivePower = 1000;
        pcs._currentState.AcVoltage = 690;

        Sync(pcs, unit, main, pcsCfg);

        Assert.Equal(0, pcs.BlackStartSharedLossActivePowerKw);
    }

    [Fact]
    public void LockedForming_DoesNotReceiveInrushCommand()
    {
        var pcsCfg = PhysicalCfg();
        var locked = CreateForming(pcsCfg);
        var open = CreateForming(pcsCfg);
        locked.AcTerminalLockedToNetwork = true;
        var unit = EnergizedTransformer(inrush: true);
        var main = MainTransformer();
        var time = DateTime.UtcNow;

        UnitTransformerIslandSync.SyncAfterPcsUpdate(
            mainBreakerClosed: false,
            stationBus35LineVoltageV: 35_000,
            new[] { unit },
            main,
            new[] { locked, open },
            _ => true,
            pcsCfg,
            pcsPerUnit: new[] { 2 },
            time,
            TimeSpan.FromMilliseconds(100));

        Assert.Equal(0, locked.BlackStartInrushActiveKw);
        Assert.Equal(0, locked.BlackStartInrushReactiveKvar);
        Assert.True(open.BlackStartInrushReactiveKvar > 1);
    }

    private static (PcsDevice Pcs, TransformerDevice Unit, TransformerDevice Main) EnergizedFormingPcs(
        out PcsPhysicalConfig pcsCfg)
    {
        pcsCfg = PhysicalCfg();
        var pcs = CreateForming(pcsCfg);
        return (pcs, EnergizedTransformer(inrush: false), MainTransformer());
    }

    private static void Sync(
        PcsDevice pcs,
        TransformerDevice unit,
        TransformerDevice main,
        PcsPhysicalConfig pcsCfg)
    {
        UnitTransformerIslandSync.SyncAfterPcsUpdate(
            mainBreakerClosed: false,
            stationBus35LineVoltageV: 35_000,
            new[] { unit },
            main,
            new[] { pcs },
            _ => true,
            pcsCfg,
            pcsPerUnit: new[] { 1 },
            DateTime.UtcNow,
            TimeSpan.FromMilliseconds(100));
    }

    private static PcsPhysicalConfig PhysicalCfg() => new()
    {
        RatedPower = 2508,
        MaxPower = 2508,
        AcVoltageNominal = 690,
        FrequencyNominal = 50,
        MaxCurrent = 2200,
        DcVoltageRangeMin = 1000,
        DcVoltageRangeMax = 1500,
        GridLossCoefficient = 0.11,
        BlackStartPrechargeDelayMs = 0,
        BlackStartVoltageRampVs = 200
    };

    private static PcsDevice CreateForming(PcsPhysicalConfig pcsCfg)
    {
        var pcs = PcsDeviceFactory.Create("pcs", PcsDeviceFactory.CreateConfig(pcsCfg, new PcsRampConfig()));
        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(690);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);
        pcs.Update(1200, 0, DateTime.UtcNow, TimeSpan.FromMilliseconds(400));
        return pcs;
    }

    private static TransformerDevice EnergizedTransformer(bool inrush)
    {
        var unit = new TransformerDevice("unit_xf", new TransformerDeviceConfig
        {
            RatedPowerKva = 2500,
            PrimaryNominalLineVoltageV = 35000,
            SecondaryNominalLineVoltageV = 690,
            NoLoadLossKw = 0.05,
            NoLoadCurrentPercent = 2,
            MagnetizingInrushEnabled = inrush,
            MagnetizingInrushDvDtThresholdPuPerSec = 0.8,
            MagnetizingInrushPeakExtraMultipleOfRatedPrimary = 4
        });
        var time = DateTime.UtcNow;
        unit.Update(0, 0, 1, 0, 0, time, TimeSpan.FromMilliseconds(100));
        unit.Update(35000, 0, 1, 0, 0, time, TimeSpan.FromMilliseconds(10));
        return unit;
    }

    private static TransformerDevice MainTransformer() =>
        new("main_xf", new TransformerDeviceConfig
        {
            RatedPowerKva = 2500,
            PrimaryNominalLineVoltageV = 220000,
            SecondaryNominalLineVoltageV = 35000,
            NoLoadCurrentPercent = 2
        });
}
