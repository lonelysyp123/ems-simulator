using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;
using Xunit.Abstractions;

namespace EssSimulator.Tests.Devices;

/// <summary>黑启动过程 V/I/Q/φ 轨迹，用于诊断无功来源（无需人工抓数）。</summary>
public class BlackStartTelemetryTraceTests
{
    private readonly ITestOutputHelper _output;

    public BlackStartTelemetryTraceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TraceBlackStartWithUnitTransformer_LogsPowerPhasorTimeline()
    {
        var pcsCfg = new PcsPhysicalConfig
        {
            RatedPower = 2508,
            MaxPower = 2508,
            AcVoltageNominal = 690,
            FrequencyNominal = 50,
            MaxCurrent = 2200,
            DcVoltageRangeMin = 1000,
            DcVoltageRangeMax = 1500,
            GridLossCoefficient = 0.11,
            BlackStartActivePowerGainKwPerVolt = 2.174,
            BlackStartMaxActivePowerKw = 200,
            BlackStartReactiveVoltageGainKvarPerV = 4.0,
            BlackStartPrechargeDelayMs = 0,
            BlackStartVoltageRampVs = 200,
            BlackStartFrequencyStartHz = 47,
            BlackStartCurrentLimitFraction = 0.3,
            BlackStartBusEnergizedFraction = 0.99
        };

        var pcs = PcsDeviceFactory.Create("pcs1", PcsDeviceFactory.CreateConfig(pcsCfg, new PcsRampConfig()));
        var unitXf = new TransformerDevice("unit_xf", new TransformerDeviceConfig
        {
            RatedPowerKva = 6300,
            PrimaryNominalLineVoltageV = 35000,
            SecondaryNominalLineVoltageV = 690,
            NoLoadLossKw = 0.1,
            NoLoadCurrentPercent = 2,
            MagnetizingInrushEnabled = true,
            MagnetizingInrushDvDtThresholdPuPerSec = 0.8,
            MagnetizingInrushPeakExtraMultipleOfRatedPrimary = 4.0,
            MagnetizingInrushDecayTimeConstantSec = 0.45,
            MagnetizingInrushMaxExtraMultipleOfRatedPrimary = 12.0
        });
        var mainXf = new TransformerDevice("main_xf", new TransformerDeviceConfig
        {
            RatedPowerKva = 31500,
            PrimaryNominalLineVoltageV = 220000,
            SecondaryNominalLineVoltageV = 35000,
            NoLoadCurrentPercent = 2
        });

        var pcsList = new List<PcsDevice> { pcs };
        var unitTransformers = new List<TransformerDevice> { unitXf };
        var step = TimeSpan.FromMilliseconds(100);
        var simTime = DateTime.UtcNow;

        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(690);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);

        _output.WriteLine("t_ms  phase           bus690V  eff690V  P_kW   Q_kvar  I_A   phi_deg  magQ  inrushQ  qBuild_est");
        _output.WriteLine("----  --------------  -------  -------  -----  ------  ----  -------  ----  -------  ----------");

        double bus690 = 0;
        double maxQ = 0;
        for (int i = 0; i < 35; i++)
        {
            simTime += step;
            pcs.Update(1200, 0, simTime, step);
            pcs.RefreshBlackStartBusContext(bus690);

            if (pcs.TryGetIslandBusVoltageInjection(out var injV, out _))
                bus690 = Math.Max(bus690, injV * 0.98 + bus690 * 0.02);

            UnitTransformerIslandSync.SyncAfterPcsUpdate(
                mainBreakerClosed: false,
                stationBus35LineVoltageV: 0,
                unitTransformers,
                mainXf,
                pcsList,
                isUnitBreakerClosed: _ => true,
                pcsCfg,
                pcsPerUnit: new[] { 1 },
                simTime,
                step);

            pcs.Update(1200, 0, simTime, TimeSpan.Zero);
            pcs.RefreshBlackStartBusContext(bus690);

            var st = pcs.GetCurrentState();
            maxQ = Math.Max(maxQ, st.ReactivePower);
            var ac = pcs.Ac.Output.Ac!.Internal;
            double nom = pcsCfg.AcVoltageNominal;
            double vCtrl = bus690 > nom * 0.08 ? bus690 : Math.Max(st.IslandVoltageEffectiveV, 1);
            double qBuildEst = 4.0 * Math.Max(0, nom - vCtrl);
            double magQ = unitXf.GetSecondaryMagnetizingReactiveKvar();
            var (_, qInrush) = unitXf.GetInrushDemandKwKvar();

            _output.WriteLine(
                $"{i * 100,4}  {st.BlackStartPhase,-14}  {bus690,7:F0}  {st.IslandVoltageEffectiveV,7:F0}  " +
                $"{st.ActivePower,5:F0}  {st.ReactivePower,6:F0}  {ac.LineCurrentA,4:F0}  {ac.PhaseAngleDeg,7:F1}  " +
                $"{magQ,4:F0}  {qInrush,7:F0}  {qBuildEst,10:F0}");

            if (st.BlackStartPhase == BlackStartPhase.Synchronized && i > 20)
                break;
        }

        var final = pcs.GetCurrentState();
        var finalAc = pcs.Ac.Output.Ac!.Internal;
        _output.WriteLine("");
        _output.WriteLine($"稳态/末期: P={final.ActivePower:F0}kW Q={final.ReactivePower:F0}kvar " +
                          $"I={finalAc.LineCurrentA:F0}A φ={finalAc.PhaseAngleDeg:F1}° PF={finalAc.PowerFactor:F3}");

        Assert.True(maxQ > 0, "黑启动建压期应有无功输出");
    }
}
