using EssSimulator.Configuration;
using EssSimulator.LocalControl;

namespace EssSimulator.Tests.LocalControl;

public class EmuProtocolLayoutTests
{
    [Fact]
    public void Enumerate_EmptyUnits_OneImplicitPcs()
    {
        var eps = EmuProtocolLayout.Enumerate((IReadOnlyList<EssUnitConfig>?)null);
        Assert.Single(eps);
        Assert.Equal("simEmu1", eps[0].ServerName);
        Assert.Equal(0, eps[0].UnitIndex0);
        Assert.Equal(0, eps[0].PcsIndex0);
    }

    [Fact]
    public void Enumerate_TwoUnitsTwoGroupsOfFour_SixteenServers()
    {
        var units = new List<EssUnitConfig>
        {
            TwoGroups("A"),
            TwoGroups("B")
        };

        var eps = EmuProtocolLayout.Enumerate(units);
        Assert.Equal(16, eps.Count);
        Assert.Equal("simEmu1", eps[0].ServerName);
        Assert.Equal(0, eps[0].UnitIndex0);
        Assert.Equal(0, eps[0].PcsIndex0);
        Assert.Equal("simEmu8", eps[7].ServerName);
        Assert.Equal(0, eps[7].UnitIndex0);
        Assert.Equal(7, eps[7].PcsIndex0);
        Assert.Equal("simEmu9", eps[8].ServerName);
        Assert.Equal(1, eps[8].UnitIndex0);
        Assert.Equal(0, eps[8].PcsIndex0);
        Assert.Equal(2, eps[8].UnitId);
        Assert.Equal("simEmu16", eps[15].ServerName);
        Assert.Equal(2, eps[15].UnitId);
        Assert.Equal(7, eps[15].PcsIndex0);
    }

    [Fact]
    public void Enumerate_FlatUnit_DefaultTwoPcs_TwoServers()
    {
        var units = new List<EssUnitConfig> { new() { Name = "flat" } };
        var eps = EmuProtocolLayout.Enumerate(units);
        Assert.Equal(2, eps.Count);
        Assert.Equal(new[] { "simEmu1", "simEmu2" }, eps.Select(e => e.ServerName));
        Assert.Equal(0, eps[1].UnitIndex0);
        Assert.Equal(1, eps[1].PcsIndex0);
    }

    [Fact]
    public void TryMapUnitSlot_FourPcsTwoGroups_SecondGroupFirstSlot()
    {
        var unit = TwoGroups("A");
        Assert.True(EmuProtocolLayout.TryMapUnitSlot(unit, 4, out int g, out int k));
        Assert.Equal(1, g);
        Assert.Equal(0, k);

        Assert.True(EmuProtocolLayout.TryMapUnitSlot(unit, 6, out g, out k));
        Assert.Equal(1, g);
        Assert.Equal(2, k);
    }

    [Fact]
    public void ServerNameFor_SecondUnitFirstPcs_IsSimEmu9()
    {
        var units = new List<EssUnitConfig> { TwoGroups("A"), TwoGroups("B") };
        Assert.Equal("simEmu9", EmuProtocolLayout.ServerNameFor(units, 1, 0));
        Assert.Equal("simEmu2", EmuProtocolLayout.ServerNameFor(units, 0, 1));
        Assert.Equal("simEmu5", EmuProtocolLayout.ServerNameFor(units, 0, 4));
    }

    private static EssUnitConfig TwoGroups(string name) => new()
    {
        Name = name,
        Groups =
        {
            new EmuGroupConfig
            {
                Name = "g1",
                Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig(), new PcsDeviceConfig(), new PcsDeviceConfig() }
            },
            new EmuGroupConfig
            {
                Name = "g2",
                Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig(), new PcsDeviceConfig(), new PcsDeviceConfig() }
            }
        }
    };
}
