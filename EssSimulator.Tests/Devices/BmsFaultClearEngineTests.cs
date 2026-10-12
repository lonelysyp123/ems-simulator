using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Bms;
using EssSimulator.EssDeviceSimModel.Battery;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssSimModelApi;
using EssSimulator.EssSimModelApi.BatteryManagementSystem;
using EssSimulator.EssSimModelApi.Mappers;

namespace EssSimulator.Tests.Devices;

public class BmsFaultClearTests
{
    [Fact]
    public void TryClearChargeDischargeFaults_ClearsDischargeFaultAtIdle()
    {
        var rack = BmsRackFactory.CreateRack(new BmsDeviceConfig { ClusterCount = 1 });
        var device = new BmsRackDevice("bms_test", rack);
        var bmsData = BmsDataGenerator.GenerateSampleData(1, 1);
        var rackState = rack.GetRackState()!;

        rackState.MinClusterSOC = 0.05;
        rackState.MaxClusterSOC = 0.05;
        rackState.StateOfHealth = 1.0;
        SetRackCurrent(rackState, -50);
        foreach (var cluster in rackState.ClusterStates!)
            cluster.MinPackSOC = 0.05;

        BmsMapper.SyncTelemetryAndProtection(device, bmsData);
        Assert.Equal((ushort)2, device.FaultCode);

        SetRackCurrent(rackState, 0);
        Assert.True(BmsMapper.TryClearChargeDischargeFaults(bmsData, device, out var message), message);

        Assert.Equal((ushort)0, device.FaultCode);
        Assert.Equal((ushort)0, bmsData.BatteryStacks[0].BMSFaultSummary);
        Assert.NotEqual(true, bmsData.BatteryStacks[0].SystemAlarms.LowSOCFault);

        // 再次放电且 SOC 仍低：应重新落入三级并置 Rack 放电故障
        SetRackCurrent(rackState, -50);
        BmsMapper.SyncTelemetryAndProtection(device, bmsData);
        Assert.Equal(true, bmsData.BatteryStacks[0].SystemAlarms.LowSOCFault);
        Assert.Equal((ushort)2, device.FaultCode);
    }

    [Fact]
    public void TryClearChargeDischargeFaults_RejectsWhileDischarging()
    {
        var rack = BmsRackFactory.CreateRack(new BmsDeviceConfig { ClusterCount = 1 });
        var device = new BmsRackDevice("bms_test", rack);
        var bmsData = BmsDataGenerator.GenerateSampleData(1, 1);
        SetRackCurrent(rack.GetRackState()!, -50);

        Assert.False(BmsMapper.TryClearChargeDischargeFaults(bmsData, device, out var message));
        Assert.Contains("待机", message);
    }

    [Fact]
    public void TryClearChargeDischargeFaults_ResetsFailedGridConnectStatus()
    {
        var rack = BmsRackFactory.CreateRack(new BmsDeviceConfig { ClusterCount = 1 });
        var device = new BmsRackDevice("bms_test", rack);
        var bmsData = BmsDataGenerator.GenerateSampleData(1, 1);
        var stack = bmsData.BatteryStacks[0];
        stack.GridConnectStatus = 3;

        Assert.True(BmsMapper.TryClearChargeDischargeFaults(bmsData, device, out _));
        Assert.Equal((ushort)0, stack.GridConnectStatus);
    }

    [Fact]
    public void ClearChargeDischargeAlarms_ClearsChargeAndDischargeBits()
    {
        var alarms = new ClusterAlarms
        {
            LowSOCFault = true,
            OvervoltageFault = true,
            InsulationFault = true
        };

        alarms.ClearChargeDischargeAlarms();

        Assert.False(alarms.LowSOCFault);
        Assert.False(alarms.OvervoltageFault);
        Assert.True(alarms.InsulationFault);
    }

    private static void SetRackCurrent(RackState rack, double current)
    {
        rack.TotalCurrent = current;
        foreach (var cluster in rack.ClusterStates!)
            cluster.TotalCurrent = current;
    }
}
