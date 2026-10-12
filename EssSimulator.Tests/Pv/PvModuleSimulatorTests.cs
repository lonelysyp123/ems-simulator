using EssSimulator.EssDeviceSimModel.Pv;

namespace EssSimulator.Tests.Pv;

public class PvModuleSimulatorTests
{
    [Fact]
    public void Catalog_Neg21c20q_MatchesOfficialIdentity()
    {
        var spec = TrinaPvModuleCatalog.Neg21c20q760();

        Assert.Equal("TSM-NEG21C.20Q", spec.Model);
        Assert.Equal(760, spec.PmaxStcW);
        Assert.Equal(0.245, spec.Efficiency, 3);
        Assert.Equal(264, spec.CellCount);
        Assert.Equal(2384, spec.LengthMm);
        Assert.Equal(1303, spec.WidthMm);
        Assert.Equal(33, spec.ThicknessMm);
        Assert.Equal(1500, spec.MaxSystemVoltageV);
        Assert.Equal(-0.0026, spec.GammaPmaxPerK);
        Assert.Equal(0.85, spec.Bifaciality);
    }

    [Fact]
    public void Catalog_TryGet_ResolvesKnownAndRejectsUnknown()
    {
        Assert.True(TrinaPvModuleCatalog.TryGet("tsm-neg21c.20q", out var spec));
        Assert.Equal("TSM-NEG21C.20Q", spec.Model);
        Assert.False(TrinaPvModuleCatalog.TryGet("NO-SUCH", out _));
        Assert.False(TrinaPvModuleCatalog.TryGet("  ", out _));
        Assert.Equal("TSM-NEG21C.20Q", TrinaPvModuleCatalog.GetOrThrow(null).Model);
        var ex = Assert.Throws<ArgumentException>(() => TrinaPvModuleCatalog.GetOrThrow("NO-SUCH"));
        Assert.Contains("TSM-NEG21C.20Q", ex.Message);
    }

    [Fact]
    public void Evaluate_AtStc_ReturnsRatedMpp()
    {
        var module = PvModuleSimulator.CreateNeg21c20q();

        var point = module.Evaluate(gFrontWm2: 1000, cellTempC: 25, gRearWm2: 0);

        Assert.InRange(point.PmpW, 759.0, 761.0);
        Assert.InRange(point.VmpV, 41.5, 43.0);
        Assert.InRange(point.VocV, 50.1, 50.5);
        Assert.InRange(point.IscA, 19.0, 19.4);
        Assert.True(point.ImpA < point.IscA);
        Assert.True(point.VmpV < point.VocV);
    }

    [Fact]
    public void Evaluate_ZeroIrradiance_OutputsZero()
    {
        var module = PvModuleSimulator.CreateNeg21c20q();

        var point = module.Evaluate(gFrontWm2: 0, cellTempC: 25);

        Assert.Equal(0, point.PmpW);
        Assert.Equal(0, point.IscA);
        Assert.Equal(0, point.ImpA);
        Assert.Equal(0, point.VocV);
        Assert.Equal(0, point.VmpV);
    }

    [Fact]
    public void Evaluate_HotCell_DeratesPower()
    {
        var module = PvModuleSimulator.CreateNeg21c20q();
        var stc = module.Evaluate(1000, 25);
        var hot = module.Evaluate(1000, 75);

        Assert.True(hot.VocV < stc.VocV);
        Assert.True(hot.PmpW < stc.PmpW);
        double expected = stc.PmpW * (1 + module.Spec.GammaPmaxPerK * 50);
        Assert.InRange(hot.PmpW, expected * 0.95, expected * 1.05);
    }

    [Fact]
    public void FindMpp_IsMaximumAlongIvCurve()
    {
        var module = PvModuleSimulator.CreateNeg21c20q();
        var mpp = module.Evaluate(1000, 25);

        double best = 0;
        for (int i = 0; i <= 40; i++)
        {
            double v = mpp.VocV * i / 40.0;
            best = Math.Max(best, v * module.CurrentAtVoltage(v, 1000, 25));
        }

        Assert.InRange(mpp.PmpW, best - 1.0, best + 1.0);
        Assert.True(mpp.PmpW >= best - 0.5);
    }

