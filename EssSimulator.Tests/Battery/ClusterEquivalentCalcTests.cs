using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssSimModelApi;
using EssSimulator.EssSimModelApi.Mappers;
using Xunit;

namespace EssSimulator.Tests.Battery;

public class ClusterEquivalentCalcTests
{
    private static BatteryRackSimulator CreateRack(double initialSoc = 0.5) =>
        new(new RackConfiguration
        {
            ClusterCount = 1,
            RackInternalResistance = 0.02,
            ClusterConfig = new ClusterConfiguration
            {
                PackCount = 2,
                ClusterInternalResistance = 0.1,
                PackConfig = new PackConfiguration
                {
                    SeriesCount = 2,
                    ParallelCount = 1,
                    NominalVoltage = 3.2,
                    NominalCapacity = 100,
                    InitialSoc = initialSoc,
                    PackInternalResistance = 0.05
                }
            }
        });

    [Fact]
    public void CellPhysics_UpdatesEveryCell()
    {
        var rack = CreateRack();
        rack.CellPhysicsEnabledOverride = true;
        var pack = rack._clusters[0]._packs[0];
        double lead = pack.GetCellState(0, 0).SOC;
        double other = pack.GetCellState(1, 0).SOC;

        rack.Update(20, 25, DateTime.UtcNow, TimeSpan.FromHours(0.5));

        Assert.True(pack.GetCellState(0, 0).SOC > lead);
        Assert.True(pack.GetCellState(1, 0).SOC > other);
        Assert.False(rack._clusters[0].GetClusterState().EquivalentCells);
    }

    [Fact]
    public void EquivalentMode_StepsOnlyRepresentativeCell_AndScalesClusterVoltage()
    {
        var rack = CreateRack();
        rack.CellPhysicsEnabledOverride = false;
        var cluster = rack._clusters[0];
        var pack = cluster._packs[0];
        double otherBefore = pack.GetCellState(1, 0).SOC;
        double secondPackBefore = cluster._packs[1].GetCellState(0, 0).SOC;

        rack.Update(10, 25, DateTime.UtcNow, TimeSpan.FromHours(1));

        var state = cluster.GetClusterState();
        double cellVoltage = pack.GetCellState(0, 0).Voltage;
        Assert.True(pack.GetCellState(0, 0).SOC > otherBefore + 0.05);
        Assert.Equal(otherBefore, pack.GetCellState(1, 0).SOC, 6);
        Assert.Equal(secondPackBefore, cluster._packs[1].GetCellState(0, 0).SOC, 6);
        Assert.True(state.EquivalentCells);
        Assert.Equal(cellVoltage, state.EquivalentCellVoltage, 6);
        Assert.Equal(cellVoltage * 4, state.TotalVoltage, 6);
        Assert.Empty(state.PackStates[0].CellStates);
        Assert.Equal(state.EquivalentCellVoltage, state.MaxPackVoltage / 2, 6);
    }

    [Fact]
    public void RestoringCellPhysics_CopiesRepresentativeSocToAllCells()
    {
        var rack = CreateRack();
        rack.CellPhysicsEnabledOverride = false;
        rack.Update(10, 25, DateTime.UtcNow, TimeSpan.FromHours(1));

        double leadSoc = rack._clusters[0]._packs[0].GetCellState(0, 0).SOC;
        rack.CellPhysicsEnabledOverride = true;
        rack.Update(0, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));

        var cluster = rack._clusters[0];
        Assert.False(cluster.GetClusterState().EquivalentCells);
        Assert.Equal(leadSoc, cluster._packs[0].GetCellState(0, 0).SOC, 5);
        Assert.Equal(leadSoc, cluster._packs[0].GetCellState(1, 0).SOC, 5);
        Assert.Equal(leadSoc, cluster._packs[1].GetCellState(0, 0).SOC, 5);
        Assert.Equal(leadSoc, cluster._packs[1].GetCellState(1, 0).SOC, 5);
    }

    [Fact]
    public void MapClusters_PublishesUniformCellValuesInEquivalentMode()
    {
        var rack = CreateRack();
        rack.CellPhysicsEnabledOverride = false;
        rack.Update(10, 25, DateTime.UtcNow, TimeSpan.FromMinutes(30));

        var data = BmsDataGenerator.GenerateSampleData(1, 1);
        BmsMapper.MapRackToStack(rack.GetRackState()!, data);
        BmsMapper.MapClusters(rack, data);

        var cluster = data.BatteryStacks[0].Cluseter[0];
        float voltage = (float)rack._clusters[0].GetClusterState().EquivalentCellVoltage;
        Assert.Equal(voltage, cluster.Measurements.MaxCellVoltage);
        Assert.Equal(voltage, cluster.Measurements.MinCellVoltage);
        Assert.Equal(voltage, cluster.Measurements.AvgCellVoltage);
        Assert.Equal(4, cluster.ClusterCellVoltages.CellVoltages.Count);
        Assert.All(cluster.ClusterCellVoltages.CellVoltages.Values, v => Assert.Equal(voltage, v));
        Assert.Equal(voltage, data.BatteryStacks[0].MaxCellVoltage);
    }
}
