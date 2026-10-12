using EssSimulator.Configuration;
using EssSimulator.LocalControl;

namespace EssSimulator.Tests.LocalControl;

public class LcChannelMapTests
{
    [Theory]
    [InlineData(1, 0, "group_param4")]
    [InlineData(1, 1, "group_param5")]
    [InlineData(2, 0, "group_param32")]
    [InlineData(2, 1, "group_param33")]
    public void Fault_MatchesExpandedGroupNames(int n, int k, string expected) =>
        Assert.Equal(expected, LcChannelMap.Fault(n, k));

    [Theory]
    [InlineData(1, 0, "group_param60")]
    [InlineData(2, 0, "group_param80")]
    public void StartStop_MatchesExpandedGroupNames(int n, int k, string expected) =>
        Assert.Equal(expected, LcChannelMap.StartStop(n, k));

    [Fact]
    public void ExtraPower_Group2_MatchesGroupParamFormulas()
    {
        Assert.Equal("group_param102", LcChannelMap.ExtraActivePower(1, 0));
        Assert.Equal("group_param110", LcChannelMap.ExtraActivePower(2, 0));
        Assert.Equal("group_param117", LcChannelMap.ExtraReactivePower(2, 3));
    }

    [Fact]
    public void Ready_DoesNotCollideWithExtraPowerNames()
    {
        Assert.Equal("group_param118", LcChannelMap.Ready(1, 0));
        Assert.Equal("group_param121", LcChannelMap.Ready(1, 3));
        Assert.Equal("group_param122", LcChannelMap.Ready(2, 0));
        Assert.Equal("group_param125", LcChannelMap.Ready(2, 3));
    }

    [Fact]
    public void Voltage_SlotOffsets()
    {
        Assert.Equal("group_param20", LcChannelMap.Vab(1, 0));
        Assert.Equal("group_param23", LcChannelMap.Vab(1, 1));
        Assert.Equal("group_param48", LcChannelMap.Vab(2, 0));
    }

    [Fact]
    public void ChannelMap_NamesExistInExpandedGroupCsv()
    {
        var path = Path.Combine(FindRepoRoot(), "pointmaps", "models", "lc", "group", "lc.csv");
        var names = LcPointMapExpander.ExpandFile(path, 2)
            .Select(e => e.ParamName)
            .ToHashSet();

        Assert.Contains(LcChannelMap.Fault(1, 0), names);
        Assert.Contains(LcChannelMap.Fault(2, 0), names);
        Assert.Contains(LcChannelMap.Alarm(1, 3), names);
        Assert.Contains(LcChannelMap.Status(2, 1), names);
        Assert.Contains(LcChannelMap.Freq(1, 0), names);
        Assert.Contains(LcChannelMap.Vab(1, 0), names);
        Assert.Contains(LcChannelMap.Vca(1, 3), names);
        Assert.Contains(LcChannelMap.StartStop(1, 0), names);
        Assert.Contains(LcChannelMap.ActivePowerSet(2, 3), names);
        Assert.Contains(LcChannelMap.ReactivePowerSet(1, 1), names);
        Assert.Contains(LcChannelMap.IslandV(1, 0), names);
        Assert.Contains(LcChannelMap.IslandF(1, 0), names);
        Assert.Contains(LcChannelMap.IslandF(2, 3), names);
        Assert.Contains(LcChannelMap.ExtraActivePower(1, 0), names);
        Assert.Contains(LcChannelMap.ExtraReactivePower(2, 3), names);
        Assert.Contains(LcChannelMap.Ready(1, 0), names);
        Assert.Contains(LcChannelMap.Ready(2, 3), names);
    }

    [Fact]
    public void Ready_Group1_OccupiesAddresses236To239()
    {
        var path = Path.Combine(FindRepoRoot(), "pointmaps", "models", "lc", "group", "lc.csv");
        var ready = LcPointMapExpander.ExpandFile(path, 2)
            .Where(e => e.ParamName == LcChannelMap.Ready(1, 0)
                     || e.ParamName == LcChannelMap.Ready(1, 3)
                     || e.ParamName == LcChannelMap.Ready(2, 0))
            .Select(e => (e.ParamName, e.FunctionCode, e.Address))
            .ToList();

        Assert.Contains(("group_param118", 4, 17236), ready);
        Assert.Contains(("group_param121", 4, 17239), ready);
        Assert.Contains(("group_param122", 4, 17536), ready);
    }

    [Fact]
    public void IslandF_MatchesExpandedGroupNames()
    {
        Assert.Equal("group_param76", LcChannelMap.IslandF(1, 0));
        Assert.Equal("group_param79", LcChannelMap.IslandF(1, 3));
        Assert.Equal("group_param96", LcChannelMap.IslandF(2, 0));
    }

    [Fact]
    public void FlatIndex_TwoGroups_Group2Slot0_IsThirdPcs()
    {
        var unit = new EssUnitConfig
        {
            Groups =
            {
                new EmuGroupConfig { Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig() } },
                new EmuGroupConfig { Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig() } }
            }
        };
        Assert.Equal(0, LcPcsIndex.Flat(unit, 0, 0));
        Assert.Equal(1, LcPcsIndex.Flat(unit, 0, 1));
        Assert.Equal(2, LcPcsIndex.Flat(unit, 1, 0));
        Assert.Equal(3, LcPcsIndex.Flat(unit, 1, 1));
    }

    [Fact]
    public void TryProtocol_AnyNonNegativeSlot_UsesSinglePcsMap()
    {
        Assert.Equal("yc46", EmuPcsChannel.TryProtocol(0)!.Fault);
        Assert.Equal("yk3", EmuPcsChannel.TryProtocol(1)!.StartStop);
        Assert.Equal("yk3", EmuPcsChannel.TryProtocol(7)!.StartStop);
        Assert.Equal("yt4", EmuPcsChannel.TryProtocol(0)!.IslandF);
        Assert.Null(EmuPcsChannel.TryProtocol(-1));
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
