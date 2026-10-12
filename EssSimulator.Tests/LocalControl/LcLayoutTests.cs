using EssSimulator.Configuration;
using EssSimulator.LocalControl;

namespace EssSimulator.Tests.LocalControl;

public class LcLayoutTests
{
    [Fact]
    public void GroupCount_FlatUnit_IsOne()
    {
        Assert.Equal(1, LcLayout.GroupCount(new EssUnitConfig { Name = "flat" }));
        Assert.Equal(1, LcLayout.GroupCount(null));
    }

    [Fact]
    public void GroupCount_GroupedUnit_UsesGroupsCount()
    {
        var unit = new EssUnitConfig
        {
            Groups =
            {
                new EmuGroupConfig { Name = "g1", Pcs = { new PcsDeviceConfig() } },
                new EmuGroupConfig { Name = "g2", Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig() } }
            }
        };
        Assert.Equal(2, LcLayout.GroupCount(unit));
        Assert.Equal(1, LcLayout.PcsCountInGroup(unit, 0));
        Assert.Equal(2, LcLayout.PcsCountInGroup(unit, 1));
        Assert.Equal(0, LcLayout.PcsCountInGroup(unit, 2));
    }

    [Fact]
    public void GroupCountForUnit_EmptyDevices_DefaultsToOne()
    {
        var cfg = new SimulatorConfig();
        Assert.Equal(1, LcLayout.GroupCountForUnit(cfg, 0));
    }

    [Fact]
    public void ExpandGroupCount_AtLeastTwoPcsGroups()
    {
        Assert.Equal(2, LcLayout.PcsGroupsPerEmu);
        Assert.Equal(2, LcLayout.ExpandGroupCount(null));
        Assert.Equal(2, LcLayout.ExpandGroupCount(new EssUnitConfig { Name = "flat" }));
        var two = new EssUnitConfig
        {
            Groups =
            {
                new EmuGroupConfig { Pcs = { new PcsDeviceConfig() } },
                new EmuGroupConfig { Pcs = { new PcsDeviceConfig() } }
            }
        };
        Assert.Equal(2, LcLayout.ExpandGroupCount(two));
        var three = new EssUnitConfig
        {
            Groups =
            {
                new EmuGroupConfig { Pcs = { new PcsDeviceConfig() } },
                new EmuGroupConfig { Pcs = { new PcsDeviceConfig() } },
                new EmuGroupConfig { Pcs = { new PcsDeviceConfig() } }
            }
        };
        Assert.Equal(3, LcLayout.ExpandGroupCount(three));
    }

    [Fact]
    public void MaxGroupCount_IsTwenty()
    {
        Assert.Equal(20, LcLayout.MaxGroupCount);
        Assert.True(LcLayout.ShouldUseExclusiveEmuMap(0));
        Assert.True(LcLayout.ShouldUseExclusiveEmuMap(2));
        Assert.False(LcLayout.ShouldUseExclusiveEmuMap(3));
        Assert.False(LcLayout.ShouldUseExclusiveEmuMap(4));
    }

    [Fact]
    public void MaxPcsInAnyGroup_UsesLargestGroup()
    {
        var unit = new EssUnitConfig
        {
            Groups =
            {
                new EmuGroupConfig { Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig() } },
                new EmuGroupConfig
                {
                    Pcs =
                    {
                        new PcsDeviceConfig(), new PcsDeviceConfig(),
                        new PcsDeviceConfig(), new PcsDeviceConfig()
                    }
                }
            }
        };
        Assert.Equal(4, LcLayout.MaxPcsInAnyGroup(unit));
        Assert.Equal(4, LcLayout.MaxPcsInAnyGroup(new EssUnitConfig
        {
            Pcs =
            {
                new PcsDeviceConfig(), new PcsDeviceConfig(),
                new PcsDeviceConfig(), new PcsDeviceConfig()
            }
        }));
        Assert.Equal(0, LcLayout.MaxPcsInAnyGroup(null));
    }
}
