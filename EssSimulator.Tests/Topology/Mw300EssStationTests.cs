using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.Protocol.Iec61850;
using EssSimulator.Protocol.Modbus;
using EssSimulator.Web.Topology;

namespace EssSimulator.Tests.Topology;

public class Mw300EssStationTests
{
    [Fact]
    public void BuildOverlay_IsSixtyUnitsOfTwoPcs_NoPv()
    {
        var overlay = Mw300EssStation.BuildOverlay();

        Assert.Equal("mw300-ess-test", overlay.SourceProjectId);
        Assert.Equal("300MW纯储能测试工程", overlay.SourceProjectName);
        Assert.Equal(60, overlay.EssUnits.Count);
        Assert.Empty(overlay.PvUnits);
        Assert.True(TopologyOverlayLoader.IsUsable(overlay));
        Assert.All(overlay.EssUnits, unit =>
        {
            Assert.Equal(2, unit.PcsCount);
            Assert.Equal(2, unit.Bms.Count);
            Assert.All(unit.Bms, bms =>
            {
                Assert.Equal(12, bms.ClusterCount);
                Assert.Equal(4, bms.PackCount);
                Assert.Equal(104, bms.CellSeriesCount);
            });
        });
    }

    [Fact]
    public void BuildOverlay_NamesThreeSectionsOfTwenty()
    {
        var units = Mw300EssStation.BuildOverlay().EssUnits;

        Assert.Equal("S1-U01", units[0].Name);
        Assert.Equal("S1-U20", units[19].Name);
        Assert.Equal("S2-U01", units[20].Name);
        Assert.Equal("S3-U20", units[59].Name);
        Assert.Equal(3, units.Select(u => u.Name[..2]).Distinct().Count());
    }

    [Fact]
    public void BuildOverlay_FixesElectricalRatingsAndSvg()
    {
        var overlay = Mw300EssStation.BuildOverlay();

        Assert.Equal(2500, overlay.Pcs!.RatedPower);
        Assert.Equal(2750, overlay.Pcs.MaxPower);
        Assert.Equal(300000, overlay.Transformer!.RatedPower);
        Assert.Equal(220000, overlay.Transformer.PrimaryVoltage);
        Assert.Equal(35000, overlay.Transformer.SecondaryVoltage);
        Assert.Equal(6300, overlay.UnitTransformer!.RatedPower);
        Assert.Equal(35000, overlay.UnitTransformer.PrimaryVoltage);
        Assert.Equal(690, overlay.UnitTransformer.SecondaryVoltage);
        Assert.Equal(220000, overlay.Pcc!.NominalLineVoltage);
        Assert.True(overlay.EnableSvg);
        Assert.Equal(10000, overlay.SvgRatedCapacityKvar);
    }

    [Fact]
    public void BuildPortPlan_OneTcpPortPerUnit_SlavesDoNotOverlap()
    {
        var plan = Mw300EssStation.BuildPortPlan();

        Assert.Equal(120, plan.Entries.Count(e => e.Type == ProtocolDeviceType.Bms));
        Assert.Equal(120, plan.Entries.Count(e => e.Type == ProtocolDeviceType.Emu));
        Assert.Equal(60, plan.Entries.Count(e => e.Type == ProtocolDeviceType.Lc));
        Assert.NotNull(plan.Find("simEm"));
        Assert.NotNull(plan.Find("simSel"));
        Assert.NotNull(plan.Find("simSvg"));
        Assert.Empty(plan.ValidateRanges());

        var unit1 = plan.Entries.Where(e => e.Port == 10001).ToList();
        Assert.Equal(1, unit1.Single(e => e.Name == "simLc1").SlaveId);
        Assert.Equal(2, unit1.Single(e => e.Name == "simEmu1").SlaveId);
        Assert.Equal(3, unit1.Single(e => e.Name == "simEmu2").SlaveId);
        Assert.Equal(10, unit1.Single(e => e.Name == "simBms1").SlaveId);
        Assert.Equal(12, unit1.Single(e => e.Name == "simBms1").RackCount);
        Assert.Equal(42, unit1.Single(e => e.Name == "simBms2").SlaveId);

        Assert.Equal(10002, plan.Find("simBms3")!.Port);
        Assert.Equal(10060, plan.Find("simLc60")!.Port);
        Assert.Equal(10060, plan.Find("simEmu120")!.Port);
        Assert.Equal(3, plan.Find("simEmu120")!.SlaveId);
        Assert.Equal(42, plan.Find("simBms120")!.SlaveId);

        Assert.Equal(1500, plan.Find("simEm")!.Port);
        Assert.Equal(2001, plan.Find("simSel")!.Port);
        Assert.Equal(2201, plan.Find("simSvg")!.Port);

        var allowedLegacy = new HashSet<int> { 1500, 2001, 2201 };
        Assert.All(plan.Entries, entry =>
        {
            bool inLegacyLane = entry.Port is >= 1500 and <= 2299;
            Assert.True(!inLegacyLane || allowedLegacy.Contains(entry.Port));
        });

        foreach (var group in plan.Entries.GroupBy(e => e.Port))
        {
            var used = new HashSet<int>();
            foreach (var entry in group)
            {
                Assert.True(used.Add(entry.SlaveId), $"{entry.Name} 从站 {entry.SlaveId} 冲突");
                for (int rack = 1; rack <= entry.RackCount; rack++)
                    Assert.True(used.Add(entry.SlaveId + rack), $"{entry.Name} 簇从站冲突");
            }
        }
    }

    [Fact]
    public void ShouldStartMms_IsOffForFrozenProfile()
    {
        Assert.False(Iec61850LayerManager.ShouldStartMms(
            new SimulatorConfig { StationProfileId = Mw300EssStation.Id }));
        Assert.True(Iec61850LayerManager.ShouldStartMms(new SimulatorConfig()));
    }

    [Fact]
    public void TrySetCellPhysics_FrozenStationStaysEquivalent()
    {
        bool previous = BatteryCalcMode.CellPhysicsEnabled;
        try
        {
            BatteryCalcMode.CellPhysicsEnabled = true;
            Assert.False(Mw300EssStation.TrySetCellPhysics(Mw300EssStation.Id, true));
            Assert.False(BatteryCalcMode.CellPhysicsEnabled);

            Assert.True(Mw300EssStation.TrySetCellPhysics(null, true));
            Assert.True(BatteryCalcMode.CellPhysicsEnabled);
        }
        finally
        {
            BatteryCalcMode.CellPhysicsEnabled = previous;
        }
    }
}
