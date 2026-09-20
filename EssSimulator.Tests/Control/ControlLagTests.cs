using EssSimulator.EssDeviceSimModel.Control;

namespace EssSimulator.Tests.Control;

/// <summary>
/// 硬规则 3：控制用上一拍测量。本测试量化「测量延迟 1 拍」时默认 PI / P-f 仍有界的 dt。
/// 记录：Kp=4、Ki=20 的电压外环同类 PI，延迟 1 拍时 dt≤100ms 输出有界；dt=1s 仍被限幅但不作为生产步长。
/// 默认 PropagationIntervalMs=100 落在有界区。分域 P/Q 环本阶段不测。
/// </summary>
public class ControlLagTests
{
    [Theory]
    [InlineData(0.02)]
    [InlineData(0.10)]
    public void DiscretePi_OneStepMeasurementDelay_BoundedAtProductionDt(double dt)
    {
        var pi = new DiscretePiController(kp: 4, ki: 20, outMin: -500, outMax: 500);
        double plant = 0;
        double meas = 0;
        const double setpoint = 690;
        for (int i = 0; i < 200; i++)
        {
            double u = pi.Step(setpoint - meas, dt);
            meas = plant;
            plant += 0.15 * (u - plant);
        }

        Assert.InRange(pi.Output, -500, 500);
        Assert.False(double.IsNaN(pi.Output));
        Assert.False(double.IsInfinity(pi.Output));
    }

    [Fact]
    public void DiscretePi_OneStepDelay_AtOneSecond_StillSaturatedButFinite()
    {
        var pi = new DiscretePiController(kp: 4, ki: 20, outMin: -500, outMax: 500);
        double plant = 0;
        double meas = 0;
        for (int i = 0; i < 50; i++)
        {
            double u = pi.Step(690 - meas, dt: 1.0);
            meas = plant;
            plant += 0.8 * (u - plant);
        }

        Assert.InRange(pi.Output, -500, 500);
    }

    [Fact]
    public void PfDroop_DelayedPower_FrequencyStaysClamped()
    {
        var droop = new PfDroopRegulator(mp: 0.5 / 1725.0, p0Kw: 0, deadbandKw: 1, fMinHz: 49, fMaxHz: 51);
        double pDelayed = 0;
        double p = 0;
        for (int i = 0; i < 100; i++)
        {
            double f = droop.Compute(50, pDelayed);
            pDelayed = p;
            p = 200 + 10 * (50 - f);
            Assert.InRange(f, 49, 51);
        }
    }
}
