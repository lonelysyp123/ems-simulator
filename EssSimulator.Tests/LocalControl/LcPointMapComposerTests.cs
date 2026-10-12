using EssSimulator.LocalControl;

namespace EssSimulator.Tests.LocalControl;

public class LcPointMapComposerTests : IDisposable
{
    private readonly string _root;

    public LcPointMapComposerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "lc-compose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "lc"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Compose_MergesFragments_AndExpandsGroups()
    {
        WriteFragment("standard", """
            FunctionCode,Address,Type,Size,ParamName,Scale,Description,ModelSim
            4,107,u16,16,param1,1,系统故障总,0
            4,17000 + 300 * (n - 1) + 200,u16,16,param{4 + 28*(n-1)},1,pcs1故障,0
            """);
        WriteFragment("extra", """
            FunctionCode,Address,Type,Size,ParamName,Scale,Description,ModelSim
            4,17000 + 300 * (n - 1) + 228,int16,16,param{102 + 8*(n-1)},1,pcs1有功,0
            """);

        var map = LcPointMapComposer.Compose(_root, groupCount: 2);
        var keys = map.Select(e => (e.Address, e.ParamName)).ToHashSet();
        Assert.Contains((107, "param1"), keys);
        Assert.Contains((17200, "param4"), keys);
        Assert.Contains((17500, "param32"), keys);
        Assert.Contains((17228, "param102"), keys);
        Assert.Contains((17528, "param110"), keys);
        Assert.Equal(1, map.Count(e => e.ParamName == "param1"));
    }

