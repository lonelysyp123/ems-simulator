using EssSimulator;
using EssSimulator.DataExchange.Adapters;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Pipeline;

namespace EssSimulator.Tests.DataExchange;

public class ControlFeedbackPipelineTests
{
    private sealed class FakeSimulationAdapter : ISimulationDataAdapter
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

        public void Set(string path, object value) => _values[path] = value;

        public object? Read(string fullPath) =>
            _values.TryGetValue(fullPath, out var v) ? v : null;

        public bool Write(string fullPath, object value)
        {
            _values[fullPath] = value;
            return true;
        }
    }

    private sealed class FakeModbusAdapter : IModbusRegisterAdapter
    {
        private readonly Dictionary<string, object> _registers = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, object> Registers => _registers;

        public void WriteDefaults(IReadOnlyDictionary<string, object> defaults) =>
            WritePoints(defaults);

        public void WritePoints(IReadOnlyDictionary<string, object> values)
        {
            foreach (var pair in values)
                _registers[pair.Key] = pair.Value;
        }

        public void WritePoints(IReadOnlyDictionary<string, object> values, byte? slaveId = null, bool applyScale = true) =>
            WritePoints(values);

        public Dictionary<string, object> ReadAllControlRaw(IReadOnlyList<string> paramNames, byte? slaveId = null)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in paramNames)
            {
                if (_registers.TryGetValue(name, out var raw))
                    result[name] = raw;
            }

            return result;
        }

        public object? ReadParsedPoint(string paramName, byte? slaveId = null) =>
            _registers.TryGetValue(paramName, out var val) ? val : null;
    }

    [Fact]
    public void RunOnce_Hold_KeepsModbusOneWhenSimulationOn()
    {
        var binding = new PointBinding
        {
            Entry = new MapEntry
            {
                Address = 5303,
                FunctionCode = 5,
                ParamName = "pcs1_startstop",
                Size = 1,
                Type = "bool"
            },
            ParamName = "pcs1_startstop",
            Target = new DataTarget { RootKey = "emu1", PropertyPath = "PcsList[0].pcsOnOffSwitch" },
            Semantics = ControlSemantics.Hold
        };

        var catalog = new PointCatalog
        {
            ServerName = "simEmu1",
            TelemetryPoints = Array.Empty<PointBinding>(),
            ControlPoints = new[] { binding },
            DefaultValues = new Dictionary<string, object>()
        };

        var simulation = new FakeSimulationAdapter();
        var modbus = new FakeModbusAdapter();
        var shadow = new ShadowStore();
        shadow.SeedControl("pcs1_startstop", 0);

        simulation.Set("emu1.PcsList[0].pcsOnOffSwitch", true);
        modbus.WritePoints(new Dictionary<string, object> { { "pcs1_startstop", 0 } });

        var pipeline = new ControlFeedbackPipeline(
            catalog, simulation, modbus, shadow, "simEmu1", logFeedback: false);

        pipeline.RunOnce();

        Assert.Equal(1, Convert.ToInt32(modbus.Registers["pcs1_startstop"]));
    }

    [Fact]
    public void PublishImmediate_WritesValueAndCommitsShadow()
    {
        var binding = new PointBinding
        {
            Entry = new MapEntry
            {
                Address = 5303,
                FunctionCode = 5,
                ParamName = "pcs1_startstop",
                Size = 1,
                Type = "bool"
            },
            ParamName = "pcs1_startstop",
            Target = new DataTarget { RootKey = "emu1", PropertyPath = "PcsList[0].pcsOnOffSwitch" },
            Semantics = ControlSemantics.Hold
        };

        var catalog = new PointCatalog
        {
            ServerName = "simEmu1",
            TelemetryPoints = Array.Empty<PointBinding>(),
            ControlPoints = new[] { binding },
            DefaultValues = new Dictionary<string, object>()
        };

        var simulation = new FakeSimulationAdapter();
        var modbus = new FakeModbusAdapter();
        var shadow = new ShadowStore();
        shadow.SeedControl("pcs1_startstop", 0);

        var pipeline = new ControlFeedbackPipeline(
            catalog, simulation, modbus, shadow, "simEmu1", logFeedback: false);

        Assert.True(pipeline.PublishImmediate("pcs1_startstop", true, out var applied));
        Assert.Equal(1, Convert.ToInt32(applied));
        Assert.Equal(1, Convert.ToInt32(modbus.Registers["pcs1_startstop"]));
    }
}
