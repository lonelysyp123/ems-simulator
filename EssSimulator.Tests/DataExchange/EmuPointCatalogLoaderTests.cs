using EssSimulator.Configuration;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Config;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.DataExchange;

public class EmuPointCatalogLoaderTests
{
    /// <summary>固定加载 standard 型号 EMU 点表，避免受运行期设备型号选型（device-models.json）劫持。</summary>
    private static ModbusPointMap LoadStandardEmuMap() =>
        new(StandardEmuMapPath(), "simEmu1");

    private static string StandardEmuMapPath()
    {
        var path = Path.Combine(FindRepoRoot(), "pointmaps", "models", "emu", "standard", "emu.csv");
        Assert.True(File.Exists(path), path);
        return path;
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
    public void FromPointMap_simEmu1_HasTelemetryAndControlBindings()
    {
        var pointMap = LoadStandardEmuMap();
        var catalog = PointCatalogLoader.FromPointMap(pointMap, "simEmu1", new DataExchangeOptions());

        Assert.True(catalog.TelemetryPoints.Count > 20);
        Assert.True(catalog.ControlPoints.Count >= 4);

        var startStop = catalog.ControlPoints.First(p => p.ParamName == "yk3");
        Assert.Equal("emu1", startStop.Target.RootKey);
        Assert.Equal(ControlSemantics.Hold, startStop.Semantics);
        Assert.Equal(ControlEffectId.PcsApplyCommands, startStop.Effect);
        Assert.Equal("PcsList[0].pcsOnOffSwitch", startStop.Target.PropertyPath);

        Assert.DoesNotContain(catalog.ControlPoints, p => p.ParamName == "yx0");
        Assert.DoesNotContain(catalog.ControlPoints, p => p.Target.PropertyPath.Contains("Breaker"));

        var islandV = catalog.ControlPoints.First(p => p.ParamName == "yt3");
        Assert.Equal("PcsList[0].IslandVoltageSetting", islandV.Target.PropertyPath);
        Assert.Equal(ControlEffectId.PcsApplyCommands, islandV.Effect);

        var islandF = catalog.ControlPoints.First(p => p.ParamName == "yt4");
        Assert.Equal("PcsList[0].IslandFrequencySetting", islandF.Target.PropertyPath);
        Assert.Equal(ControlEffectId.PcsApplyCommands, islandF.Effect);
    }

    [Fact]
    public void FromPointMap_AppliesConfiguredSemanticsOverride()
    {
        var pointMap = LoadStandardEmuMap();
        var options = new DataExchangeOptions
        {
            ControlSemantics = { ["yk3"] = "Hold" }
        };

        var catalog = PointCatalogLoader.FromPointMap(pointMap, "simEmu1", options);
        var startStop = catalog.ControlPoints.First(p => p.ParamName == "yk3");
        Assert.Equal(ControlSemantics.Hold, startStop.Semantics);
    }

    [Fact]
    public void FromPointMap_GatesEmuBindingsByUnitComposition()
    {
        var pointMap = LoadStandardEmuMap();
        // 机组构成：1 台 PCS、无断路器、无电表
        var units = new List<EssUnitConfig>
        {
            new()
            {
                HasUnitBreaker = false,
                HasUnitMeter = false,
                Pcs = { new PcsDeviceConfig() }
            }
        };

        var gated = PointCatalogLoader.FromPointMap(pointMap, "simEmu1", new DataExchangeOptions(), units);
        var ungated = PointCatalogLoader.FromPointMap(pointMap, "simEmu1", new DataExchangeOptions());

        Assert.DoesNotContain(ungated.ControlPoints, p => p.ParamName == "yx0");
        Assert.DoesNotContain(ungated.ControlPoints, p => p.Target.PropertyPath.Contains("PcsList[1]"));
        Assert.Contains(ungated.ControlPoints, p => p.ParamName == "yk3");
        Assert.Contains(gated.ControlPoints, p => p.ParamName == "yk3");
        Assert.Equal(ungated.DefaultValues.Count, gated.DefaultValues.Count);

        var mapPcs1 = new ModbusPointMap(StandardEmuMapPath(), "simEmu1", emuDeviceIdOverride: 1, pcsIndex: 1);
        var gatedSecond = PointCatalogLoader.FromPointMap(mapPcs1, "simEmu1", new DataExchangeOptions(), units);
        var ungatedSecond = PointCatalogLoader.FromPointMap(mapPcs1, "simEmu1", new DataExchangeOptions());
        Assert.Contains(ungatedSecond.ControlPoints, p => p.Target.PropertyPath.Contains("PcsList[1]"));
        Assert.DoesNotContain(gatedSecond.ControlPoints, p => p.Target.PropertyPath.Contains("PcsList[1]"));
        Assert.DoesNotContain(gatedSecond.TelemetryPoints, p => p.Target.PropertyPath.Contains("PcsList[1]"));
        Assert.True(gatedSecond.TelemetryPoints.Count < ungatedSecond.TelemetryPoints.Count);
        Assert.Equal(ungatedSecond.DefaultValues.Count, gatedSecond.DefaultValues.Count);
    }
}
