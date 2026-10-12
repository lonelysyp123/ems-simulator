using EssSimulator.Configuration;
using EssSimulator.LocalControl;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.LocalControl;

public class ModbusPointMapComposedLcTests
{
    [Fact]
    public void FromComposedLc_G1_OmitsSecondGroupAddresses()
    {
        var map = ModbusPointMap.FromComposedLc("simLc1", groupCount: 1, modelsRoot: ModelsRoot());
        var names = map.DataMaps.Select(e => e.ParamName).ToHashSet();
        Assert.Contains("sysyc107", names);
        Assert.Contains("group_param4", names);
        Assert.DoesNotContain("group_param32", names);
        Assert.DoesNotContain(map.DataMaps, e => e.Address == 17500);
        Assert.Contains(map.ControlMaps, e => e.ParamName == "mv_param1" && e.Address == 33200);
    }

    [Fact]
    public void FromComposedLc_G2_LongerThanG1()
    {
        var models = ModelsRoot();
        var g1 = ModbusPointMap.FromComposedLc("simLc1", 1, modelsRoot: models);
        var g2 = ModbusPointMap.FromComposedLc("simLc2", 2, modelsRoot: models);
        Assert.True(g2.RawMaps[0].Length > g1.RawMaps[0].Length);
        Assert.Contains(g2.DataMaps, e => e.ParamName == "group_param32" && e.Address == 17500);
    }

    [Fact]
    public void ForLocalControl_NoExclusive_ComposesFragments()
    {
        var tmp = CreateSelectionRoot(lcModelId: null);
        try
        {
            var map = ModbusPointMap.ForLocalControl(
                "simLc1", 1, modelsRoot: ModelsRoot(), selectionRoot: tmp);
            Assert.Contains(map.DataMaps, e => e.ParamName == "sysyc107");
            Assert.DoesNotContain(map.ControlMaps, e => e.ParamName == "yx0");
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void ForLocalControl_ExclusiveEmu_LoadsOriginalTable()
    {
        var tmp = CreateSelectionRoot(lcModelId: "emu", copyRepoEmuCsv: true);
        try
        {
            var map = ModbusPointMap.ForLocalControl(
                "simLc1", 8, emuDeviceIdOverride: 1, selectionRoot: tmp);
            Assert.Contains(map.ControlMaps, e => e.ParamName == "yx0" && e.Address == 1000);
            Assert.Contains(map.ControlMaps, e => e.ParamName == "yx5" && e.Address == 1005);
            Assert.Contains(map.DataMaps, e => e.ParamName == "yc76");
            Assert.DoesNotContain(map.DataMaps, e => e.ParamName == "sysyc107");
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void ForLocalControl_ExclusiveEmu_FourPcsUnit_FallsBackToFragments()
    {
        var tmp = CreateSelectionRoot(lcModelId: "emu", copyRepoEmuCsv: true);
        try
        {
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
            var map = ModbusPointMap.ForLocalControl(
                "simLc1", 1, emuDeviceIdOverride: 1, selectionRoot: tmp, essUnits: units);
            Assert.Contains(map.DataMaps, e => e.ParamName == "sysyc107");
            Assert.DoesNotContain(map.ControlMaps, e => e.ParamName == "yx0");
            Assert.Contains(map.DataMaps, e => e.ParamName == "unit_param0" && e.Address == LcUnitMap.AddressBase);
            Assert.Contains(map.DataMaps, e => e.ParamName == "unit1_param0" && e.Address == LcUnitMap.FiveFiveMw.AddressBase);
            Assert.Contains(map.DataMaps, e => e.ParamName == "unit_param600" && e.Address == LcUnitMap.Address(2, 0));
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    private static string ModelsRoot() =>
        Path.Combine(FindRepoRoot(), "pointmaps", "models");

    private static string CreateSelectionRoot(string? lcModelId, bool copyRepoEmuCsv = false)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ess-lc-map-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "configs", "topology"));
        if (lcModelId != null)
        {
            var dir = Path.Combine(tmp, DeviceModelRegistry.ModelsRelativeDir, "lc", lcModelId);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "model.json"),
                """{"id":"emu","name":"EMU 直控点表","role":"exclusive"}""");
            var dest = Path.Combine(dir, "lc.csv");
            if (copyRepoEmuCsv)
                File.Copy(Path.Combine(FindRepoRoot(), "pointmaps", "models", "lc", "emu", "lc.csv"), dest);
            else
                File.WriteAllText(dest,
                    "FunctionCode,Address,Type,Size,ParamName,Scale,Description,ModelSim\n5,1000,bool,1,yx0,1,x,0\n");

            DeviceModelRegistry.SaveSelection(new DeviceModelSelection
            {
                Selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["lc"] = lcModelId
                }
            }, tmp);
        }

        return tmp;
    }

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
}
