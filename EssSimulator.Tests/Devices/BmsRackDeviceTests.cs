using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Battery;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssSimModelApi;
using EssSimulator.EssSimModelApi.BatteryManagementSystem;
using EssSimulator.EssSimModelApi.Mappers;

namespace EssSimulator.Tests.Devices;

public class BmsRackDeviceTests
{
    [Fact]
    public void SetPcsLinked_UpdatesRackState()
    {
        var rack = BmsRackFactory.CreateRack(new BmsDeviceConfig { ClusterCount = 1 });
        var device = new BmsRackDevice("bms_test", rack);

        device.SetPcsLinked(true);
        Assert.True(device.IsLinked);
        Assert.True(rack.GetRackState()!.IsPcsLinked);

        device.SetPcsLinked(false);
        Assert.False(device.IsLinked);
    }

    [Fact]
    public void SyncTelemetryAndProtection_SetsChargeFaultWhenSocHighWhileCharging()
    {
        var rack = BmsRackFactory.CreateRack(new BmsDeviceConfig { ClusterCount = 1 });
        var device = new BmsRackDevice("bms_test", rack);
        var bmsData = BmsDataGenerator.GenerateSampleData(1, 1);

        var rackState = rack.GetRackState()!;
        rackState.MinClusterSOC = 0.96;
        rackState.MaxClusterSOC = 0.96;
        rackState.StateOfHealth = 1.0;
        SetRackCurrent(rackState, 50);

        BmsMapper.SyncTelemetryAndProtection(device, bmsData);

        Assert.Equal((ushort)1, device.FaultCode);
        Assert.True(device.HasBlockingFault);
    }

    [Fact]
    public void SyncTelemetryAndProtection_NoChargeFaultWhenSocHighAtIdle()
    {
        var rack = BmsRackFactory.CreateRack(new BmsDeviceConfig { ClusterCount = 1 });
        var device = new BmsRackDevice("bms_test", rack);
        var bmsData = BmsDataGenerator.GenerateSampleData(1, 1);

        var rackState = rack.GetRackState()!;
        rackState.MinClusterSOC = 0.96;
        rackState.MaxClusterSOC = 0.96;
        rackState.StateOfHealth = 1.0;
        SetRackCurrent(rackState, 0);

        BmsMapper.SyncTelemetryAndProtection(device, bmsData);

        Assert.Equal((ushort)0, device.FaultCode);
        Assert.False(device.HasBlockingFault);
    }

    [Fact]
    public void SyncTelemetryAndProtection_SetsDischargeFaultWhenSocLowWhileDischarging()
    {
        var rack = BmsRackFactory.CreateRack(new BmsDeviceConfig { ClusterCount = 1 });
        var device = new BmsRackDevice("bms_test", rack);
        var bmsData = BmsDataGenerator.GenerateSampleData(1, 1);

        var rackState = rack.GetRackState()!;
        rackState.MinClusterSOC = 0.05;
        rackState.MaxClusterSOC = 0.05;
        rackState.StateOfHealth = 1.0;
        foreach (var cluster in rackState.ClusterStates!)
            cluster.MinPackSOC = 0.05;
        SetRackCurrent(rackState, -50);

        BmsMapper.SyncTelemetryAndProtection(device, bmsData);

        Assert.Equal((ushort)2, device.FaultCode);
        Assert.True(device.HasBlockingFault);
    }

    [Fact]
    public void SyncTelemetryAndProtection_NoDischargeFaultWhenSocLowAtIdle()
    {
        var rack = BmsRackFactory.CreateRack(new BmsDeviceConfig { ClusterCount = 1 });
        var device = new BmsRackDevice("bms_test", rack);
        var bmsData = BmsDataGenerator.GenerateSampleData(1, 1);

        var rackState = rack.GetRackState()!;
        rackState.MinClusterSOC = 0.05;
        rackState.MaxClusterSOC = 0.05;
        rackState.StateOfHealth = 1.0;
        foreach (var cluster in rackState.ClusterStates!)
            cluster.MinPackSOC = 0.05;
        SetRackCurrent(rackState, 0);

        BmsMapper.SyncTelemetryAndProtection(device, bmsData);

        Assert.Equal((ushort)0, device.FaultCode);
        Assert.False(device.HasBlockingFault);
    }

