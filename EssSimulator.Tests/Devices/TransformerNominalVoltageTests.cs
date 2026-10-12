using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Tests.Devices;

public class TransformerNominalVoltageTests
{
    [Fact]
    public void MainTransformer_ZeroVoltage_Uses220And35kV()
    {
        var cfg = TransformerDeviceFactory.CreateConfig(new TransformerConfig
        {
            PrimaryVoltage = 0,
            SecondaryVoltage = 0
        });

        Assert.Equal(220_000, cfg.PrimaryNominalLineVoltageV);
        Assert.Equal(35_000, cfg.SecondaryNominalLineVoltageV);
    }

    [Fact]
    public void UnitTransformer_ZeroVoltage_Uses35kVAnd690V()
    {
        var cfg = TransformerDeviceFactory.CreateConfig(new UnitTransformerConfig
        {
            PrimaryVoltage = 0,
            SecondaryVoltage = 0
        });

        Assert.Equal(35_000, cfg.PrimaryNominalLineVoltageV);
        Assert.Equal(690, cfg.SecondaryNominalLineVoltageV);
    }

    [Fact]
    public void ZeroPrimaryNominal_Step_KeepsStateFinite()
    {
        var device = new TransformerDevice("xf", new TransformerDeviceConfig
        {
            RatedPowerKva = 300_000,
            PrimaryNominalLineVoltageV = 0,
            SecondaryNominalLineVoltageV = 35_000,
            NoLoadCurrentPercent = 2
        });

        device.Update(220_000, 10, 1, 100, 0, DateTime.UtcNow, TimeSpan.FromMilliseconds(200));
        var state = device.GetCurrentState();

        Assert.True(double.IsFinite(state.PrimaryVoltage));
        Assert.True(double.IsFinite(state.SecondaryVoltage));
        Assert.True(double.IsFinite(state.PrimaryCurrent));
        Assert.True(double.IsFinite(state.SecondaryCurrent));
        Assert.True(double.IsFinite(state.LoadRatio));
        Assert.True(double.IsFinite(state.Power));
    }
}
