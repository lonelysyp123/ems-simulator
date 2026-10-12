using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;

namespace EssSimulator.Tests.Plant;

/// <summary>钉住 Task 14：ESS 死统计字段、单数 Obsolete 别名、无读取方的 NoGui 已删除。</summary>
public class EnergyStorageSystemDeadMemberTests
{
    [Fact]
    public void EnergyStorageSystem_DoesNotExposeUnusedSessionOrDailyAggregates()
    {
        var names = typeof(EnergyStorageSystem).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain("ChargeSessions", names);
        Assert.DoesNotContain("DischargeSessions", names);
        Assert.DoesNotContain("DailyCharge", names);
        Assert.DoesNotContain("DailyDischarge", names);
    }

    [Fact]
    public void EnergyStorageSystem_DoesNotExposeUnusedStationEnergyAggregates()
    {
        var names = typeof(EnergyStorageSystem).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain("Capacity", names);
        Assert.DoesNotContain("CurrentEnergy", names);
        Assert.DoesNotContain("Efficiency", names);
        Assert.DoesNotContain("TotalChargeEnergy", names);
        Assert.DoesNotContain("TotalDischargeEnergy", names);
        Assert.DoesNotContain("AvailableChargeEnergy", names);
        Assert.DoesNotContain("AvailableDischargeEnergy", names);
    }

    [Fact]
    public void EnergyStorageSystem_DoesNotExposeObsoleteSingularAliases()
    {
        var names = typeof(EnergyStorageSystem).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain("_batteryRack", names);
        Assert.DoesNotContain("_batteryRack2", names);
        Assert.DoesNotContain("_pcs1", names);
        Assert.DoesNotContain("_pcs2", names);
        Assert.Contains("_batteryRacks", names);
        Assert.Contains("_pcsList", names);
    }

    [Fact]
    public void RuntimeConfig_DoesNotExposeNoGui()
    {
        Assert.Null(typeof(RuntimeConfig).GetProperty("NoGui"));
        Assert.Null(typeof(SimulatorConfig).GetProperty("NoGui"));
    }
}
