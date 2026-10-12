using EssSimulator.EssDeviceSimModel.Control;

namespace EssSimulator.Tests.Control;

public class QvDroopRegulatorTests
{
    [Fact]
    public void Compute_ExportingQ_LowersVref()
    {
        var droop = new QvDroopRegulator(nq: 0.02, q0Kvar: 0, deadbandKvar: 0, vMin: 0, vMax: 759);
        Assert.Equal(688, droop.Compute(690, 100), 9);
    }

    [Fact]
    public void Compute_WithinDeadband_DoesNotChangeVref()
    {
        var droop = new QvDroopRegulator(nq: 0.02, q0Kvar: 0, deadbandKvar: 150, vMin: 0, vMax: 759);
        Assert.Equal(690, droop.Compute(690, 100), 9);
    }

    [Fact]
    public void Compute_AbsorbingQ_RaisesVrefButNotAboveVmax()
    {
        var droop = new QvDroopRegulator(nq: 0.02, q0Kvar: 0, deadbandKvar: 0, vMin: 0, vMax: 759);
        double vRef = droop.Compute(690, -100);
        Assert.True(vRef > 690);
        Assert.True(vRef <= 759);
        Assert.Equal(692, vRef, 9);
    }

    [Fact]
    public void Compute_ZeroNq_ReturnsVramp()
    {
        var droop = new QvDroopRegulator(nq: 0, q0Kvar: 0, deadbandKvar: 0, vMin: 0, vMax: 759);
        Assert.Equal(690, droop.Compute(690, 500), 9);
    }

    [Fact]
    public void Compute_ClampsToVmax()
    {
        var droop = new QvDroopRegulator(nq: 1, q0Kvar: 0, deadbandKvar: 0, vMin: 0, vMax: 700);
        Assert.Equal(700, droop.Compute(690, -50), 9);
    }
}