    [Fact]
    public void Compose_DuplicateAddress_ThrowsWithFragmentIds()
    {
        WriteFragment("a", """
            FunctionCode,Address,Type,Size,ParamName,Scale,Description,ModelSim
            4,107,u16,16,param1,1,故障总,0
            """);
        WriteFragment("b", """
            FunctionCode,Address,Type,Size,ParamName,Scale,Description,ModelSim
            4,107,u16,16,param9,1,重复地址,0
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => LcPointMapComposer.Compose(_root, 1));
        Assert.Contains("a", ex.Message);
        Assert.Contains("b", ex.Message);
        Assert.Contains("107", ex.Message);
    }

    [Fact]
    public void ListFragmentPaths_IgnoresMissingCsv()
    {
        Directory.CreateDirectory(Path.Combine(_root, "lc", "empty"));
        WriteFragment("standard", """
            FunctionCode,Address,Type,Size,ParamName,Scale,Description,ModelSim
            4,107,u16,16,param1,1,系统故障总,0
            """);

        var paths = LcPointMapComposer.ListFragmentPaths(_root);
        Assert.Single(paths);
        Assert.Contains("standard", paths[0]);
    }

    [Fact]
    public void ListFragmentPaths_SkipsExclusiveModels()
    {
        WriteFragment("standard", """
            FunctionCode,Address,Type,Size,ParamName,Scale,Description,ModelSim
            4,107,u16,16,param1,1,系统故障总,0
            """);
        WriteExclusive("emu", """
            FunctionCode,Address,Type,Size,ParamName,Scale,Description,ModelSim
            5,1000,bool,1,yx0,1,高压断路器开合,0
            """);

        var paths = LcPointMapComposer.ListFragmentPaths(_root);
        Assert.Single(paths);
        Assert.Contains("standard", paths[0]);

        var map = LcPointMapComposer.Compose(_root, groupCount: 1);
        Assert.DoesNotContain(map, e => e.ParamName == "yx0");
        Assert.Contains(map, e => e.ParamName == "param1");
    }

    [Fact]
    public void Compose_RepoFragments_IncludesSystemProtocolPoints()
    {
        var models = Path.Combine(FindRepoRoot(), "pointmaps", "models");
        var map = LcPointMapComposer.Compose(models, groupCount: 1);
        var names = map.Select(e => e.ParamName).ToHashSet();
        Assert.Contains("sysyc107", names);
        Assert.Contains("sysyc170", names);
        Assert.Contains("sysyc171", names);
        Assert.Contains("syst7", names);
        Assert.Contains("syst1010", names);
        Assert.Contains("mv_param1", names);
        Assert.Contains("mvyc25306", names);
        Assert.Contains("bmsyc38000", names);
        Assert.Contains("bmsyc38001", names);
        Assert.Contains("bmsyc38074", names);
        Assert.Contains("unit_param0", names);
        Assert.Contains("unit1_param0", names);
        Assert.Contains(map, e => e.ParamName == "unit_param0" && e.Address == LcUnitMap.AddressBase);
        Assert.Contains(map, e => e.ParamName == "unit_param600" && e.Address == LcUnitMap.Address(2, 0));
        Assert.Contains(map, e => e.ParamName == "unit1_param0" && e.Address == LcUnitMap.FiveFiveMw.AddressBase);
        Assert.DoesNotContain(map, e => e.Address == LcUnitMap.FiveFiveMw.Address(2, 0));
        Assert.DoesNotContain("param1", names);
        Assert.DoesNotContain("yx0", names);
        Assert.DoesNotContain("yt0", names);
        Assert.Equal(1, map.Count(e => e.ParamName == "sysyc107"));
        Assert.Contains(map, e => e.FunctionCode == 6 && e.Address == 33200 && e.ParamName == "mv_param1");
    }

    [Fact]
    public void Compose_UnitFragments_AlwaysCoexist_RegardlessOfPcsCount()
    {
        var models = Path.Combine(FindRepoRoot(), "pointmaps", "models");
        var four = LcPointMapComposer.Compose(models, groupCount: 1, maxPcsPerGroup: 4);
        var two = LcPointMapComposer.Compose(models, groupCount: 1, maxPcsPerGroup: 2);
        var five = LcPointMapComposer.Compose(models, groupCount: 1, maxPcsPerGroup: 5);
        Assert.Contains(four, e => e.ParamName == "unit_param0" && e.Address == LcUnitMap.AddressBase);
        Assert.Contains(four, e => e.ParamName == "unit1_param0" && e.Address == LcUnitMap.FiveFiveMw.AddressBase);
        Assert.Contains(two, e => e.ParamName == "unit_param0");
        Assert.Contains(two, e => e.ParamName == "unit1_param0");
        Assert.Contains(five, e => e.ParamName == "unit_param0");
        Assert.Contains(five, e => e.ParamName == "unit1_param0");
        Assert.Contains(four, e => e.ParamName == "sysyc107");
        Assert.Contains(four, e => e.ParamName == "sysyc228");
    }

    [Fact]
    public void Compose_GroupCountOverMax_ThrowsClearError()
    {
        var models = Path.Combine(FindRepoRoot(), "pointmaps", "models");
        var ex = Assert.Throws<InvalidOperationException>(
            () => LcPointMapComposer.Compose(models, groupCount: LcLayout.MaxGroupCount + 1));
        Assert.Contains(LcLayout.MaxGroupCount.ToString(), ex.Message);
        Assert.Contains("上限", ex.Message);
        Assert.DoesNotContain("冲突", ex.Message);
    }

    [Fact]
    public void Compose_DifferentGroupCounts_YieldDifferentLengths()
    {
        var models = Path.Combine(FindRepoRoot(), "pointmaps", "models");
        var g1 = LcPointMapComposer.Compose(models, groupCount: 1);
        var g2 = LcPointMapComposer.Compose(models, groupCount: 2);
        Assert.True(g2.Count > g1.Count);
        Assert.DoesNotContain(g1, e => e.Address == 17500);
        Assert.Contains(g2, e => e.Address == 17500);
        Assert.Contains(g1, e => e.Address == 17200);
        Assert.Contains(g1, e => e.Address == LcUnitMap.AddressBase && e.ParamName == "unit_param0");
        Assert.Contains(g1, e => e.Address == LcUnitMap.Address(2, 0) && e.ParamName == "unit_param600");
        Assert.Contains(g1, e => e.Address == LcUnitMap.FiveFiveMw.AddressBase && e.ParamName == "unit1_param0");
        Assert.DoesNotContain(g1, e => e.Address == LcUnitMap.FiveFiveMw.Address(2, 0));
        Assert.Contains(g2, e => e.Address == LcUnitMap.Address(2, 0) && e.ParamName == "unit_param600");
        Assert.DoesNotContain(g1, e => e.Address == 38200);
        Assert.Contains(g2, e => e.Address == 38200 && e.ParamName == "bmsyc38200");
        Assert.Contains(g1, e => e.Address == 38000 && e.ParamName == "bmsyc38000");
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

    private void WriteFragment(string id, string csv)
    {
        var dir = Path.Combine(_root, "lc", id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "lc.csv"), csv.Trim() + "\n");
    }

    private void WriteExclusive(string id, string csv)
    {
        var dir = Path.Combine(_root, "lc", id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "model.json"),
            $"{{\"id\":\"{id}\",\"name\":\"{id}\",\"role\":\"exclusive\"}}\n");
        File.WriteAllText(Path.Combine(dir, "lc.csv"), csv.Trim() + "\n");
    }
}
