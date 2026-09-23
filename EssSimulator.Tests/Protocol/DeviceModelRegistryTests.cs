using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.Protocol;

public class DeviceModelRegistryTests
{
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

    [Fact]
    public void ListTypes_ScansKnownTypesAndModels()
    {
        var types = DeviceModelRegistry.ListTypes(FindRepoRoot());
        var byId = types.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

        Assert.True(byId.ContainsKey("bms"));
        Assert.True(byId.ContainsKey("emu"));
        Assert.True(byId.ContainsKey("em"));
        Assert.True(byId.ContainsKey("lc"));
        Assert.True(byId.ContainsKey("pv"));

        var bms = byId["bms"];
        Assert.Contains("bms_bank.csv", bms.Files);
        Assert.Contains("bms_rack.csv", bms.Files);

        var pv = byId["pv"];
        Assert.Contains("pv_logger.csv", pv.Files);
        Assert.Contains("pv_apm810.csv", pv.Files);
        Assert.Contains("pv_inverter.csv", pv.Files);

        var modelIds = bms.Models.Select(m => m.Id).ToList();
        Assert.Contains("standard", modelIds);
        Assert.Contains("g2_pro", modelIds);
        Assert.Contains("g2_4mwh", modelIds);
        Assert.All(bms.Models, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Name));
            Assert.True(Directory.Exists(m.Directory));
        });
    }

    [Fact]
    public void ListTypes_LcIncludesTrinaMvModels_EmuOnlyStandard()
    {
        var types = DeviceModelRegistry.ListTypes(FindRepoRoot());
        var byId = types.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

        var emuIds = byId["emu"].Models.Select(m => m.Id).ToList();
        Assert.Contains("standard", emuIds);
        Assert.Contains("iec61850", emuIds);
        Assert.False(DeviceModelRegistry.IsExclusiveModel(byId["emu"].Models.First(m => m.Id == "iec61850")));

        var lcIds = byId["lc"].Models.Select(m => m.Id).ToList();
        Assert.Contains("system", lcIds);
        Assert.Contains("group", lcIds);
        Assert.Contains("unit_10MW", lcIds);
        Assert.Contains("unit_5.5MW", lcIds);
        Assert.Contains("mv", lcIds);
        Assert.Contains("emu", lcIds);
        Assert.DoesNotContain("trina_10MW", lcIds);
        Assert.DoesNotContain("unit", lcIds);
        var unit10 = byId["lc"].Models.First(m => m.Id == "unit_10MW");
        var unit55 = byId["lc"].Models.First(m => m.Id == "unit_5.5MW");
        Assert.Equal(2, unit10.PairCount);
        Assert.Equal(1, unit55.PairCount);
        Assert.Equal(0, unit10.MaxPcsPerGroup);
        Assert.Equal(0, unit55.MaxPcsPerGroup);
        var emuLc = byId["lc"].Models.First(m => m.Id == "emu");
        Assert.True(DeviceModelRegistry.IsExclusiveModel(emuLc));
        Assert.All(byId["lc"].Models, m =>
        {
            Assert.True(File.Exists(Path.Combine(m.Directory, "lc.csv")));
        });
    }

    [Fact]
    public void FindTypeForFile_MapsRuntimeFileNamesToTypes()
    {
        var root = FindRepoRoot();
        Assert.Equal("bms", DeviceModelRegistry.FindTypeForFile("bms_bank.csv", root));
        Assert.Equal("bms", DeviceModelRegistry.FindTypeForFile("bms_rack.csv", root));
        Assert.Equal("emu", DeviceModelRegistry.FindTypeForFile("emu.csv", root));
        Assert.Equal("em", DeviceModelRegistry.FindTypeForFile("em.csv", root));
        Assert.Equal("lc", DeviceModelRegistry.FindTypeForFile("lc.csv", root));
        Assert.Equal("pv", DeviceModelRegistry.FindTypeForFile("pv_logger.csv", root));
        Assert.Equal("pv", DeviceModelRegistry.FindTypeForFile("pv_apm810.csv", root));
        Assert.Equal("pv", DeviceModelRegistry.FindTypeForFile("pv_inverter.csv", root));
    }

    [Fact]
    public void ValidateSelection_ReportsUnknownTypeAndModel()
    {
        var root = FindRepoRoot();

        var ok = DeviceModelRegistry.ValidateSelection(
            new Dictionary<string, string> { ["bms"] = "g2_pro", ["emu"] = "standard", ["lc"] = "emu" }, root);
        Assert.Empty(ok);

        var bad = DeviceModelRegistry.ValidateSelection(
            new Dictionary<string, string> { ["bms"] = "no-such-model", ["xxx"] = "standard" }, root);
        Assert.Equal(2, bad.Count);
    }

    [Fact]
    public void ValidateSelection_RejectsLcFragmentAndUnknownModel()
    {
        var root = FindRepoRoot();
        var fragment = DeviceModelRegistry.ValidateSelection(
            new Dictionary<string, string> { ["lc"] = "group" }, root);
        Assert.Contains(fragment, e => e.Contains("拼装片段"));

        var unknown = DeviceModelRegistry.ValidateSelection(
            new Dictionary<string, string> { ["lc"] = "trina_10MW" }, root);
        Assert.Contains(unknown, e => e.Contains("不存在型号"));
    }

    [Fact]
    public void GetSelectedModelDir_LcFragmentSelection_IsIgnored()
    {
        var tmp = CreateTempLcSelectionRoot(fragmentId: "group", exclusiveId: "emu");
        try
        {
            DeviceModelRegistry.SaveSelection(new DeviceModelSelection
            {
                Selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["lc"] = "group"
                }
            }, tmp);

            Assert.Null(DeviceModelRegistry.GetSelectedModelDir("lc", "lc.csv", tmp));
            Assert.Null(DeviceModelRegistry.GetSelectedExclusiveLcDir(tmp));
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void GetSelectedModelDir_ExclusiveLc_ReturnsDir()
    {
        var tmp = CreateTempLcSelectionRoot(fragmentId: "group", exclusiveId: "emu");
        try
        {
            DeviceModelRegistry.SaveSelection(new DeviceModelSelection
            {
                Selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["lc"] = "emu"
                }
            }, tmp);

            var dir = DeviceModelRegistry.GetSelectedModelDir("lc", "lc.csv", tmp);
            Assert.NotNull(dir);
            Assert.Equal("emu", Path.GetFileName(dir));
            Assert.True(DeviceModelRegistry.TryGetExclusiveLcCsv(out var csv, tmp));
            Assert.True(File.Exists(csv));
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void GetSelectedModelDir_LcUnselected_IsNull()
    {
        var root = FindRepoRoot();
        Assert.Null(DeviceModelRegistry.GetSelectedModelDir("lc", "lc.csv", root));
    }

    [Fact]
    public void Selection_SaveLoadRoundtrip_WithExplicitRoot()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ess-devmodel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "configs", "topology"));
        try
        {
            var saved = DeviceModelRegistry.SaveSelection(new DeviceModelSelection
            {
                Selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["bms"] = "g2_pro"
                }
            }, tmp);

            Assert.True(File.Exists(Path.Combine(tmp, DeviceModelRegistry.SelectionRelativePath)));

            var loaded = DeviceModelRegistry.LoadSelection(tmp);
            Assert.Equal("g2_pro", loaded.Selections["bms"]);
            Assert.Equal(saved.UpdatedAtUtc, loaded.UpdatedAtUtc);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LoadSelection_ReturnsEmpty_WhenFileMissing()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ess-devmodel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var loaded = DeviceModelRegistry.LoadSelection(tmp);
            Assert.Empty(loaded.Selections);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    private static string CreateTempLcSelectionRoot(string fragmentId, string exclusiveId)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ess-devmodel-lc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "configs", "topology"));
        var fragmentDir = Path.Combine(tmp, DeviceModelRegistry.ModelsRelativeDir, "lc", fragmentId);
        var exclusiveDir = Path.Combine(tmp, DeviceModelRegistry.ModelsRelativeDir, "lc", exclusiveId);
        Directory.CreateDirectory(fragmentDir);
        Directory.CreateDirectory(exclusiveDir);
        File.WriteAllText(Path.Combine(fragmentDir, "model.json"),
            """{"id":"group","name":"fragment","role":"fragment"}""");
        File.WriteAllText(Path.Combine(fragmentDir, "lc.csv"),
            "FunctionCode,Address,Type,Size,ParamName,Scale,Description,ModelSim\n4,107,u16,16,param1,1,x,0\n");
        File.WriteAllText(Path.Combine(exclusiveDir, "model.json"),
            """{"id":"emu","name":"EMU 直控点表","role":"exclusive"}""");
        File.WriteAllText(Path.Combine(exclusiveDir, "lc.csv"),
            "FunctionCode,Address,Type,Size,ParamName,Scale,Description,ModelSim\n5,1000,bool,1,yx0,1,高压断路器,0\n");
        File.WriteAllText(Path.Combine(tmp, DeviceModelRegistry.ModelsRelativeDir, "lc", "type.json"),
            """{"id":"lc","name":"LocalControl","files":["lc.csv"]}""");
        return tmp;
    }
}
