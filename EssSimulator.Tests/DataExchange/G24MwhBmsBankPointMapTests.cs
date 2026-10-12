using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Config;
using EssSimulator.EssSimModelApi;
using EssSimulator.EssSimModelApi.BatteryManagementSystem;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.DataExchange;

/// <summary>
/// 验证 G2 4MWh 标准版 BMS bank 点表（Bank Information 工作表）：
/// 协议地址、功能码、ModelSim 绑定与一键并网/复位控制语义。
/// </summary>
public class G24MwhBmsBankPointMapTests
{
    private static string BankCsvPath =>
        Path.Combine(FindRepoRoot(), "pointmaps", "models", "bms", "g2_4mwh", "bms_bank.csv");

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

    private static PointCatalog LoadCatalog()
    {
        Assert.True(File.Exists(BankCsvPath), $"G2 4MWh 点表不存在: {BankCsvPath}");
        var pointMap = new ModbusPointMap(BankCsvPath, "simBms1", clusterCount: 12);
        return PointCatalogLoader.FromPointMap(pointMap, "simBms1", new DataExchangeOptions());
    }

    private static BatteryManagementSystemData CreateBmsData(int clusterCount = 12)
    {
        var bms = new BatteryManagementSystemData();
        var stack = new BatteryStack
        {
            SOC = 0.55f,
            TotalVoltage = 1330f,
            Current = 40f
        };
        for (int i = 0; i < clusterCount; i++)
            stack.Cluseter.Add(new BatteryCluster());
        bms.BatteryStacks.Add(stack);
        bms.TempHumiditySensors.Add(new TemperatureHumidityData { UnitId = 1, Temperature = 25f });
        return bms;
    }

    [Fact]
    public void Bank_UsesProtocolAddressesFromExcel()
    {
        Assert.True(File.Exists(BankCsvPath), $"G2 4MWh 点表不存在: {BankCsvPath}");
        var pointMap = new ModbusPointMap(BankCsvPath, "simBms1", clusterCount: 12);

        var gridWrite = pointMap.ControlMaps.Single(m => m.Address == 1);
        Assert.Equal(6, gridWrite.FunctionCode);
        Assert.Contains("一键并网", gridWrite.Description);

        var reset = pointMap.ControlMaps.Single(m => m.Address == 11);
        Assert.Equal(6, reset.FunctionCode);
        Assert.Contains("复位", reset.Description);

        var gridStatus = pointMap.DataMaps.Single(m => m.Address == 0x1001);
        Assert.Equal(3, gridStatus.FunctionCode);
        Assert.Contains("一键并网", gridStatus.Description);

        var voltage = pointMap.DataMaps.Single(m => m.Address == 0x100C);
        Assert.Equal(10, voltage.Scale);
        Assert.Contains("总电压", voltage.Description);

        var soc = pointMap.DataMaps.Single(m => m.Address == 0x100E);
        Assert.Equal(1000, soc.Scale);
        Assert.Contains("SOC", soc.Description);

        var chargeEnergy = pointMap.DataMaps.Single(m => m.Address == 0x1030);
        Assert.Equal("u32", chargeEnergy.Type);
        Assert.Equal(32, chargeEnergy.Size);
        Assert.Contains("累计充电", chargeEnergy.Description);

        var avgTemp = pointMap.DataMaps.Single(m => m.Address == 0x1028);
        Assert.Equal("int16", avgTemp.Type);
        Assert.Equal(10, avgTemp.Scale);
        Assert.Contains("平均温度", avgTemp.Description);

        var commFault = pointMap.DataMaps.Single(m => m.Address == 0x1029);
        Assert.Contains("主控通信故障", commFault.Description);
    }

    [Fact]
    public void Bank_TelemetryBindings_ResolveToStackProperties()
    {
        var catalog = LoadCatalog();
        var bms = CreateBmsData();

        Assert.True(catalog.TelemetryPoints.Count > 30,
            $"G2 4MWh 遥测点位过少: {catalog.TelemetryPoints.Count}");

        var failures = new List<string>();
        foreach (var point in catalog.TelemetryPoints)
        {
            try
            {
                var propPath = point.Target.PropertyPath;
                var lastSegment = propPath.Split('.').Last().Split('[')[0];
                var ownerType = propPath.Contains("TempHumiditySensors", StringComparison.Ordinal)
                    ? typeof(TemperatureHumidityData)
                    : typeof(BatteryStack);
                var prop = ownerType.GetProperty(lastSegment,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.IgnoreCase);
                if (prop == null)
                {
                    failures.Add($"{point.ParamName}: 属性 {lastSegment} 不存在于 {ownerType.Name}");
                    continue;
                }

                ObjectPathResolver.GetValue(bms, propPath);
            }
            catch (Exception ex)
            {
                failures.Add($"{point.ParamName}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            $"以下遥测绑定解析失败:\n{string.Join("\n", failures)}");
    }

    [Fact]
    public void Bank_ControlBindings_AreWritable()
    {
        var catalog = LoadCatalog();

        var failures = new List<string>();
        foreach (var point in catalog.ControlPoints)
        {
            var lastSegment = point.Target.PropertyPath.Split('.').Last().Split('[')[0];
            var prop = typeof(BatteryStack).GetProperty(lastSegment,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);
            if (prop == null)
            {
                failures.Add($"{point.ParamName}: 属性 {lastSegment} 不存在于 BatteryStack");
                continue;
            }

            if (!prop.CanWrite)
                failures.Add($"{point.ParamName}: 属性 {lastSegment} 只读，无法写入控制值");
        }

        Assert.True(failures.Count == 0,
            $"以下控制绑定无效:\n{string.Join("\n", failures)}");
    }

    [Fact]
    public void Bank_GridConnectAndReset_HavePulseSemantics()
    {
        var catalog = LoadCatalog();

        var grid = catalog.ControlPoints.Single(p =>
            p.Target.PropertyPath.Contains("GridConnectCommand", StringComparison.Ordinal));
        Assert.Equal(1, grid.Entry.Address);
        Assert.Equal(ControlSemantics.Pulse, grid.Semantics);
        Assert.Equal(ControlEffectId.BmsApplyLinkCommands, grid.Effect);

        var reset = catalog.ControlPoints.Single(p =>
            p.Target.PropertyPath.Contains("FaultClearCommand", StringComparison.Ordinal));
        Assert.Equal(11, reset.Entry.Address);
        Assert.Equal(ControlSemantics.Pulse, reset.Semantics);
        Assert.Equal(ControlEffectId.BmsApplyLinkCommands, reset.Effect);
    }

    [Fact]
    public void Bank_NoAddressDuplicatesWithinRegisterSpace()
    {
        Assert.True(File.Exists(BankCsvPath), $"G2 4MWh 点表不存在: {BankCsvPath}");
        var pointMap = new ModbusPointMap(BankCsvPath, "simBms1", clusterCount: 12);

        var holding = new HashSet<int>();
        var input = new HashSet<int>();
        foreach (var entry in pointMap.DataMaps.Concat(pointMap.ControlMaps))
        {
            int space = AddressOverlapValidator.RegisterSpaceOf(entry.FunctionCode);
            int len = Math.Max(1, entry.Size / 16);
            var set = space == 0 ? holding : input;
            for (int addr = entry.Address; addr < entry.Address + len; addr++)
            {
                Assert.True(set.Add(addr),
                    $"地址重复: {addr} ({entry.ParamName})");
            }
        }
    }
}
