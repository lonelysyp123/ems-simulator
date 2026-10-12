using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Bms;
using EssSimulator.EssDeviceSimModel.Devices;

namespace EssSimulator.Tests.Devices;

public class BmsRackProtectionTests
{
    [Fact]
    public void UpdateUnder_EscalatesFromAlarmToFault()
    {
        bool? l1 = false, l2 = true, l3 = false;
        BmsRackProtection.UpdateUnder(ref l1, ref l2, ref l3,
            t1: 100, t2: 90, t3: 80,
            r1: 105, r2: 95, r3: 85,
            val: 75);

        Assert.False(l1);
        Assert.False(l2);
        Assert.True(l3);
    }

    [Fact]
    public void UpdateOver_RecoversFromFaultToAlarm()
    {
        bool? l1 = false, l2 = false, l3 = true;
        BmsRackProtection.UpdateOver(ref l1, ref l2, ref l3,
            t1: 50, t2: 55, t3: 60,
            r1: 48, r2: 53, r3: 58,
            val: 57);

        Assert.False(l3);
        Assert.True(l2);
    }

    [Fact]
    public void UpdateUnder_TriggersProtectionWhenBelowThreshold()
    {
        bool? l1 = null, l2 = null, l3 = null;
        BmsRackProtection.UpdateUnder(ref l1, ref l2, ref l3,
            t1: 600, t2: 580, t3: 560,
            r1: 610, r2: 590, r3: 570,
            val: 595);

        Assert.True(l1);
        Assert.Null(l2);
        Assert.Null(l3);
    }

    [Fact]
    public void UpdateUnder_SnapsToFaultWhenDeeplyBelowThreshold()
    {
        bool? l1 = false, l2 = false, l3 = false;
        BmsRackProtection.UpdateUnder(ref l1, ref l2, ref l3,
            t1: 0.15f, t2: 0.10f, t3: 0.05f,
            r1: 0.18f, r2: 0.13f, r3: 0.08f,
            val: 0.04);

        Assert.False(l1);
        Assert.False(l2);
        Assert.True(l3);
    }

    [Fact]
    public void UpdateOver_SnapsToFaultWhenDeeplyAboveThreshold()
    {
        bool? l1 = false, l2 = false, l3 = false;
        BmsRackProtection.UpdateOver(ref l1, ref l2, ref l3,
            t1: 50, t2: 55, t3: 60,
            r1: 48, r2: 53, r3: 58,
            val: 65);

        Assert.False(l1);
        Assert.False(l2);
        Assert.True(l3);
    }

    [Fact]
    public void EvaluateCluster_SkipsLowSocWhenIdle()
    {
        var alarms = new ClusterAlarms();
        var thresholds = new ClusterThresholds();
        var clusterState = CreateClusterState(minPackSoc: 0.05, current: 0);

        BmsRackProtection.EvaluateCluster(
            clusterState, packCount: 1, cellsPerPack: 1, thresholds, alarms, insulationValue: 1000f);

        Assert.False(alarms.LowSOCProtection);
        Assert.False(alarms.LowSOCAlarm);
        Assert.False(alarms.LowSOCFault);
    }

    [Fact]
    public void EvaluateCluster_SnapsToLowSocFaultWhenDischargingDeeplyLow()
    {
        var alarms = new ClusterAlarms();
        var thresholds = new ClusterThresholds();
        var clusterState = CreateClusterState(minPackSoc: 0.05, current: -50);

        BmsRackProtection.EvaluateCluster(
            clusterState, packCount: 1, cellsPerPack: 1, thresholds, alarms, insulationValue: 1000f);

        Assert.True(alarms.LowSOCFault);
        Assert.False(alarms.LowSOCProtection);
        Assert.False(alarms.LowSOCAlarm);
    }

    [Fact]
    public void EvaluateCluster_ClearedFaultRetriggersWhenDischargingAgain()
    {
        var alarms = new ClusterAlarms
        {
            LowSOCProtection = true,
            LowSOCAlarm = true,
            LowSOCFault = true
        };
        var thresholds = new ClusterThresholds();

        // 待机清除：方向门控下 LowSOC 不评估，位保持清除结果
        alarms.ClearChargeDischargeAlarms();
        var idle = CreateClusterState(minPackSoc: 0.05, current: 0);
        BmsRackProtection.EvaluateCluster(
            idle, packCount: 1, cellsPerPack: 1, thresholds, alarms, insulationValue: 1000f);
        Assert.False(alarms.LowSOCFault);

        // 再次放电且仍超限：一次性落入三级
        var discharging = CreateClusterState(minPackSoc: 0.05, current: -50);
        BmsRackProtection.EvaluateCluster(
            discharging, packCount: 1, cellsPerPack: 1, thresholds, alarms, insulationValue: 1000f);
        Assert.True(alarms.LowSOCFault);
    }

    [Fact]
    public void EvaluateCluster_SkipsOvervoltageWhenIdle()
    {
        var alarms = new ClusterAlarms();
        var thresholds = new ClusterThresholds();
        var clusterState = CreateClusterState(totalVoltage: thresholds.OvervoltageThreshold1!.Value + 10, current: 0);

        BmsRackProtection.EvaluateCluster(
            clusterState, packCount: 1, cellsPerPack: 1, thresholds, alarms, insulationValue: 1000f);

        Assert.False(alarms.OvervoltageProtection);
    }

    [Fact]
    public void EvaluateCluster_EvaluatesOvervoltageWhenCharging()
    {
        var alarms = new ClusterAlarms();
        var thresholds = new ClusterThresholds();
        var clusterState = CreateClusterState(totalVoltage: thresholds.OvervoltageThreshold1!.Value + 10, current: 50);

        BmsRackProtection.EvaluateCluster(
            clusterState, packCount: 1, cellsPerPack: 1, thresholds, alarms, insulationValue: 1000f);

        Assert.True(alarms.OvervoltageProtection);
    }