    private static void SetRackCurrent(RackState rack, double current)
    {
        rack.TotalCurrent = current;
        foreach (var cluster in rack.ClusterStates!)
            cluster.TotalCurrent = current;
    }

    [Fact]
    public void UpdatePhysics_RefreshesDcPortVoltageWhenLinked()
    {
        var rack = BmsRackFactory.CreateRack(new BmsDeviceConfig { ClusterCount = 1 });
        var device = new BmsRackDevice("bms_test", rack);
        device.SetPcsLinked(true);

        device.UpdatePhysics(0, 25.0, DateTime.Now, TimeSpan.FromMilliseconds(200));

        var voltage = device.Port.Output.Dc?.VoltageV ?? 0;
        Assert.True(voltage > 0);
    }

    [Fact]
    public void UpdatePhysics_ZeroVoltageWhenUnlinked()
    {
        var rack = BmsRackFactory.CreateRack(new BmsDeviceConfig { ClusterCount = 1 });
        var device = new BmsRackDevice("bms_test", rack);
        device.SetPcsLinked(false);

        device.UpdatePhysics(10, 25.0, DateTime.Now, TimeSpan.FromMilliseconds(200));

        Assert.Equal(0, device.Port.Output.Dc?.VoltageV ?? -1);
    }

    [Fact]
    public void SyncTelemetryAndProtection_ZerosStackDcVoltageWhenUnlinked()
    {
        var rack = BmsRackFactory.CreateRack(new BmsDeviceConfig { ClusterCount = 1 });
        var device = new BmsRackDevice("bms_test", rack);
        device.SetPcsLinked(true);
        device.UpdatePhysics(0, 25.0, DateTime.Now, TimeSpan.FromMilliseconds(200));
        Assert.True((rack.GetRackState()?.TotalVoltage ?? 0) > 0);

        var bmsData = BmsDataGenerator.GenerateSampleData(1, 1);
        device.SetPcsLinked(false);
        BmsMapper.SyncTelemetryAndProtection(device, bmsData);

        Assert.Equal(0f, bmsData.BatteryStacks[0].TotalVoltage);
        Assert.True((rack.GetRackState()?.TotalVoltage ?? 0) > 0);
    }

    [Fact]
    public void TrySetSoc_UpdatesDeviceSocAndPreservesLink()
    {
        var rack = BmsRackFactory.CreateRack(new BmsDeviceConfig
        {
            ClusterCount = 1,
            PackCount = 1,
            CellSeriesCount = 2,
            CellParallelCount = 1,
            CellInitialSoc = 0.4,
            CellInitialSocRandomRange = 0
        });
        var device = new BmsRackDevice("bms_test", rack);
        device.SetPcsLinked(true);

        Assert.True(device.TrySetSoc(0.66, out var message));
        Assert.Contains("0.66", message);
        Assert.Equal(0.66, device.Soc, 2);
        Assert.True(device.IsLinked);
    }

    [Fact]
    public void TrySetSoc_RejectsWhileCharging()
    {
        var rack = BmsRackFactory.CreateRack(new BmsDeviceConfig { ClusterCount = 1 });
        var device = new BmsRackDevice("bms_test", rack);
        rack.GetRackState()!.TotalCurrent = 40;

        Assert.False(device.TrySetSoc(0.7, out var message));
        Assert.Contains("待机", message);
        Assert.NotEqual(0.7, device.Soc, 2);
    }

    [Fact]
    public void TrySetSoc_RejectsWhileDischarging()
    {
        var rack = BmsRackFactory.CreateRack(new BmsDeviceConfig { ClusterCount = 1 });
        var device = new BmsRackDevice("bms_test", rack);
        rack.GetRackState()!.TotalCurrent = -40;

        Assert.False(device.TrySetSoc(0.3, out var message));
        Assert.Contains("待机", message);
    }
}
