using EssSimulator.DataExchange.Adapters;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Config;
using EssSimulator.DataExchange.Pipeline;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.DataExchange;

/// <summary>
/// 验证 ModelSim 加法类型 model=sum：
/// 经典两路径（arg1/arg2）与 arg1 分号分隔多路径（多机组聚合），
/// 目录编译、遥测管道求和、缺失操作数按 0 处理，以及 sum 不作为控制目标。
/// </summary>
public class SumModelBindingTests
{
    private const string SumCsv = """
FunctionCode,Address,Type,Size,ParamName,Scale,Description,ModelSim
4,40000,float32,32,ycsum0,1,双PCS有功合计,model=sum|arg1=emu1.PcsList[0].ActivePower|arg2=emu1.PcsList[1].ActivePower
4,40002,float32,32,ycplain0,1,普通遥测点位,model=4|arg1=emu1.Freq
4,40004,float32,32,ycsum1,1,缺第二操作数,model=sum|arg1=emu1.PcsList[0].ActivePower
4,40008,float32,32,ycsummulti0,1,四机组限值多路径求和,model=sum|arg1=emu1.Emu.MaxChargePower;emu2.Emu.MaxChargePower;emu3.Emu.MaxChargePower;emu4.Emu.MaxChargePower|arg2=|arg3=|arg4=10
6,41000,int16,16,ytsum0,1,sum误配为控制点,model=sum|arg1=emu1.X|arg2=emu1.Y
""";

