using EssSimulator.EssDeviceSimModel.Pv;

namespace EssSimulator.Tests.Pv;

public class PvIrradianceModelTests
{
    [Fact]
    public void NinetyDegrees_IsPeak()
    {
        double g = PvIrradianceModel.EvaluatePlaneOfArrayWm2(90);
        Assert.InRange(g, 990, 1000);
    }

    [Fact]
    public void ZeroAndOneEighty_AreZero()
    {
        Assert.Equal(0, PvIrradianceModel.EvaluatePlaneOfArrayWm2(0));
        Assert.Equal(0, PvIrradianceModel.EvaluatePlaneOfArrayWm2(180));
        Assert.Equal(0, PvIrradianceModel.EvaluatePlaneOfArrayWm2(-10));
        Assert.Equal(0, PvIrradianceModel.EvaluatePlaneOfArrayWm2(200));
    }

    [Fact]
    public void SixtyDegrees_FollowsSine()
    {
        double g = PvIrradianceModel.EvaluatePlaneOfArrayWm2(60);
        Assert.InRange(g, 860, 870);
    }

    [Fact]
    public void SymmetricAboutNinety()
    {
        Assert.Equal(
            PvIrradianceModel.EvaluatePlaneOfArrayWm2(60),
            PvIrradianceModel.EvaluatePlaneOfArrayWm2(120),
            9);
        Assert.Equal(
            PvIrradianceModel.EvaluatePlaneOfArrayWm2(30),
            PvIrradianceModel.EvaluatePlaneOfArrayWm2(150),
            9);
    }
}
