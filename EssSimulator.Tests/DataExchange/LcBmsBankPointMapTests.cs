using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Config;
using EssSimulator.EssSimModelApi;
using EssSimulator.EssSimModelApi.BatteryManagementSystem;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.DataExchange;

/// <summary>
/// 验证 G2 Pro 版 BMS bank 点表（pointmaps/models/bms/g2_pro/bms_bank.csv，原 LC 版点表）的准确性：
/// 1. 所有 ModelSim 绑定路径能通过反射解析到 BatteryStack 属性；
/// 2. 控制点（FC6）绑定属性可写；
/// 3. param170 一键复归绑定 FaultClearCommand 且具有 Pulse 语义与 BmsApplyLinkCommands 效果。
/// </summary>
public class LcBmsBankPointMapTests
{
    private static string LcBankCsvPath =>
        Path.Combine(FindRepoRoot(), "pointmaps", "models", "bms", "g2_pro", "bms_bank.csv");

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

    private static PointCatalog LoadLcCatalog()
    {
        Assert.True(File.Exists(LcBankCsvPath), $"LC 点表不存在: {LcBankCsvPath}");
        var pointMap = new ModbusPointMap(LcBankCsvPath, "simBms1", clusterCount: 12);
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
        return bms;
    }

    [Fact]
    public void LcBank_AllTelemetryBindings_ResolveToStackProperties()
    {
        var catalog = LoadLcCatalog();
        var bms = CreateBmsData();

        Assert.True(catalog.TelemetryPoints.Count > 40,
            $"LC 版遥测点位过少: {catalog.TelemetryPoints.Count}");

        var failures = new List<string>();
        foreach (var point in catalog.TelemetryPoints)
        {
            try
            {
                // 属性不存在时反射返回 null（路径首段解析失败），通过 PropertyInfo 检测更精确
                var propPath = point.Target.PropertyPath;
                var lastSegment = propPath.Split('.').Last().Split('[')[0];
                var prop = typeof(BatteryStack).GetProperty(lastSegment,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.IgnoreCase);
                if (prop == null)
                {
                    failures.Add($"{point.ParamName}: 属性 {lastSegment} 不存在于 BatteryStack");
                    continue;
                }

                // 验证完整路径可解析（不抛异常）
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
    public void LcBank_AllControlBindings_AreWritableProperties()
    {
        var catalog = LoadLcCatalog();
        var bms = CreateBmsData();

        var failures = new List<string>();
        foreach (var point in catalog.ControlPoints)
        {
            var propPath = point.Target.PropertyPath;
            var lastSegment = propPath.Split('.').Last().Split('[')[0];
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
    public void LcBank_Param170_BindsFaultClearCommandWithPulseSemantics()
    {
        var catalog = LoadLcCatalog();

        var param170 = catalog.ControlPoints.FirstOrDefault(p => p.ParamName == "param170");
        Assert.NotNull(param170);
        Assert.Equal(12356, param170!.Entry.Address);
        Assert.Equal("BatteryStacks[0].FaultClearCommand", param170.Target.PropertyPath);
        Assert.Equal(ControlSemantics.Pulse, param170.Semantics);
        Assert.Equal(ControlEffectId.BmsApplyLinkCommands, param170.Effect);
    }

    [Fact]
    public void LcBank_Param131_BindsGridConnectCommandWithPulseSemantics()
    {
        var catalog = LoadLcCatalog();

        var param131 = catalog.ControlPoints.FirstOrDefault(p => p.ParamName == "param131");
        Assert.NotNull(param131);
        Assert.Equal(12289, param131!.Entry.Address);
        Assert.Equal("BatteryStacks[0].GridConnectCommand", param131.Target.PropertyPath);
        Assert.Equal(ControlSemantics.Pulse, param131.Semantics);
        Assert.Equal(ControlEffectId.BmsApplyLinkCommands, param131.Effect);
    }

    [Fact]
    public void LcBank_Param171_BindsBlackStartCommandWithLinkEffect()
    {
        var catalog = LoadLcCatalog();

        var param171 = catalog.ControlPoints.FirstOrDefault(p => p.ParamName == "param171");
        Assert.NotNull(param171);
        Assert.Equal(12357, param171!.Entry.Address);
        Assert.Equal("BatteryStacks[0].BlackStartCommand", param171.Target.PropertyPath);
        Assert.Equal(ControlSemantics.Hold, param171.Semantics);
        Assert.Equal(ControlEffectId.BmsApplyLinkCommands, param171.Effect);
    }

    [Fact]
    public void LcBank_AlarmSummaries_ResolveAndAggregate()
    {
        var catalog = LoadLcCatalog();
        var bms = CreateBmsData();

        // 触发簇级故障，验证汇总属性聚合
        bms.BatteryStacks[0].Cluseter[0].Alarms.CellOverVoltageFault = true;
        bms.BatteryStacks[0].Cluseter[3].Alarms.BatteryBoxOvervoltageFault = true;

        var alarmParams = new[]
        {
            "param19", "param20", "param21", "param22",
            "param23", "param24", "param25", "param26",
            "param27", "param28", "param29", "param30"
        };

        foreach (var paramName in alarmParams)
        {
            var point = catalog.TelemetryPoints.FirstOrDefault(p => p.ParamName == paramName);
            Assert.NotNull(point);
            var value = ObjectPathResolver.GetValue(bms, point!.Target.PropertyPath);
            Assert.NotNull(value);
            Assert.IsType<ushort>(value);
        }

        // BMSFaultSummary (param19) 应包含簇0的单体过压故障位
        var faultSummary = ObjectPathResolver.GetValue(bms, "BatteryStacks[0].BMSFaultSummary");
        Assert.NotEqual((ushort)0, faultSummary);

        // BMSFaultSummary2 (param20) 应包含簇3的模组过压故障位 (bit1)
        var faultSummary2 = (ushort)ObjectPathResolver.GetValue(bms, "BatteryStacks[0].BMSFaultSummary2")!;
        Assert.True((faultSummary2 & 0x02) != 0, "BMSFaultSummary2 应置位 bit1(模组过压)");
    }

    [Fact]
    public void G2ProRack_HasNoThresholdControlPoints_ProtocolSurfaceOnly()
    {
        var pointMap = new ModbusPointMap(LcBankCsvPath, "simBms1", clusterCount: 12);
        var catalog = PointCatalogLoader.FromPointMap(pointMap, "simBms1");

        Assert.DoesNotContain(
            pointMap.RackControlMaps,
            m => (m.ModelSim ?? "").Contains("Thresholds.", StringComparison.OrdinalIgnoreCase));
        Assert.Null(catalog.FindRackControl("yt0"));
    }

    [Fact]
    public void LcBank_NoAddressDuplicates()
    {
        var pointMap = new ModbusPointMap(LcBankCsvPath, "simBms1", clusterCount: 12);

        var addresses = new HashSet<int>();
        foreach (var entry in pointMap.DataMaps)
        {
            Assert.True(addresses.Add(entry.Address),
                $"遥测地址重复: {entry.Address} ({entry.ParamName})");
        }

        foreach (var entry in pointMap.ControlMaps)
        {
            Assert.True(addresses.Add(entry.Address),
                $"控制地址重复: {entry.Address} ({entry.ParamName})");
        }
    }
}
