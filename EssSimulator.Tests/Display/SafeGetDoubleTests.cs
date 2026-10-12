using EssSimulator.Core;
using EssSimulator.Display;

namespace EssSimulator.Tests.Display;

[Collection("SimulatorHost")]
public class SafeGetDoubleTests : SimulatorHostTestBase
{
    private sealed class Probe
    {
        public double Voltage { get; set; }
        public double Ok { get; set; }
    }

    [Fact]
    public void NonFinite_ReturnsFallback_FiniteValuePassesThrough()
    {
        SimulatorHost.Instance.Register("nanprobe", new Probe
        {
            Voltage = double.NaN,
            Ok = 12.5
        });

        Assert.Equal(0, GuiSimDataAccess.SafeGetDouble("nanprobe.Voltage"));
        Assert.Equal(-3, GuiSimDataAccess.SafeGetDouble("nanprobe.Voltage", -3));
        Assert.Equal(12.5, GuiSimDataAccess.SafeGetDouble("nanprobe.Ok"), 6);

        SimulatorHost.Instance.Register("infprobe", new Probe { Voltage = double.PositiveInfinity });
        Assert.Equal(0, GuiSimDataAccess.SafeGetDouble("infprobe.Voltage"));
    }
}