    private static string WriteTempPointMap()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sum_pointmap_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, SumCsv);
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
    public void FromPointMap_ParsesSumBindingPaths()
    {
        var path = WriteTempPointMap();
        try
        {
            var pointMap = new ModbusPointMap(path, "simEmu1");
            var catalog = PointCatalogLoader.FromPointMap(pointMap, "simEmu1", new DataExchangeOptions());

            var sum = Assert.Single(catalog.SumPoints, p => p.ParamName == "ycsum0");
            Assert.Equal("emu1.PcsList[0].ActivePower", sum.FirstPath);
            Assert.Equal("emu1.PcsList[1].ActivePower", sum.SecondPath);
            Assert.Equal(40000, sum.Entry.Address);

            // sum 点位不进入普通遥测绑定
            Assert.DoesNotContain(catalog.TelemetryPoints, p => p.ParamName == "ycsum0");
            // 普通点位不受影响
            Assert.Contains(catalog.TelemetryPoints, p => p.ParamName == "ycplain0");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FromPointMap_DropsSumPointMissingSecondOperand()
    {
        var path = WriteTempPointMap();
        try
        {
            var pointMap = new ModbusPointMap(path, "simEmu1");
            var catalog = PointCatalogLoader.FromPointMap(pointMap, "simEmu1", new DataExchangeOptions());

            Assert.DoesNotContain(catalog.SumPoints, p => p.ParamName == "ycsum1");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FromPointMap_SumIsNotAControlTarget()
    {
        var path = WriteTempPointMap();
        try
        {
            var pointMap = new ModbusPointMap(path, "simEmu1");
            var catalog = PointCatalogLoader.FromPointMap(pointMap, "simEmu1", new DataExchangeOptions());

            Assert.Null(catalog.FindControl("ytsum0"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TelemetryPipeline_WritesSumOfTwoPaths()
    {
        var path = WriteTempPointMap();
        try
        {
            var pointMap = new ModbusPointMap(path, "simEmu1");
            var catalog = PointCatalogLoader.FromPointMap(pointMap, "simEmu1", new DataExchangeOptions());

            var simulation = new FakeSimulationAdapter();
            simulation.Set("emu1.PcsList[0].ActivePower", 100.5f);
            simulation.Set("emu1.PcsList[1].ActivePower", 200.25f);

            var modbus = new FakeModbusAdapter();
            var pipeline = new TelemetryPipeline(catalog, simulation, modbus, new ShadowStore());

            pipeline.RunOnce();

            Assert.True(modbus.Registers.ContainsKey("ycsum0"));
            Assert.Equal(300.75, Convert.ToDouble(modbus.Registers["ycsum0"]), 6);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TelemetryPipeline_TreatsMissingOperandAsZero()
    {
        var path = WriteTempPointMap();
        try
        {
            var pointMap = new ModbusPointMap(path, "simEmu1");
            var catalog = PointCatalogLoader.FromPointMap(pointMap, "simEmu1", new DataExchangeOptions());

            var simulation = new FakeSimulationAdapter();
            simulation.Set("emu1.PcsList[0].ActivePower", 123.5f);

            var modbus = new FakeModbusAdapter();
            var pipeline = new TelemetryPipeline(catalog, simulation, modbus, new ShadowStore());

            pipeline.RunOnce();

            Assert.Equal(123.5, Convert.ToDouble(modbus.Registers["ycsum0"]), 6);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FromPointMap_ParsesMultiPathSumBinding()
    {
        var path = WriteTempPointMap();
        try
        {
            var pointMap = new ModbusPointMap(path, "simEmu1");
            var catalog = PointCatalogLoader.FromPointMap(pointMap, "simEmu1", new DataExchangeOptions());

            var sum = Assert.Single(catalog.SumPoints, p => p.ParamName == "ycsummulti0");
            Assert.Equal(4, sum.Paths.Count);
            Assert.Equal("emu1.Emu.MaxChargePower", sum.FirstPath);
            Assert.Equal("emu4.Emu.MaxChargePower", sum.Paths[3]);
            Assert.DoesNotContain(catalog.TelemetryPoints, p => p.ParamName == "ycsummulti0");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TelemetryPipeline_WritesSumOfMultiPaths()
    {
        var path = WriteTempPointMap();
        try
        {
            var pointMap = new ModbusPointMap(path, "simEmu1");
            var catalog = PointCatalogLoader.FromPointMap(pointMap, "simEmu1", new DataExchangeOptions());

            var simulation = new FakeSimulationAdapter();
            simulation.Set("emu1.Emu.MaxChargePower", 1000f);
            simulation.Set("emu2.Emu.MaxChargePower", 1000f);
            // emu3 缺失按 0 处理
            simulation.Set("emu4.Emu.MaxChargePower", 500f);

            var modbus = new FakeModbusAdapter();
            var pipeline = new TelemetryPipeline(catalog, simulation, modbus, new ShadowStore());

            pipeline.RunOnce();

            Assert.Equal(2500.0, Convert.ToDouble(modbus.Registers["ycsummulti0"]), 6);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("a.X", null, null)]              // 单路径非法
    [InlineData("a.X", "", null)]
    [InlineData("a.X", "b.Y", new[] { "a.X", "b.Y" })]          // 经典两路径
    [InlineData("a.X;b.Y;c.Z", null, new[] { "a.X", "b.Y", "c.Z" })] // 分号多路径
    [InlineData("a.X; b.Y ;;", "c.Z", new[] { "a.X", "b.Y", "c.Z" })] // 空段剔除 + arg2 补充
    public void ParsePaths_Combinations(string? arg1, string? arg2, string[]? expected)
    {
        var paths = SumPointBinding.ParsePaths(arg1, arg2);
        if (expected == null)
            Assert.Null(paths);
        else
            Assert.Equal(expected, paths);
    }

    [Fact]
    public void TelemetryPipeline_RewritesOnlyWhenSumChanges()
    {
        var path = WriteTempPointMap();
        try
        {
            var pointMap = new ModbusPointMap(path, "simEmu1");
            var catalog = PointCatalogLoader.FromPointMap(pointMap, "simEmu1", new DataExchangeOptions());

            var simulation = new FakeSimulationAdapter();
            simulation.Set("emu1.PcsList[0].ActivePower", 100f);
            simulation.Set("emu1.PcsList[1].ActivePower", 50f);

            var modbus = new FakeModbusAdapter();
            var shadow = new ShadowStore();
            var pipeline = new TelemetryPipeline(catalog, simulation, modbus, shadow);

            pipeline.RunOnce();
            modbus.Registers.Clear();

            // 值未变化：shadow 抑制重复写入
            pipeline.RunOnce();
            Assert.False(modbus.Registers.ContainsKey("ycsum0"));

            // 任一操作数变化：写入新和值
            simulation.Set("emu1.PcsList[1].ActivePower", 80f);
            pipeline.RunOnce();
            Assert.Equal(180.0, Convert.ToDouble(modbus.Registers["ycsum0"]), 6);
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
    [InlineData(1, 2, 3)]
    public void ComputeSum_HandlesNullOrNonNumericOperands(object? a, object? b, double expected)
    {
        Assert.Equal(expected, SumPointBinding.ComputeSum(a, b), 9);
    }
}
