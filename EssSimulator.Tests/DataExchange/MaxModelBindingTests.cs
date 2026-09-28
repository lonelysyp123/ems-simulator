using EssSimulator.DataExchange.Adapters;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Config;
using EssSimulator.DataExchange.Pipeline;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.DataExchange;

/// <summary>
/// 验证 ModelSim 取大类型 model=max——单元级「取最严重模块」口径（如过温降载 NTC = 两模块 IGBT 温度取大）：
/// 目录编译、遥测管道取最大值、缺失操作数忽略，以及 max 不作为控制目标。
/// </summary>
public class MaxModelBindingTests
{
    private const string MaxCsv = """
FunctionCode,Address,Type,Size,ParamName,Scale,Description,ModelSim
4,42000,int16,16,ycmax0,10,单元过温NTC两模块取大,model=max|arg1=emu1.PcsList[0].IGBTMaxTemp|arg2=emu1.PcsList[1].IGBTMaxTemp
4,42001,float32,32,ycplain0,1,普通遥测点位,model=4|arg1=emu1.Freq
4,42002,int16,16,ycmax1,10,缺第二操作数,model=max|arg1=emu1.PcsList[0].IGBTMaxTemp
6,43000,int16,16,ytmax0,1,max误配为控制点,model=max|arg1=emu1.X|arg2=emu1.Y
""";

    private static string WriteTempPointMap()
    {
        var path = Path.Combine(Path.GetTempPath(), $"max_pointmap_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, MaxCsv);
        return path;
    }

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
        public Dictionary<string, object> Registers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void WriteDefaults(IReadOnlyDictionary<string, object> defaults) => WritePoints(defaults);

        public void WritePoints(IReadOnlyDictionary<string, object> values, byte? slaveId = null, bool applyScale = true)
        {
            foreach (var pair in values)
                Registers[pair.Key] = pair.Value;
        }

        public Dictionary<string, object> ReadAllControlRaw(IReadOnlyList<string> paramNames, byte? slaveId = null) => new();

        public object? ReadParsedPoint(string paramName, byte? slaveId = null) =>
            Registers.TryGetValue(paramName, out var val) ? val : null;
    }

    [Fact]
    public void FromPointMap_ParsesMaxBindingPaths()
    {
        var path = WriteTempPointMap();
        try
        {
            var pointMap = new ModbusPointMap(path, "simEmu1");
            var catalog = PointCatalogLoader.FromPointMap(pointMap, "simEmu1", new DataExchangeOptions());

            var max = Assert.Single(catalog.MaxPoints, p => p.ParamName == "ycmax0");
            Assert.Equal("emu1.PcsList[0].IGBTMaxTemp", max.Paths[0]);
            Assert.Equal("emu1.PcsList[1].IGBTMaxTemp", max.Paths[1]);
            Assert.Equal(42000, max.Entry.Address);

            // max 点位不进入普通遥测绑定
            Assert.DoesNotContain(catalog.TelemetryPoints, p => p.ParamName == "ycmax0");
            // 普通点位不受影响
            Assert.Contains(catalog.TelemetryPoints, p => p.ParamName == "ycplain0");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FromPointMap_DropsMaxPointMissingSecondOperand()
    {
        var path = WriteTempPointMap();
        try
        {
            var pointMap = new ModbusPointMap(path, "simEmu1");
            var catalog = PointCatalogLoader.FromPointMap(pointMap, "simEmu1", new DataExchangeOptions());

            Assert.DoesNotContain(catalog.MaxPoints, p => p.ParamName == "ycmax1");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FromPointMap_MaxIsNotAControlTarget()
    {
        var path = WriteTempPointMap();
        try
        {
            var pointMap = new ModbusPointMap(path, "simEmu1");
            var catalog = PointCatalogLoader.FromPointMap(pointMap, "simEmu1", new DataExchangeOptions());

            Assert.Null(catalog.FindControl("ytmax0"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TelemetryPipeline_WritesMaxOfTwoPaths()
    {
        var path = WriteTempPointMap();
        try
        {
            var pointMap = new ModbusPointMap(path, "simEmu1");
            var catalog = PointCatalogLoader.FromPointMap(pointMap, "simEmu1", new DataExchangeOptions());

            var simulation = new FakeSimulationAdapter();
            simulation.Set("emu1.PcsList[0].IGBTMaxTemp", 45.5f);
            simulation.Set("emu1.PcsList[1].IGBTMaxTemp", 52.25f);

            var modbus = new FakeModbusAdapter();
            var pipeline = new TelemetryPipeline(catalog, simulation, modbus, new ShadowStore());

            pipeline.RunOnce();

            Assert.True(modbus.Registers.ContainsKey("ycmax0"));
            Assert.Equal(52.25, Convert.ToDouble(modbus.Registers["ycmax0"]), 6);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TelemetryPipeline_MaxIgnoresMissingOperand()
    {
        var path = WriteTempPointMap();
        try
        {
            var pointMap = new ModbusPointMap(path, "simEmu1");
            var catalog = PointCatalogLoader.FromPointMap(pointMap, "simEmu1", new DataExchangeOptions());

            var simulation = new FakeSimulationAdapter();
            simulation.Set("emu1.PcsList[0].IGBTMaxTemp", 38.5f);
            // 模块2 缺失：取现有最大值而非按 0

            var modbus = new FakeModbusAdapter();
            var pipeline = new TelemetryPipeline(catalog, simulation, modbus, new ShadowStore());

            pipeline.RunOnce();

            Assert.Equal(38.5, Convert.ToDouble(modbus.Registers["ycmax0"]), 6);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TelemetryPipeline_RewritesOnlyWhenMaxChanges()
    {
        var path = WriteTempPointMap();
        try
        {
            var pointMap = new ModbusPointMap(path, "simEmu1");
            var catalog = PointCatalogLoader.FromPointMap(pointMap, "simEmu1", new DataExchangeOptions());

            var simulation = new FakeSimulationAdapter();
            simulation.Set("emu1.PcsList[0].IGBTMaxTemp", 40f);
            simulation.Set("emu1.PcsList[1].IGBTMaxTemp", 48f);

            var modbus = new FakeModbusAdapter();
            var pipeline = new TelemetryPipeline(catalog, simulation, modbus, new ShadowStore());

            pipeline.RunOnce();
            modbus.Registers.Clear();

            // 值未变化：shadow 抑制重复写入
            pipeline.RunOnce();
            Assert.False(modbus.Registers.ContainsKey("ycmax0"));

            // 模块温度超过当前最大值：写入新最大值
            simulation.Set("emu1.PcsList[0].IGBTMaxTemp", 55f);
            pipeline.RunOnce();
            Assert.Equal(55.0, Convert.ToDouble(modbus.Registers["ycmax0"]), 6);

            // 模块温度回落但仍在最大值以下：写入回落后的最大值
            simulation.Set("emu1.PcsList[0].IGBTMaxTemp", 30f);
            pipeline.RunOnce();
            Assert.Equal(48.0, Convert.ToDouble(modbus.Registers["ycmax0"]), 6);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(null, null, 0)]
    [InlineData(null, 3.5, 3.5)]
    [InlineData("abc", 2.0, 2.0)]
    [InlineData(1, 2, 2)]
    [InlineData(-5, -3, -3)]
    public void ComputeMax_HandlesNullOrNonNumericOperands(object? a, object? b, double expected)
    {
        Assert.Equal(expected, MaxPointBinding.ComputeMax(a, b), 9);
    }
}
