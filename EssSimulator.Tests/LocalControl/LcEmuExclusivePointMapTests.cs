using EssSimulator.Configuration;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Config;
using EssSimulator.EssSimModelApi;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;
using EssSimulator.LocalControl;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.LocalControl;

/// <summary>
/// 原 EMU 点表迁入 LC 互斥型号后，绑定与 DataExchange 目录应与当年 simEmu 行为一致。
/// </summary>
public class LcEmuExclusivePointMapTests
{
    private static string ExclusiveCsvPath =>
        Path.Combine(FindRepoRoot(), "pointmaps", "models", "lc", "emu", "lc.csv");

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

    private static ModbusPointMap LoadExclusiveMap()
    {
        Assert.True(File.Exists(ExclusiveCsvPath), ExclusiveCsvPath);
        return new ModbusPointMap(ExclusiveCsvPath, "simLc1", emuDeviceIdOverride: 1, lcGroupCount: 1);
    }

    private static PointCatalog LoadCatalog(IReadOnlyList<EssUnitConfig>? units = null) =>
        PointCatalogLoader.FromPointMap(LoadExclusiveMap(), "simLc1", new DataExchangeOptions(), units);

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
    public void Catalog_BindsBreakerAndTwoPcsLikeOriginalEmu()
    {
        var catalog = LoadCatalog();

        var breaker = catalog.ControlPoints.First(p => p.ParamName == "yx0");
        Assert.Equal("emu1", breaker.Target.RootKey);
        Assert.Equal("Breaker.Closed", breaker.Target.PropertyPath);
        Assert.Equal(ControlEffectId.UnitHighVoltageBreaker, breaker.Effect);

        var pcs1 = catalog.ControlPoints.First(p => p.ParamName == "yx3");
        Assert.Equal("PcsList[0].pcsOnOffSwitch", pcs1.Target.PropertyPath);
        Assert.Equal(ControlEffectId.PcsApplyCommands, pcs1.Effect);

        var pcs2 = catalog.ControlPoints.First(p => p.ParamName == "yx5");
        Assert.Equal("PcsList[1].pcsOnOffSwitch", pcs2.Target.PropertyPath);

        var p1 = catalog.TelemetryPoints.First(p => p.ParamName == "yc20");
        Assert.Equal("PcsList[0].LineVoltageAB", p1.Target.PropertyPath);
        var p2 = catalog.TelemetryPoints.First(p => p.ParamName == "yc47");
        Assert.Equal("PcsList[1].LineVoltageAB", p2.Target.PropertyPath);

        var totalP = catalog.TelemetryPoints.First(p => p.ParamName == "yc76");
        Assert.Equal("Emu.OutputActivePower", totalP.Target.PropertyPath);

        var yt0 = catalog.ControlPoints.First(p => p.ParamName == "yt0");
        Assert.Equal("PcsList[0].PCSActivePowerSetting", yt0.Target.PropertyPath);
        var yt4 = catalog.ControlPoints.First(p => p.ParamName == "yt4");
        Assert.Equal("PcsList[1].PCSActivePowerSetting", yt4.Target.PropertyPath);

        var alarm1 = catalog.TelemetryPoints.FirstOrDefault(p => p.ParamName == "yc45");
        Assert.NotNull(alarm1);
        Assert.Equal("PcsList[0].AlarmSummary1", alarm1!.Target.PropertyPath);
        var fault1 = catalog.TelemetryPoints.FirstOrDefault(p => p.ParamName == "yc46");
        Assert.NotNull(fault1);
        Assert.Equal("PcsList[0].CurrentFault", fault1!.Target.PropertyPath);
        var alarm2 = catalog.TelemetryPoints.FirstOrDefault(p => p.ParamName == "yc72");
        Assert.NotNull(alarm2);
        Assert.Equal("PcsList[1].AlarmSummary1", alarm2!.Target.PropertyPath);
        var fault2 = catalog.TelemetryPoints.FirstOrDefault(p => p.ParamName == "yc73");
        Assert.NotNull(fault2);
        Assert.Equal("PcsList[1].CurrentFault", fault2!.Target.PropertyPath);
    }

