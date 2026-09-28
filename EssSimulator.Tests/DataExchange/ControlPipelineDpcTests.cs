using EssSimulator.Core;
using EssSimulator.DataExchange;
using EssSimulator.DataExchange.Adapters;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Effects;
using EssSimulator.DataExchange.Pipeline;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.DataExchange;

[Collection("SimulatorHost")]
public class ControlPipelineDpcTests
{
    private sealed class FakeSimulationAdapter : ISimulationDataAdapter
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

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

        public void WriteDefaults(IReadOnlyDictionary<string, object> defaults) => WritePoints(defaults);

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

    private static PointBinding Yk3Binding() => new()
    {
        Entry = new MapEntry
        {
            Address = 1003,
            FunctionCode = 5,
            ParamName = "yk3",
            Size = 1,
            Type = "bool"
        },
        ParamName = "yk3",
        Target = new DataTarget { RootKey = "emu1", PropertyPath = "PcsList[0].pcsOnOffSwitch" },
        Semantics = ControlSemantics.Hold,
        Effect = ControlEffectId.PcsApplyCommands
    };

    private static ModbusParser CreateParser()
    {
        // 固定加载 standard 型号 EMU 点表（含 yk3），避免受运行期设备型号选型劫持
        var path = Path.Combine(FindRepoRoot(), "pointmaps", "models", "emu", "standard", "emu.csv");
        var pointMap = new ModbusPointMap(path, "simEmu1");
        return new ModbusParser(pointMap.RawMaps);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EssSimulator.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("未找到仓库根目录");
    }

    [Fact]
    public void RunOnce_DoesNotRevert_WhenModbusMatchesDpcStop()
    {
        var catalog = new PointCatalog
        {
            ServerName = "simEmu1",
            TelemetryPoints = Array.Empty<PointBinding>(),
            ControlPoints = new[] { Yk3Binding() },
            DefaultValues = new Dictionary<string, object>()
        };

        var simulation = new FakeSimulationAdapter();
        var modbus = new FakeModbusAdapter();
        var shadow = new ShadowStore();

        simulation.Write("emu1.PcsList[0].pcsOnOffSwitch", false);
        shadow.CommitControl("yk3", 0);
        modbus.WritePoints(new Dictionary<string, object> { { "yk3", 0 } });

        var control = new ControlPipeline(
            catalog, simulation, modbus, CreateParser(),
            shadow, new ControlEffectRegistry(), "simEmu1", logControlChanges: false);

        control.RunOnce();

        Assert.False(Convert.ToBoolean(simulation.Read("emu1.PcsList[0].pcsOnOffSwitch")!));
        Assert.Equal(0, Convert.ToInt32(modbus.ReadParsedPoint("yk3")!));
    }

    [Fact]
    public void RunOnce_Skipped_WhenThirdPartyEmsExclusive()
    {
        var catalog = new PointCatalog
        {
            ServerName = "simEmu1",
            TelemetryPoints = Array.Empty<PointBinding>(),
            ControlPoints = new[] { Yk3Binding() },
            DefaultValues = new Dictionary<string, object>()
        };

        var simulation = new FakeSimulationAdapter();
        var modbus = new FakeModbusAdapter();
        var shadow = new ShadowStore();
        simulation.Write("emu1.PcsList[0].pcsOnOffSwitch", false);
        shadow.CommitControl("yk3", 0);
        var yk3 = Yk3Binding();
        modbus.WritePoints(new Dictionary<string, object>
        {
            { "yk3", ModbusPointCodec.Encode(true, yk3.Entry, applyScale: true) }
        });

        ExternalControlGate.SetBlocked(true);
        try
        {
            var control = new ControlPipeline(
                catalog, simulation, modbus, CreateParser(),
                shadow, new ControlEffectRegistry(), "simEmu1", logControlChanges: false);
            control.RunOnce();
            Assert.False(Convert.ToBoolean(simulation.Read("emu1.PcsList[0].pcsOnOffSwitch")!));
        }
        finally
        {
            ExternalControlGate.Reset();
        }
    }

    [Fact]
    public void RunOnce_NotifiesControlPointCapture_OnHoldChange()
    {
        var catalog = new PointCatalog
        {
            ServerName = "simEmu1",
            TelemetryPoints = Array.Empty<PointBinding>(),
            ControlPoints = new[] { Yk3Binding() },
            DefaultValues = new Dictionary<string, object>()
        };

        var simulation = new FakeSimulationAdapter();
        var modbus = new FakeModbusAdapter();
        var shadow = new ShadowStore();
        simulation.Write("emu1.PcsList[0].pcsOnOffSwitch", false);
        shadow.CommitControl("yk3", 0);
        var yk3 = Yk3Binding();
        modbus.WritePoints(new Dictionary<string, object>
        {
            { "yk3", ModbusPointCodec.Encode(true, yk3.Entry, applyScale: true) }
        });

        var recorder = new RecordingControlPointCapture();
        ControlPointCapture.Current = recorder;
        try
        {
            var control = new ControlPipeline(
                catalog, simulation, modbus, CreateParser(),
                shadow, new ControlEffectRegistry(), "simEmu1", logControlChanges: false);
            control.RunOnce();
            Assert.Equal(1, recorder.Count);
            Assert.Equal("simEmu1", recorder.LastServerName);
            Assert.Equal("yk3", recorder.LastParamName);
        }
        finally
        {
            ControlPointCapture.Reset();
        }
    }

    private sealed class RecordingControlPointCapture : IControlPointCapture
    {
        public int Count { get; private set; }
        public string? LastServerName { get; private set; }
        public string? LastParamName { get; private set; }

        public void OnControlApplied(string serverName, PointBinding binding, object applied, object? previous)
        {
            Count++;
            LastServerName = serverName;
            LastParamName = binding.ParamName;
        }
    }
}
