using EssSimulator;
using EssSimulator.Core;
using EssSimulator.DataExchange.Adapters;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Config;
using EssSimulator.DataExchange.Pipeline;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssSimModelApi;
using EssSimulator.EssSimModelApi.BatteryManagementSystem;
using EssSimulator.EssSimModelApi.Mappers;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.DataExchange;

/// <summary>
/// 验证 BMS 点表 ModelSim 路径能否解析到 DTO 属性，以及遥测管道能否读到映射后的值。
/// </summary>
public class BmsTelemetryBindingTests : SimulatorHostTestBase
{
    /// <summary>
    /// common 版点表绝对路径。根目录 bms_bank.csv 可能随交付版本切换（如 LC 版），
    /// 本测试固定验证 pointmaps/models/bms/standard 版本（原 pointmaps/common），不能依赖复制到 bin 的运行时点表。
    /// </summary>
    private static string CommonBankCsvPath
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "EssSimulator.sln")))
                    return Path.Combine(dir.FullName, "pointmaps", "models", "bms", "standard", "bms_bank.csv");
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("找不到仓库根目录");
        }
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
    public void FromPointMap_yc11_BindsToBatteryStackSoc()
    {
        var pointMap = new ModbusPointMap(CommonBankCsvPath, "simBms1", clusterCount: 12);
        var catalog = PointCatalogLoader.FromPointMap(pointMap, "simBms1", new DataExchangeOptions());

        var yc11 = catalog.TelemetryPoints.First(p => p.ParamName == "yc11");
        Assert.Equal(10011, yc11.Entry.Address);
        Assert.Equal(1000, yc11.Entry.Scale);
        Assert.Equal("bms1", yc11.Target.RootKey);
        Assert.Equal("BatteryStacks[0].SOC", yc11.Target.PropertyPath);
        Assert.Equal("bms1.BatteryStacks[0].SOC", yc11.Target.FullPath);
    }

    [Fact]
    public void ObjectPathResolver_ReadsBatteryStackSoc()
    {
        var bms = new BatteryManagementSystemData();
        bms.BatteryStacks.Add(new BatteryStack { SOC = 0.55f });

        var value = ObjectPathResolver.GetValue(bms, "BatteryStacks[0].SOC");
        Assert.Equal(0.55f, value);
    }

    [Fact]
    public void BmsMapper_MapsRackMinClusterSocToStackSoc()
    {
        var bms = new BatteryManagementSystemData();
        bms.BatteryStacks.Add(new BatteryStack());

        var rack = new RackState { MinClusterSOC = 0.62, StateOfHealth = 0.98, IsPcsLinked = true };
        BmsMapper.MapRackToStack(rack, bms);

        Assert.Equal(0.62f, bms.BatteryStacks[0].SOC);
    }

    [Fact]
    public void BmsMapper_ZerosDcVoltageWhenUnlinked()
    {
        var bms = new BatteryManagementSystemData();
        bms.BatteryStacks.Add(new BatteryStack());

        var rack = new RackState
        {
            TotalVoltage = 1330,
            TotalCurrent = 40,
            MinClusterSOC = 0.5,
            StateOfHealth = 1.0,
            IsPcsLinked = false
        };
        BmsMapper.MapRackToStack(rack, bms);

        Assert.Equal(0f, bms.BatteryStacks[0].TotalVoltage);
        Assert.Equal(0f, bms.BatteryStacks[0].Current);
        Assert.Equal(0f, bms.BatteryStacks[0].Power);
        Assert.Equal(0.5f, bms.BatteryStacks[0].SOC);
    }

    [Fact]
    public void BmsMapper_ReportsDcVoltageWhenLinked()
    {
        var bms = new BatteryManagementSystemData();
        bms.BatteryStacks.Add(new BatteryStack());

        var rack = new RackState
        {
            TotalVoltage = 1330,
            TotalCurrent = 40,
            MinClusterSOC = 0.5,
            StateOfHealth = 1.0,
            IsPcsLinked = true
        };
        BmsMapper.MapRackToStack(rack, bms);

        Assert.Equal(1330f, bms.BatteryStacks[0].TotalVoltage);
        Assert.Equal(40f, bms.BatteryStacks[0].Current);
        Assert.Equal(1330f * 40f / 1000f, bms.BatteryStacks[0].Power);
    }

    [Fact]
    public void SimServer_ReadsRegisteredBmsSoc()
    {
        var store = SimulatorHost.Instance;
        var bms = new BatteryManagementSystemData();
        bms.BatteryStacks.Add(new BatteryStack { SOC = 0.48f });
        store.Register("bms1", bms);

        var soc = SimServer.GetExtIfVariableVal("bms1.BatteryStacks[0].SOC");
        Assert.Equal(0.48f, soc);
    }

    [Fact]
    public void TelemetryPipeline_RewritesAfterShadowCleared()
    {
        var pointMap = new ModbusPointMap(CommonBankCsvPath, "simBms1", clusterCount: 12);
        var catalog = PointCatalogLoader.FromPointMap(pointMap, "simBms1", new DataExchangeOptions());

        var simulation = new FakeSimulationAdapter();
        simulation.Set("bms1.BatteryStacks[0].SOC", 0.55f);

        var modbus = new FakeModbusAdapter();
        var shadow = new ShadowStore();
        var pipeline = new TelemetryPipeline(catalog, simulation, modbus, shadow);

        pipeline.RunOnce();
        modbus.Registers.Clear();

        pipeline.RunOnce();
        Assert.False(modbus.Registers.ContainsKey("yc11"));

        shadow.ClearTelemetry();
        pipeline.RunOnce();
        Assert.True(modbus.Registers.ContainsKey("yc11"));
        Assert.Equal(0.55f, Convert.ToSingle(modbus.Registers["yc11"]));
    }

    [Fact]
    public void TelemetryPipeline_WritesYc11WhenSocChanges()
    {
        var pointMap = new ModbusPointMap(CommonBankCsvPath, "simBms1", clusterCount: 12);
        var catalog = PointCatalogLoader.FromPointMap(pointMap, "simBms1", new DataExchangeOptions());

        var simulation = new FakeSimulationAdapter();
        simulation.Set("bms1.BatteryStacks[0].SOC", 0.55f);

        var modbus = new FakeModbusAdapter();
        var shadow = new ShadowStore();
        var pipeline = new TelemetryPipeline(catalog, simulation, modbus, shadow);

        pipeline.RunOnce();

        Assert.True(modbus.Registers.ContainsKey("yc11"));
        Assert.Equal(0.55f, Convert.ToSingle(modbus.Registers["yc11"]));
    }

    [Fact]
    public void ObjectPathResolver_ReadsComputedChargeDischargeStatus()
    {
        var stack = new BatteryStack { Current = -120f };
        var bms = new BatteryManagementSystemData();
        bms.BatteryStacks.Add(stack);

        var status = ObjectPathResolver.GetValue(bms, "BatteryStacks[0].ChargeDischargeStatus");
        Assert.Equal(2, status); // 负电流 → 充电
    }
}
