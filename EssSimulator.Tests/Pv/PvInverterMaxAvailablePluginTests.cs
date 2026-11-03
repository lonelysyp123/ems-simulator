using EssSimulator.DataExchange.Adapters;
using EssSimulator.DataExchange.Plugins;

namespace EssSimulator.Tests.Pv;

public class PvInverterMaxAvailablePluginTests
{
    [Theory]
    [InlineData(329.3, 320, 320)]
    [InlineData(180.5, 320, 180.5)]
    [InlineData(-10, 320, 0)]
    public void Compute_ClampsAvailableAcPowerToRated(double available, double rated, double expected)
    {
        var simulation = new FakeSimulationAdapter();
        simulation.Set("pv99.Inverters[0].AvailableAcPowerKw", available);
        simulation.Set("pv99.Inverters[0].RatedPowerKw", rated);
        var plugin = new PvInverterMaxAvailablePlugin();

        object? value = plugin.Compute(PvInverterMaxAvailablePlugin.WordKey, "pv99.Inverters[0]", simulation);

        Assert.Equal(expected, Convert.ToDouble(value), 6);
    }

    [Fact]
    public void Compute_ReturnsNullWhenModelValueMissing()
    {
        var plugin = new PvInverterMaxAvailablePlugin();

        Assert.Null(plugin.Compute(PvInverterMaxAvailablePlugin.WordKey, "pv99.Inverters[0]",
            new FakeSimulationAdapter()));
    }

    [Fact]
    public void CanHandle_MatchesConfiguredWordKeyIgnoringCase()
    {
        var plugin = new PvInverterMaxAvailablePlugin();

        Assert.True(plugin.CanHandle(PvInverterMaxAvailablePlugin.WordKey));
        Assert.True(plugin.CanHandle("invertermaxavailablekw"));
        Assert.False(plugin.CanHandle("Other"));
    }

    private sealed class FakeSimulationAdapter : ISimulationDataAdapter
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);

        public void Set(string path, object value) => _values[path] = value;

        public object? Read(string fullPath) =>
            _values.TryGetValue(fullPath, out var value) ? value : null;

        public bool Write(string fullPath, object value)
        {
            _values[fullPath] = value;
            return true;
        }
    }
}
