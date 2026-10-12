using EssSimulator.EssDeviceSimModel.Diagnostics;

namespace EssSimulator.Tests.Diagnostics;

public class SimStateChangeLoggerTests
{
    [Fact]
    public void ShouldLogNumericChange_IgnoresJitterWithinEpsilon()
    {
        Assert.False(SimStateChangeLogger.ShouldLogNumericChange(100, 100.01, 0.05));
        Assert.False(SimStateChangeLogger.ShouldLogNumericChange(0, 0, 0.05));
    }

    [Fact]
    public void ShouldLogNumericChange_TrueWhenDeltaExceedsEpsilon()
    {
        Assert.True(SimStateChangeLogger.ShouldLogNumericChange(0, 10, 0.05));
        Assert.True(SimStateChangeLogger.ShouldLogNumericChange(-500, 0, 0.05));
    }
}
