using EssSimulator.LocalControl;

namespace EssSimulator.Tests.LocalControl;

public class DcChannelRunStatusTests
{
    [Theory]
    [InlineData(false, 0, 0, DcChannelRunStatus.Off)]
    [InlineData(false, 100, 0, DcChannelRunStatus.Off)]
    [InlineData(false, 0, -50, DcChannelRunStatus.Off)]
    public void Encode_WhenOff_IsShutdown(bool isOn, double p, double q, int expected)
    {
        Assert.Equal(expected, DcChannelRunStatus.Encode(isOn, p, q));
    }

    [Theory]
    [InlineData(true, 0, 0)]
    [InlineData(true, 0.0, -0.0)]
    public void Encode_WhenOnAndPowerZero_IsStandby(bool isOn, double p, double q)
    {
        Assert.Equal(DcChannelRunStatus.Standby, DcChannelRunStatus.Encode(isOn, p, q));
    }

    [Theory]
    [InlineData(true, 1, 0)]
    [InlineData(true, -1, 0)]
    [InlineData(true, 0, 1)]
    [InlineData(true, 0, -1)]
    [InlineData(true, 80, 20)]
    public void Encode_WhenOnAndAnyPower_IsRunning(bool isOn, double p, double q)
    {
        Assert.Equal(DcChannelRunStatus.Running, DcChannelRunStatus.Encode(isOn, p, q));
    }
}
