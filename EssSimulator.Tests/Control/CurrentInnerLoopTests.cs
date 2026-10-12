using EssSimulator.EssDeviceSimModel.Control;

namespace EssSimulator.Tests.Control;

public class DiscretePiControllerTests
{
    [Fact]
    public void Step_ClampsOutputToLimits()
    {
        var pi = new DiscretePiController(kp: 10, ki: 50, outMin: -5, outMax: 5);
        double y = pi.Step(error: 10, dt: 0.01);
        Assert.InRange(y, -5, 5);
        Assert.Equal(5, y);
    }

    [Fact]
    public void Step_DoesNotWindUpWhileSaturated()
    {
        var pi = new DiscretePiController(kp: 1, ki: 100, outMin: -1, outMax: 1);
        for (int i = 0; i < 50; i++)
            pi.Step(error: 1, dt: 0.01);

        Assert.Equal(1, pi.Output);
        double integralAtSat = pi.Integral;

        pi.Step(error: 1, dt: 0.01);
        Assert.Equal(integralAtSat, pi.Integral, 9);
    }

    [Fact]
    public void Step_ZeroOrNegativeDt_LeavesOutputUnchanged()
    {
        var pi = new DiscretePiController(kp: 2, ki: 1, outMin: -10, outMax: 10);
        pi.Step(1, 0.1);
        double y = pi.Output;
        pi.Step(100, 0);
        pi.Step(100, -0.01);
        Assert.Equal(y, pi.Output);
    }
}

public class CurrentInnerLoopTests
{
    [Fact]
    public void Step_StepIref_DoesNotExceedImax()
    {
        var loop = new CurrentInnerLoop(kp: 8, ki: 40);
        for (int i = 0; i < 40; i++)
            loop.Step(iRef: 500, iMeas: loop.Output, dt: 0.01, iMax: 100);

        Assert.True(loop.Output <= 100 + 1e-9);
        Assert.True(loop.Saturated);
    }

    [Fact]
    public void Step_ZeroDt_HoldsOutput()
    {
        var loop = new CurrentInnerLoop(kp: 8, ki: 40);
        loop.Step(50, 0, 0.01, 100);
        double y = loop.Output;
        loop.Step(999, y, 0, 100);
        Assert.Equal(y, loop.Output);
    }
}
