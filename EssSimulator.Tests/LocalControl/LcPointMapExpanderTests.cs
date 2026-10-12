using EssSimulator.LocalControl;

namespace EssSimulator.Tests.LocalControl;

public class LcPointMapExpanderTests
{
    [Fact]
    public void Expand_G2_DoublesGroupTemplate()
    {
        var g1 = LcPointMapExpander.ExpandFile(GroupCsvPath(), 1);
        var g2 = LcPointMapExpander.ExpandFile(GroupCsvPath(), 2);
        Assert.Equal(g1.Count * 2, g2.Count);
        Assert.Contains(g2, e => e.ParamName == "group_param4" && e.Address == 17200);
        Assert.Contains(g2, e => e.ParamName == "group_param32" && e.Address == 17500);
        Assert.Contains(g2, e => e.ParamName == "group_param60" && e.Address == 27200);
        Assert.Contains(g2, e => e.ParamName == "group_param80" && e.Address == 27500);
    }

    [Fact]
    public void Expand_G1_OmitsSecondGroupAddresses()
    {
        var actual = Keys(LcPointMapExpander.ExpandFile(GroupCsvPath(), 1));
        Assert.Contains((4, 17200, "group_param4"), actual);
        Assert.Contains((16, 27200, "group_param60"), actual);
        Assert.DoesNotContain(actual, k => k.Address == 17500);
        Assert.DoesNotContain(actual, k => k.ParamName == "group_param32");
        Assert.DoesNotContain(actual, k => k.Address == 27500);
    }

    [Fact]
    public void Expand_SystemRows_AppearOnceRegardlessOfG()
    {
        var path = Path.Combine(FindRepoRoot(), "pointmaps", "models", "lc", "system", "lc.csv");
        var g1 = Keys(LcPointMapExpander.ExpandFile(path, 1));
        var g3 = Keys(LcPointMapExpander.ExpandFile(path, 3));
        Assert.Equal(g1, g3);
        Assert.Equal(1, g3.Count(k => k.ParamName == "sysyc107"));
        Assert.Contains((4, 107, "sysyc107"), g3);
        Assert.Contains((6, 7, "syst7"), g3);
    }

    private static string GroupCsvPath()
    {
        var path = Path.Combine(FindRepoRoot(), "pointmaps", "models", "lc", "group", "lc.csv");
        Assert.True(File.Exists(path), path);
        return path;
    }

    private static HashSet<(int Fc, int Address, string ParamName)> Keys(IReadOnlyList<MapEntry> entries) =>
        entries.Select(e => (e.FunctionCode, e.Address, e.ParamName ?? "")).ToHashSet();

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
