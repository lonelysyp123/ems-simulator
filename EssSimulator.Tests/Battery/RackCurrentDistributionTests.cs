using EssSimulator.EssDeviceSimModel;

namespace EssSimulator.Tests.Battery;

public class RackCurrentDistributionTests
{
    private static BatteryRackSimulator CreateRack(double resistance) =>
        new(new RackConfiguration
        {
            ClusterCount = 1,
            RackInternalResistance = 0.02,
            ClusterConfig = new ClusterConfiguration
            {
                PackCount = 2,
                ClusterInternalResistance = resistance,
                PackConfig = new PackConfiguration
                {
                    SeriesCount = 2,
                    ParallelCount = 1,
                    NominalVoltage = 3.2,
                    NominalCapacity = 100,
                    InitialSoc = 0.5,
                    PackInternalResistance = 0.05
                }
            }
        })
        {
            CellPhysicsEnabledOverride = false
        };

    [Fact]
    public void PositiveResistance_AssignsRackCurrentToTheCluster()
    {
        var rack = CreateRack(0.1);
        rack.Update(20, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));

        Assert.Equal(20, rack._clusters[0].GetClusterState().TotalCurrent, 6);
        Assert.True(double.IsFinite(rack.GetRackState().TotalVoltage));
    }

    [Fact]
    public void ZeroResistance_Update_KeepsVoltageAndCurrentFinite()
    {
        var rack = CreateRack(0);
        rack.Update(20, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));

        var cluster = rack._clusters[0].GetClusterState();
        Assert.Equal(0, cluster.TotalCurrent, 6);
        Assert.True(double.IsFinite(cluster.TotalVoltage));
        Assert.True(double.IsFinite(rack.GetRackState().TotalVoltage));
    }
}
