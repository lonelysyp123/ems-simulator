using EssSimulator.LocalControl;

namespace EssSimulator.Tests.LocalControl;

public class SystemPointMapTests
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

    private static string CsvPath =>
        Path.Combine(FindRepoRoot(), "pointmaps", "models", "lc", "system", "lc.csv");

    [Fact]
    public void PointMap_CoversProtocolSystemSheet()
    {
        Assert.True(File.Exists(CsvPath), CsvPath);
        var expanded = LcPointMapExpander.ExpandFile(CsvPath, groupCount: 2);
        var names = expanded.Select(e => e.ParamName ?? "").ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(expanded.Count, LcPointMapExpander.ExpandFile(CsvPath, 1).Count);

        Assert.Contains(expanded, e => e.FunctionCode == 4 && e.Address == 107 && e.ParamName == "sysyc107");
        Assert.Contains(expanded, e => e.FunctionCode == 4 && e.Address == 170 && e.ParamName == "sysyc170");
        Assert.Contains(expanded, e => e.FunctionCode == 4 && e.Address == 171 && e.ParamName == "sysyc171");
        Assert.Contains(expanded, e => e.FunctionCode == 6 && e.Address == 7 && e.ParamName == "syst7");
        Assert.Contains(expanded, e => e.FunctionCode == 6 && e.Address == 1010 && e.ParamName == "syst1010");
        Assert.DoesNotContain(expanded, e => e.Address == 33200);
        Assert.DoesNotContain(names, n => n.StartsWith("param", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PointMap_ModelSimIsUnbound()
    {
        var rows = LcPointMapExpander.LoadTemplate(CsvPath);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("0", r.ModelSim));
    }
}