    [Fact]
    public void Catalog_BindingsResolveOnEnergyManagementData()
    {
        var catalog = LoadCatalog();
        var emu = CreateEmuData();

        var failures = new List<string>();
        foreach (var point in catalog.TelemetryPoints.Concat(catalog.ControlPoints))
        {
            var value = ObjectPathResolver.GetValue(emu, point.Target.PropertyPath);
            if (value == null)
                failures.Add($"{point.ParamName}: {point.Target.PropertyPath}");
        }

        Assert.True(failures.Count == 0, "绑定解析失败:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void Catalog_GatesBreakerWhenUnitHasNoBreaker()
    {
        var units = new List<EssUnitConfig>
        {
            new()
            {
                HasUnitBreaker = false,
                Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig() }
            }
        };

        var gated = LoadCatalog(units);
        Assert.DoesNotContain(gated.ControlPoints, p => p.ParamName == "yx0");
        Assert.Contains(gated.ControlPoints, p => p.ParamName == "yx3");
        Assert.Contains(gated.ControlPoints, p => p.ParamName == "yx5");
    }

    [Fact]
    public void Server_ExclusiveSelection_UsesDataExchange()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ess-lc-emu-rt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dir = Path.Combine(tmp, DeviceModelRegistry.ModelsRelativeDir, "lc", "emu");
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(tmp, "configs", "topology"));
            File.WriteAllText(Path.Combine(dir, "model.json"),
                """{"id":"emu","name":"EMU 直控点表","role":"exclusive"}""");
            File.Copy(ExclusiveCsvPath, Path.Combine(dir, "lc.csv"));
            DeviceModelRegistry.SaveSelection(new DeviceModelSelection
            {
                Selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["lc"] = "emu"
                }
            }, tmp);

            var server = new LocalControlModbusServer(
                lcGroupCount: 2, modbusPort: 0, serverName: "simLc1",
                firstEmuId: 1, selectionRoot: tmp);

            Assert.True(server.UsesDataExchange);
            Assert.Contains(server.PointMap.ControlMaps, e => e.ParamName == "yx0");
            Assert.DoesNotContain(server.PointMap.DataMaps, e => e.ParamName == "sysyc171");
        }
        finally
        {
            if (Directory.Exists(tmp))
                Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void ForLocalControl_ExclusiveEmu_FourPcs_DoesNotUseDataExchange()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ess-lc-excl4-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(tmp, "configs", "topology"));
            var dir = Path.Combine(tmp, DeviceModelRegistry.ModelsRelativeDir, "lc", "emu");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "model.json"),
                """{"id":"emu","name":"EMU 直控点表","role":"exclusive"}""");
            File.Copy(ExclusiveCsvPath, Path.Combine(dir, "lc.csv"));
            DeviceModelRegistry.SaveSelection(new DeviceModelSelection
            {
                Selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["lc"] = "emu"
                }
            }, tmp);

            var units = new List<EssUnitConfig>
            {
                new()
                {
                    Pcs =
                    {
                        new PcsDeviceConfig(), new PcsDeviceConfig(),
                        new PcsDeviceConfig(), new PcsDeviceConfig()
                    }
                }
            };
            var server = new LocalControlModbusServer(
                lcGroupCount: 1, modbusPort: 0, serverName: "simLc1",
                firstEmuId: 1, essUnits: units, selectionRoot: tmp);

            Assert.False(server.UsesDataExchange);
            Assert.Contains(server.PointMap.DataMaps, e => e.ParamName == "sysyc107");
            Assert.DoesNotContain(server.PointMap.ControlMaps, e => e.ParamName == "yx0");
        }
        finally
        {
            if (Directory.Exists(tmp))
                Directory.Delete(tmp, recursive: true);
        }
    }
}
