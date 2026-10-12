using EssSimulator.LocalControl;

namespace EssSimulator.Tests.LocalControl;

public class LcPcsReadyTests
{
    [Fact]
    public void BlackStartDisabled_NeverReadyEvenIfVoltageMatches() =>
        Assert.False(LcPcsReady.IsReady(false, 690, 690, 690, 690));

    [Theory]
    [InlineData(0)]
    [InlineData(-690)]
    public void NonPositiveSetpoint_NotReady(double setting) =>
        Assert.False(LcPcsReady.IsReady(true, setting, 0, 0, 0));

    [Theory]
    [InlineData(690, 690, 690, 690)]
    [InlineData(800, 760, 840, 800)]
    [InlineData(100, 95, 105, 100)]
    [InlineData(552, 525, 579, 552)]
    public void AllPhasesWithinFivePercent_Ready(double setting, double vab, double vbc, double vca) =>
        Assert.True(LcPcsReady.IsReady(true, setting, vab, vbc, vca));

    [Theory]
    [InlineData(690, 655.4, 690, 690)]
    [InlineData(690, 724.6, 690, 690)]
    [InlineData(690, 690, 655.4, 690)]
    [InlineData(690, 690, 690, 724.6)]
    [InlineData(800, 759.9, 800, 800)]
    [InlineData(800, 840.1, 800, 800)]
    public void AnyPhaseOutsideFivePercent_NotReady(double setting, double vab, double vbc, double vca) =>
        Assert.False(LcPcsReady.IsReady(true, setting, vab, vbc, vca));

    [Fact]
    public void UnmeasuredPhase_TreatedAsOutOfRange() =>
        Assert.False(LcPcsReady.IsReady(true, 690, double.NaN, 690, 690));

    [Fact]
    public void RampMidway_BelowBand_NotReady() =>
        Assert.False(LcPcsReady.IsReady(true, 690, 345, 345, 345));
}
