using EssSimulator.EssDeviceSimModel.Pv;

namespace EssSimulator.Tests.Pv;

public class PvStringSimulatorTests
{
    [Fact]
    public void Stc_SeriesVoltageAndPowerScaleWithModuleCount()
    {
        var module = PvModuleSimulator.CreateNeg21c20q();
        var str = PvStringSimulator.CreateDefault();
        var m = module.Evaluate(1000, 25);
        var s = str.Evaluate(1000, 25);

        Assert.Equal(PvStringSimulator.DefaultModuleCount, str.ModuleCount);
        Assert.InRange(s.VocV, m.VocV * 30 - 1, m.VocV * 30 + 1);
        Assert.InRange(s.VmpV, m.VmpV * 30 - 1, m.VmpV * 30 + 1);
        Assert.InRange(s.PmpW, m.PmpW * 30 - 30, m.PmpW * 30 + 30);
        Assert.InRange(s.ImpA, m.ImpA - 0.05, m.ImpA + 0.05);
        Assert.InRange(s.IscA, m.IscA - 0.05, m.IscA + 0.05);
    }

    [Fact]
    public void ZeroIrradiance_OutputsZero()
    {
        var str = PvStringSimulator.CreateDefault();
        var s = str.Evaluate(0, 25);
        Assert.Equal(0, s.PmpW);
        Assert.Equal(0, s.VocV);
    }
}
