using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Config;
using EssSimulator.EssSimModelApi;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.DataExchange;

/// <summary>
/// 验证 standard 版 EMU 点表（pointmaps/models/emu/standard/emu.csv）的绑定准确性：
/// 1. 所有 ModelSim 绑定路径能在 EnergyManagementData（Emu + PcsList）上解析出非空值；
/// 2. 控制点（FC5/FC6）绑定属性可写；
/// 3. 频率与功率能力点位（yc23/yc36-39）已绑定真实模型属性。
/// </summary>
public class StandardEmuPointMapTests
{
    private static string StandardCsvPath =>
        Path.Combine(FindRepoRoot(), "pointmaps", "models", "emu", "standard", "emu.csv");

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

    private static PointCatalog LoadStandardCatalog()
    {
        Assert.True(File.Exists(StandardCsvPath), $"standard 点表不存在: {StandardCsvPath}");
        var pointMap = new ModbusPointMap(StandardCsvPath, "simEmu1");
        return PointCatalogLoader.FromPointMap(pointMap, "simEmu1", new DataExchangeOptions());
    }

    private static EnergyManagementData CreateEmuData(int pcsCount = 2)
    {
        var emu = new EnergyManagementData();
        for (int i = 0; i < pcsCount; i++)
        {
            emu.PcsList.Add(new PcsData
            {
                PcsId = i + 1,
                Frequency = 50f,
                ChargePowerLimit = 2500f,
                DischargePowerLimit = 2500f,
                PCSRatePower = 2750f
            });
        }

        emu.Emu.MaxChargePower = pcsCount * 2500f;
        emu.Emu.MaxDischargePower = pcsCount * 2500f;
        return emu;
    }

    [Fact]
    public void Standard_AllTelemetryBindings_ResolveToModelValues()
    {
        var catalog = LoadStandardCatalog();
        var emu = CreateEmuData();

        Assert.True(catalog.TelemetryPoints.Count > 20,
            $"standard 遥测绑定点位过少: {catalog.TelemetryPoints.Count}");

        var failures = new List<string>();
        foreach (var point in catalog.TelemetryPoints)
        {
            var value = ObjectPathResolver.GetValue(emu, point.Target.PropertyPath);
            if (value == null)
                failures.Add($"{point.ParamName}: 路径 {point.Target.PropertyPath} 解析为 null");
        }

        Assert.True(failures.Count == 0,
            $"以下遥测绑定解析失败:\n{string.Join("\n", failures)}");
    }

    [Fact]
    public void Standard_AllControlBindings_AreWritable()
    {
        var catalog = LoadStandardCatalog();
        var emu = CreateEmuData();

        Assert.True(catalog.ControlPoints.Count >= 4,
            $"standard 控制点位过少: {catalog.ControlPoints.Count}");

        var failures = new List<string>();
        foreach (var point in catalog.ControlPoints)
        {
            var value = ObjectPathResolver.GetValue(emu, point.Target.PropertyPath);
            if (value == null)
                failures.Add($"{point.ParamName}: 路径 {point.Target.PropertyPath} 解析为 null");
        }

        Assert.True(failures.Count == 0,
            $"以下控制绑定解析失败:\n{string.Join("\n", failures)}");
    }

    [Fact]
    public void Standard_PcsIndexPlaceholder_BindsSecondPcs()
    {
        var pointMap = new ModbusPointMap(StandardCsvPath, "simEmu1", emuDeviceIdOverride: 1, pcsIndex: 1);
        var catalog = PointCatalogLoader.FromPointMap(pointMap, "simEmu1", new DataExchangeOptions());
        var emu = CreateEmuData(pcsCount: 2);

        var freq = catalog.TelemetryPoints.First(p => p.ParamName == "yc23");
        Assert.Equal("emu1", freq.Target.RootKey);
        Assert.Equal("PcsList[1].Frequency", freq.Target.PropertyPath);
        Assert.NotNull(ObjectPathResolver.GetValue(emu, freq.Target.PropertyPath));
    }

    [Theory]
    [InlineData("yc23", "PcsList[0].Frequency")]
    [InlineData("yc36", "PcsList[0].ChargePowerLimit")]
    [InlineData("yc37", "PcsList[0].DischargePowerLimit")]
    [InlineData("yc38", "PcsList[0].PCSRatePower")]
    [InlineData("yc39", "PcsList[0].PCSRatePower")]
    [InlineData("yc45", "PcsList[0].AlarmSummary1")]
    [InlineData("yc46", "PcsList[0].CurrentFault")]
    public void Standard_FrequencyAndCapabilityPoints_BindModelProperties(string paramName, string expectedPath)
    {
        var catalog = LoadStandardCatalog();

        var point = catalog.TelemetryPoints.FirstOrDefault(p => p.ParamName == paramName);
        Assert.NotNull(point);
        Assert.Equal(expectedPath, point!.Target.PropertyPath);
    }

    [Fact]
    public void Standard_Yt4_BindsIslandFrequencySetting()
    {
        var catalog = LoadStandardCatalog();
        var yt4 = catalog.ControlPoints.FirstOrDefault(p => p.ParamName == "yt4");
        Assert.NotNull(yt4);
        Assert.Equal("PcsList[0].IslandFrequencySetting", yt4!.Target.PropertyPath);

        var rows = File.ReadAllLines(StandardCsvPath)
            .Skip(1)
            .Select(l => l.Split(','))
            .ToList();
        var row = rows.First(c => c[4] == "yt4");
        Assert.Equal("40004", row[1]);
        Assert.Equal("100", row[5]);
        Assert.Contains("IslandFrequencySetting", row[7]);
        Assert.NotEqual("0", row[7]);
    }

    [Fact]
    public void Standard_Yc45Yc46_AreBoundNotPlaceholderZero()
    {
        var rows = File.ReadAllLines(StandardCsvPath)
            .Skip(1)
            .Select(l => l.Split(','))
            .ToList();
        var yc45 = rows.First(c => c[4] == "yc45");
        var yc46 = rows.First(c => c[4] == "yc46");
        Assert.Contains("AlarmSummary1", yc45[7]);
        Assert.Contains("CurrentFault", yc46[7]);
        Assert.NotEqual("0", yc45[7]);
        Assert.NotEqual("0", yc46[7]);
    }

    [Fact]
    public void CurrentFault_FollowsOperationStatusAndDriveOrBmsFault()
    {
        Assert.Equal(0, new PcsData { OperationStatus = 5 }.CurrentFault);
        Assert.Equal(1, new PcsData { OperationStatus = 6 }.CurrentFault);
        Assert.Equal(1, new PcsData { DriveFault = true }.CurrentFault);
        Assert.Equal(1, new PcsData { BmsSystemFault = true }.CurrentFault);
    }
}
