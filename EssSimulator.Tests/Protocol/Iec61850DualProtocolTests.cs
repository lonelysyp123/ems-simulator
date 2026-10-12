using EssSimulator.DataExchange.Adapters;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Effects;
using EssSimulator.DataExchange.Pipeline;
using EssSimulator.Protocol.Iec61850;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.Protocol;

public class Iec61850DualProtocolTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EssSimulator.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("找不到仓库根目录");
    }

    private sealed class FakeSimulationAdapter : ISimulationDataAdapter
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);
        public object? Read(string fullPath) => _values.TryGetValue(fullPath, out var v) ? v : null;
        public bool Write(string fullPath, object value)
        {
            _values[fullPath] = value;
            return true;
        }
    }

    private sealed class FakeModbusAdapter : IModbusRegisterAdapter
    {
        private readonly Dictionary<string, object> _registers = new(StringComparer.OrdinalIgnoreCase);
        public void WriteDefaults(IReadOnlyDictionary<string, object> defaults) => WritePoints(defaults);
        public void WritePoints(IReadOnlyDictionary<string, object> values, byte? slaveId = null, bool applyScale = true)
        {
            foreach (var pair in values)
                _registers[pair.Key] = pair.Value;
        }
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

    [NativeLibraryFact]
    public void OperateAndModbusWrite_SharePointShadowAndSimulation()
    {
        var path = Path.Combine(FindRepoRoot(), "pointmaps", "models", "emu", "standard", "emu.csv");
        var pointMap = new ModbusPointMap(path, "simEmu1");
        var parser = new ModbusParser(pointMap.RawMaps);
        var binding = new PointBinding
        {
            Entry = pointMap.ControlMaps.First(m => m.ParamName == "yk3"),
            ParamName = "yk3",
            Target = new DataTarget { RootKey = "emu1", PropertyPath = "PcsList[0].pcsOnOffSwitch" },
            Semantics = ControlSemantics.Hold,
            Effect = ControlEffectId.None
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
        var store = new ProtocolPointStore(modbus);
        var shadow = new ShadowStore();
        simulation.Write("emu1.PcsList[0].pcsOnOffSwitch", false);
        shadow.CommitControl("yk3", 0);
        store.WritePoints(new Dictionary<string, object> { ["yk3"] = 0 }, applyScale: false);

        var pipeline = new ControlPipeline(
            catalog, simulation, store, parser, shadow, new ControlEffectRegistry(), "simEmu1", false);

        var mapping = Iec61850Mapping.Load(Path.Combine(FindRepoRoot(), "pointmaps", "models", "emu", "iec61850", "mapping.csv"));
        using var ied = new Iec61850IedServer("simEmu1", 0, mapping);
        ied.Attach(store, (name, value) =>
        {
            store.WritePoints(new Dictionary<string, object> { [name] = value }, applyScale: false);
            pipeline.RunOnce();
        });

        Assert.True(ied.TryOperate("PCS/GGIO1.SPCSO1", true, out var err), err);
        Assert.True(Convert.ToBoolean(simulation.Read("emu1.PcsList[0].pcsOnOffSwitch")!));
        Assert.Equal(1, Convert.ToDouble(store.GetCachedValue("yk3")));

        store.WritePoints(new Dictionary<string, object> { ["yk3"] = 0 }, applyScale: false);
        pipeline.RunOnce();
        Assert.False(Convert.ToBoolean(simulation.Read("emu1.PcsList[0].pcsOnOffSwitch")!));
    }

    [Fact]
    public void ProtocolPointStore_RaisesChange_OnTelemetryWrite()
    {
        var inner = new FakeModbusAdapter();
        var store = new ProtocolPointStore(inner);
        IReadOnlyDictionary<string, object>? seen = null;
        store.PointsChanged += (_, e) => seen = e.Values;
        store.WritePoints(new Dictionary<string, object> { ["yc27"] = 55.0 });
        Assert.NotNull(seen);
        Assert.Equal(55.0, Convert.ToDouble(seen!["yc27"]));
        Assert.Equal(55.0, Convert.ToDouble(store.GetCachedValue("yc27")));
    }
}
