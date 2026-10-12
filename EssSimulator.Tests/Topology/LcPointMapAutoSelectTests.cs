using EssSimulator.Protocol.Modbus;
using EssSimulator.Web.Topology;

namespace EssSimulator.Tests.Topology;

public class LcPointMapAutoSelectTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void ResolveModelId_NeverSelectsLcByPcsCount(int pcsCount)
    {
        Assert.Null(LcPointMapAutoSelect.ResolveModelId(pcsCount));
    }

    [Fact]
    public void CountPcs_OnlyCountsPcsTemplateNodes()
    {
        var project = BuildProject("pcs", "pcs", "emu_unit", "ac_bus", "pcs");
        Assert.Equal(3, LcPointMapAutoSelect.CountPcs(project));
    }

    [Fact]
    public void ApplyForProject_FourOrEightPcs_DoesNotWriteLcSelection()
    {
        var tmp = CreateTempRoot("standard", "trina_5.5MW");
        try
        {
            DeviceModelRegistry.SaveSelection(new DeviceModelSelection
            {
                Selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["bms"] = "g2_pro",
                    ["emu"] = "standard"
                }
            }, tmp);

            Assert.Null(LcPointMapAutoSelect.ApplyForProject(BuildProject("pcs", "pcs", "pcs", "pcs"), tmp));
            var eight = BuildProject(Enumerable.Repeat("pcs", 8).ToArray());
            Assert.Null(LcPointMapAutoSelect.ApplyForProject(eight, tmp));

            var selection = DeviceModelRegistry.LoadSelection(tmp);
            Assert.False(selection.Selections.ContainsKey("lc"));
            Assert.Equal("standard", selection.Selections["emu"]);
            Assert.Equal("g2_pro", selection.Selections["bms"]);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void ApplyForProject_ExistingLcSelection_LeftUntouched()
    {
        var tmp = CreateTempRoot("standard", "trina_5.5MW");
        try
        {
            DeviceModelRegistry.SaveSelection(new DeviceModelSelection
            {
                Selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["lc"] = "trina_10MW" }
            }, tmp);

            Assert.Null(LcPointMapAutoSelect.ApplyForProject(BuildProject("pcs", "pcs", "pcs", "pcs"), tmp));
            Assert.Equal("trina_10MW", DeviceModelRegistry.LoadSelection(tmp).Selections["lc"]);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void ApplyForProject_StaleEmuTrinaSelection_ResetsEmuWithoutWritingLc()
    {
        var tmp = CreateTempRoot("standard", "trina_5.5MW");
        try
        {
            DeviceModelRegistry.SaveSelection(new DeviceModelSelection
            {
                Selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["emu"] = "trina_10MW"
                }
            }, tmp);

            var applied = LcPointMapAutoSelect.ApplyForProject(BuildProject("pcs", "pcs", "pcs"), tmp);

            Assert.Equal("standard", applied);
            var selection = DeviceModelRegistry.LoadSelection(tmp);
            Assert.False(selection.Selections.ContainsKey("lc"));
            Assert.Equal("standard", selection.Selections["emu"]);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    private static TopologyProject BuildProject(params string[] templateIds)
    {
        var project = new TopologyProject();
        for (int i = 0; i < templateIds.Length; i++)
            project.Nodes.Add(new TopologyNode { Id = $"n{i}", TemplateId = templateIds[i] });
        return project;
    }

    private static string CreateTempRoot(params string[] lcModelIds)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ess-lc-autosel-" + Guid.NewGuid().ToString("N"));
        foreach (var modelId in lcModelIds)
            Directory.CreateDirectory(Path.Combine(tmp, DeviceModelRegistry.ModelsRelativeDir, "lc", modelId));
        Directory.CreateDirectory(Path.Combine(tmp, "configs", "topology"));
        return tmp;
    }
}