    [Fact]
    public void Evaluate_IncidenceAngle_ReducesPoaAndMpp()
    {
        var module = PvModuleSimulator.CreateNeg21c20q();
        double gPeak = PvIrradianceModel.EvaluatePlaneOfArrayWm2(90);
        double gLow = PvIrradianceModel.EvaluatePlaneOfArrayWm2(30);
        var normal = module.Evaluate(gPeak, 25);
        var tilted = module.Evaluate(gLow, 25);

        Assert.True(gLow < gPeak);
        Assert.True(tilted.PmpW < normal.PmpW * 0.6);
        Assert.True(tilted.PmpW > 0);
    }

    [Fact]
    public void Evaluate_HalfIrradiance_HalvesCurrentAndPower()
    {
        var module = PvModuleSimulator.CreateNeg21c20q();
        var full = module.Evaluate(1000, 25);
        var half = module.Evaluate(500, 25);

        Assert.InRange(half.IscA, full.IscA * 0.49, full.IscA * 0.51);
        Assert.InRange(half.PmpW, full.PmpW * 0.48, full.PmpW * 0.52);
        Assert.True(half.VocV < full.VocV);
        Assert.True(half.VocV > full.VocV * 0.9);
    }

    [Fact]
    public void Evaluate_BifacialRear_IncreasesPower()
    {
        var module = PvModuleSimulator.CreateNeg21c20q();
        var frontOnly = module.Evaluate(1000, 25, gRearWm2: 0);
        var bnpi = module.Evaluate(1000, 25, gRearWm2: 135);

        Assert.True(bnpi.PmpW > frontOnly.PmpW);
        double expectedGain = 1 + module.Spec.Bifaciality * 135 / 1000.0;
        Assert.InRange(bnpi.PmpW / frontOnly.PmpW, expectedGain - 0.01, expectedGain + 0.01);
    }

    [Fact]
    public void EstimateCellTempC_AtNoctCondition_Near43C()
    {
        var spec = TrinaPvModuleCatalog.Neg21c20q760();

        double tCell = PvModuleSimulator.EstimateCellTempC(spec, ambientC: 20, gFrontWm2: 800);

        Assert.InRange(tCell, 42.0, 44.0);
    }

    [Fact]
    public void Degradation_Year0_IsIdentity_Year1_ScalesAbout99Percent()
    {
        var spec = TrinaPvModuleCatalog.Neg21c20q760();
        Assert.Equal(1, PvModuleSimulator.DegradationFactorFromYears(spec, 0));
        Assert.InRange(PvModuleSimulator.DegradationFactorFromYears(spec, 1), 0.989, 0.991);

        var fresh = PvModuleSimulator.CreateNeg21c20q();
        var aged = new PvModuleSimulator(spec, PvModuleSimulator.DegradationFactorFromYears(spec, 1));
        var p0 = fresh.Evaluate(1000, 25);
        var p1 = aged.Evaluate(1000, 25);
        Assert.InRange(p1.PmpW / p0.PmpW, 0.985, 0.995);
        Assert.InRange(p1.IscA / p0.IscA, 0.985, 0.995);
    }

    [Fact]
    public void CurrentAtVoltage_PassesThroughMppAndVoc()
    {
        var module = PvModuleSimulator.CreateNeg21c20q();
        var mpp = module.Evaluate(1000, 25);

        Assert.InRange(module.CurrentAtVoltage(mpp.VmpV, 1000, 25), mpp.ImpA - 0.05, mpp.ImpA + 0.05);
        Assert.InRange(module.CurrentAtVoltage(mpp.VocV, 1000, 25), -0.02, 0.02);
        Assert.InRange(module.CurrentAtVoltage(0, 1000, 25), mpp.IscA - 0.05, mpp.IscA + 0.05);
        Assert.Equal(0, module.CurrentAtVoltage(mpp.VocV + 1, 1000, 25));
    }
}
