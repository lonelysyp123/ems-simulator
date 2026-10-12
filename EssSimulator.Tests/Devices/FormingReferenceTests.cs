using EssSimulator.EssDeviceSimModel;

namespace EssSimulator.Tests.Devices;

public class FormingReferenceTests
{
    [Fact]
    public void AverageFormingReference_averages_frequency_and_circular_mean_phase()
    {
        var (f, th) = EssIslandBusLogic.AverageFormingReference(new (double, double, double)[]
        {
            (400, 48, 0),
            (600, 52, Math.PI / 2)
        });

        Assert.Equal(50, f, 6);
        Assert.Equal(Math.PI / 4, th, 6);
    }

    [Fact]
    public void AverageFormingReference_ignores_dead_sources_and_empty_is_zero()
    {
        var empty = EssIslandBusLogic.AverageFormingReference(Array.Empty<(double, double, double)>());
        Assert.Equal(0, empty.FrequencyHz);
        Assert.Equal(0, empty.PhaseRad);

        var (f, th) = EssIslandBusLogic.AverageFormingReference(new (double, double, double)[]
        {
            (0, 50, 1),
            (500, 51, 0.4)
        });
        Assert.Equal(51, f, 6);
        Assert.Equal(0.4, th, 6);
    }

    [Fact]
    public void AverageFormingVoltageCommand_averages_positive_commands()
    {
        Assert.Equal(0, EssIslandBusLogic.AverageFormingVoltageCommand(Array.Empty<double>()));
        Assert.Equal(500, EssIslandBusLogic.AverageFormingVoltageCommand(new[] { 400.0, 600.0 }), 6);
        Assert.Equal(600, EssIslandBusLogic.AverageFormingVoltageCommand(new[] { 0.0, 600.0 }), 6);
    }
}
