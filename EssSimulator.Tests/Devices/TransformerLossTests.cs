using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Tests.Devices;

public class TransformerLossTests
{
    [Fact]
    public void NominalVoltage_SplitsIronAndCopperByLoadRatio()
    {
        var full = TransformerLoss.Compute(100, 200, 35000, 35000, loadRatio: 1);
        Assert.Equal(100, full.IronLossW, 6);
        Assert.Equal(200, full.CopperLossW, 6);

        var half = TransformerLoss.Compute(100, 200, 35000, 35000, loadRatio: 0.5);
        Assert.Equal(100, half.IronLossW, 6);
        Assert.Equal(50, half.CopperLossW, 6);
    }

    [Fact]
    public void VoltageSquared_ScalesIronLossOnly()
    {
        var loss = TransformerLoss.Compute(100, 200, 17500, 35000, loadRatio: 1);
        Assert.Equal(25, loss.IronLossW, 6);
        Assert.Equal(200, loss.CopperLossW, 6);
    }

    [Fact]
    public void ZeroVoltage_ClearsIronLoss()
    {
        var loss = TransformerLoss.Compute(100, 200, 0, 35000, loadRatio: 1);
        Assert.Equal(0, loss.IronLossW, 6);
        Assert.Equal(200, loss.CopperLossW, 6);
    }
}
