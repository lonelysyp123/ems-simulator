using EssSimulator.LocalControl;

namespace EssSimulator.Tests.LocalControl;

public class BlackStartPointMapTests
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
        Path.Combine(FindRepoRoot(), "pointmaps", "models", "lc", "group", "lc.csv");

    [Fact]
    public void PointMap_ExistsWithUniqueParamNames()
    {
        Assert.True(File.Exists(CsvPath));
        var names = ReadParamNames();
        var duplicates = names
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(duplicates.Count == 0, "重复 ParamName: " + string.Join(", ", duplicates));

        var expanded = LcPointMapExpander.ExpandFile(CsvPath, 2);
        var expandedNames = expanded.Select(e => e.ParamName ?? "").ToList();
        Assert.Equal(expandedNames.Count, expandedNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void PointMap_AddsGroupPowerTelemetryFormulas()
    {
        var names = ReadParamNames();
        Assert.Contains(names, n => n.StartsWith("group_param{102", StringComparison.Ordinal));
        Assert.Contains(names, n => n.StartsWith("group_param{109", StringComparison.Ordinal));

        var expanded = LcPointMapExpander.ExpandFile(CsvPath, groupCount: 2)
            .Select(e => e.ParamName)
            .ToHashSet();
        Assert.Contains("group_param102", expanded);
        Assert.Contains("group_param109", expanded);
        Assert.Contains("group_param110", expanded);
        Assert.Contains("group_param117", expanded);
    }

    private static List<string> ReadParamNames()
    {
        var names = new List<string>();
        foreach (var line in File.ReadLines(CsvPath).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var cols = line.Split(',');
            if (cols.Length < 5)
                continue;
            names.Add(cols[4].Trim());
        }
        return names;
    }
}
