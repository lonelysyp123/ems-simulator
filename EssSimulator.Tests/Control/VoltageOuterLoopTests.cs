using EssSimulator.EssDeviceSimModel.Control;

namespace EssSimulator.Tests.Control;

public class VoltageOuterLoopTests
{
    [Fact]
    public void Step_RampVref_IrefStaysWithinImax()
    {
        var outer = new VoltageOuterLoop(kp: 2, ki: 10);
        double vRef = 0;
        for (int i = 0; i < 50; i++)
        {
            vRef = Math.Min(690, vRef + 13.8);
            double iRef = outer.Step(vRef, vMeas: vRef * 0.5, dt: 0.1, iMax: 200);
            Assert.InRange(iRef, -200, 200);
        }
    }

    [Fact]
    public void Step_MatchedVoltage_IrefGoesToZero()
    {
        var outer = new VoltageOuterLoop(kp: 4, ki: 20);
        double iRef = 0;
        for (int i = 0; i < 80; i++)
            iRef = outer.Step(vRef: 690, vMeas: 690, dt: 0.02, iMax: 500);

        Assert.InRange(iRef, -1, 1);
    }
}