    [Fact]
    public void EvaluateCluster_SetsTerminalAndHvbFromPoleAndBusbarTemps()
    {
        var alarms = new ClusterAlarms();
        var thresholds = new ClusterThresholds();
        var clusterState = CreateClusterState(current: 0);

        BmsRackProtection.EvaluateCluster(
            clusterState, packCount: 1, cellsPerPack: 1, thresholds, alarms,
            insulationValue: 1000f,
            busbarTempC: thresholds.HVBHighTempThreshold3!.Value + 1,
            poleTempC: thresholds.PoleHighTempThreshold3!.Value + 1);

        Assert.True(alarms.TerminalHighTempFault);
        Assert.True(alarms.HVBHighTempFault);
        Assert.True((alarms.RackFaultSummary1 & (1 << 11)) != 0);
        Assert.True((alarms.RackFaultSummary1 & (1 << 12)) != 0);
    }

    [Fact]
    public void EvaluateCluster_PackVoltageImbalanceUsesTotalVoltageDifferenceThreshold()
    {
        var alarms = new ClusterAlarms();
        var thresholds = new ClusterThresholds();
        var clusterState = CreateClusterState(current: 0);
        clusterState.VoltageImbalance = thresholds.TotalVoltageDifferenceThreshold3!.Value + 1;

        BmsRackProtection.EvaluateCluster(
            clusterState, packCount: 1, cellsPerPack: 1, thresholds, alarms, insulationValue: 1000f);

        Assert.True(alarms.BatteryBoxVoltageExtremaDifferenceFault);
    }

    [Fact]
    public void EvaluateCluster_DischargeHighTempUsesCellTemp()
    {
        var alarms = new ClusterAlarms();
        var thresholds = new ClusterThresholds();
        // 放电高温三级 65°C：电芯温度超限 → 触发放电高温故障（判据为电芯温度）
        var clusterState = CreateClusterState(current: -50);
        clusterState.PackStates[0].MaxCellTemp = thresholds.DischargeHighTempThreshold3!.Value + 1;

        BmsRackProtection.EvaluateCluster(
            clusterState, packCount: 1, cellsPerPack: 1, thresholds, alarms, insulationValue: 1000f);

        Assert.True(alarms.CellDischargeHighTempFault);
        Assert.False(alarms.CellDischargeHighTempProtection);
    }

    [Fact]
    public void EvaluateCluster_ChargeHighTempUsesCellTemp()
    {
        var alarms = new ClusterAlarms();
        var thresholds = new ClusterThresholds();
        var clusterState = CreateClusterState(current: 50);
        clusterState.PackStates[0].MaxCellTemp = thresholds.ChargeHighTempThreshold3!.Value + 1;

        BmsRackProtection.EvaluateCluster(
            clusterState, packCount: 1, cellsPerPack: 1, thresholds, alarms, insulationValue: 1000f);

        Assert.True(alarms.CellChargeHighTempFault);
    }

    [Fact]
    public void EvaluateCluster_DischargeLowTempUsesCellTemp()
    {
        var alarms = new ClusterAlarms();
        var thresholds = new ClusterThresholds();
        var clusterState = CreateClusterState(current: -50);
        clusterState.PackStates[0].MinCellTemp = thresholds.DischargeLowTempThreshold3!.Value - 1;

        BmsRackProtection.EvaluateCluster(
            clusterState, packCount: 1, cellsPerPack: 1, thresholds, alarms, insulationValue: 1000f);

        Assert.True(alarms.CellDischargeLowTempFault);
    }

    [Fact]
    public void EvaluateCluster_ChargeLowTempUsesCellTemp()
    {
        var alarms = new ClusterAlarms();
        var thresholds = new ClusterThresholds();
        var clusterState = CreateClusterState(current: 50);
        clusterState.PackStates[0].MinCellTemp = thresholds.ChargeLowTempThreshold3!.Value - 1;

        BmsRackProtection.EvaluateCluster(
            clusterState, packCount: 1, cellsPerPack: 1, thresholds, alarms, insulationValue: 1000f);

        Assert.True(alarms.CellChargeLowTempFault);
    }

    [Fact]
    public void EvaluateCluster_NormalCellTempDoesNotTriggerHighTemp()
    {
        var alarms = new ClusterAlarms();
        var thresholds = new ClusterThresholds();
        var clusterState = CreateClusterState(current: -50);

        // 电芯温度 25°C（正常）→ 不触发高温
        BmsRackProtection.EvaluateCluster(
            clusterState, packCount: 1, cellsPerPack: 1, thresholds, alarms, insulationValue: 1000f);

        Assert.False(alarms.CellDischargeHighTempFault);
        Assert.False(alarms.CellDischargeHighTempAlarm);
    }

    private static ClusterState CreateClusterState(
        double minPackSoc = 0.5,
        double totalVoltage = 1300,
        double current = 0)
    {
        return new ClusterState
        {
            MinPackSOC = minPackSoc,
            MaxPackSOC = minPackSoc,
            TotalVoltage = totalVoltage,
            TotalCurrent = current,
            PackStates =
            [
                new PackState
                {
                    MinCellVoltage = 3.2,
                    MaxCellVoltage = 3.2,
                    MinCellTemp = 25,
                    MaxCellTemp = 25,
                    CellStates = [new CellState { Voltage = 3.2, Temperature = 25 }]
                }
            ]
        };
    }
}
