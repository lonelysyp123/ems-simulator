using EssSimulator.EssDeviceSimModel.Control;

namespace EssSimulator.Tests.Control;

public class VoltageRampGeneratorTests
{
    [Fact]
    public void Step_ZeroTo690At138Vs_ReachesExactlyAtFiveSecondsWithoutOvershoot()
    {
        var ramp = new VoltageRampGenerator(upRate: 138, downRate: 138);
        ramp.Target = 690;

        double elapsed = 0;
        const double dt = 0.2;
        while (elapsed + 1e-12 < 5.0)
        {
            ramp.Step(dt);
            elapsed += dt;
            if (elapsed + 1e-12 < 5.0)
                Assert.True(ramp.Output < 690, $"t={elapsed:F1}s 应尚未到 690，实际 {ramp.Output}");
        }

        Assert.Equal(5.0, elapsed, 9);
        Assert.Equal(690, ramp.Output, 9);
    }

    [Fact]
    public void Step_DoesNotOvershootTarget_WhenDtWouldCross()
    {
        var ramp = new VoltageRampGenerator(upRate: 138, downRate: 60);
        ramp.Target = 690;
        ramp.Step(10);
        Assert.Equal(690, ramp.Output);
    }

    [Fact]
    public void Step_DownRateIndependentOfUpRate()
    {
        var ramp = new VoltageRampGenerator(upRate: 138, downRate: 60);
        ramp.Reset(690);
        ramp.Target = 0;
        ramp.Step(1.0);
        Assert.Equal(630, ramp.Output);
    }

    [Fact]
    public void Step_ZeroOrNegativeDt_LeavesOutputUnchanged()
    {
        var ramp = new VoltageRampGenerator(upRate: 138, downRate: 138);
        ramp.Target = 690;
        ramp.Step(1.0);
        double after = ramp.Output;
        ramp.Step(0);
        ramp.Step(-0.1);
        Assert.Equal(after, ramp.Output);
    }

    [Fact]
    public void Reset_JumpsWithoutRamping()
    {
        var ramp = new VoltageRampGenerator(upRate: 138, downRate: 138);
        ramp.Target = 690;
        ramp.Step(1.0);
        Assert.True(ramp.Output > 0);
        ramp.Reset(0);
        Assert.Equal(0, ramp.Output);
        Assert.Equal(0, ramp.Target);
    }
}
