using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssSimModelApi;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;
using EssSimulator.EssSimModelApi.Mappers;
using PcsDeviceConfig = EssSimulator.Configuration.PcsDeviceConfig;

namespace EssSimulator.Tests.Emu;

/// <summary>
/// EMU 设备树分层：协议镜像构建（扁平/分组）与组级聚合遥测。
/// 组内 PcsList 与扁平 PcsList 共享同一 PcsData 实例引用，两条路径读写同一对象。
/// </summary>
public class EmuGroupMirrorTests
{
    private static EssUnitConfig GroupedUnit() => new()
    {
        Name = "EMU-1",
        Groups =
        {
            new EmuGroupConfig
            {
                Name = "G1",
                Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig() },
                BreakerName = "组断路器-1",
                MeterNames = { "组电表-1", "组电表-2" }
            },
            new EmuGroupConfig
            {
                Name = "G2",
                Pcs = { new PcsDeviceConfig() }
            }
        }
    };

    [Fact]
    public void BuildEmuMirror_GroupedUnit_SharesPcsReferencesAcrossViews()
    {
        var emu = PcsDataServer.BuildEmuMirror(GroupedUnit(), new PcsPhysicalConfig());

        // 扁平视图按组顺序展平
        Assert.Equal(3, emu.PcsList.Count);
        Assert.Equal(new[] { 1, 2, 3 }, emu.PcsList.Select(p => p.PcsId).ToArray());

        // 分组视图：组名、组内台数与扁平视图共享同一实例引用
        Assert.Equal(2, emu.Groups.Count);
        Assert.Equal("G1", emu.Groups[0].Name);
        Assert.Equal("G2", emu.Groups[1].Name);
        Assert.Equal(2, emu.Groups[0].PcsList.Count);
        Assert.Single(emu.Groups[1].PcsList);
        Assert.Same(emu.PcsList[0], emu.Groups[0].PcsList[0]);
        Assert.Same(emu.PcsList[1], emu.Groups[0].PcsList[1]);
        Assert.Same(emu.PcsList[2], emu.Groups[1].PcsList[0]);

        // 组级断路器镜像：仅绑定 BreakerName 的组建镜像，恒合闸
        Assert.NotNull(emu.Groups[0].Breaker);
        Assert.Equal(1, emu.Groups[0].Breaker!.Closed);
        Assert.Null(emu.Groups[1].Breaker);

        // 组级电表镜像：按 MeterNames 台数建镜像，未绑定组为空
        Assert.Equal(2, emu.Groups[0].Meters.Count);
        Assert.Empty(emu.Groups[1].Meters);

        // 单元变镜像固定 1 台（对应电气层单元变，本期仅 k=0）
        Assert.Single(emu.Transformers);
    }

    [Fact]
    public void BuildEmuMirror_FlatUnit_KeepsFlatViewAndTransformer()
    {
        var unit = new EssUnitConfig
        {
            Name = "EMU-1",
            Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig() }
        };
        var emu = PcsDataServer.BuildEmuMirror(unit, new PcsPhysicalConfig());

        Assert.Equal(2, emu.PcsList.Count);
        Assert.Single(emu.Groups);
        Assert.Same(emu.PcsList[0], emu.Groups[0].PcsList[0]);
        Assert.Same(emu.PcsList[1], emu.Groups[0].PcsList[1]);
        Assert.Single(emu.Transformers);
    }

    [Fact]
    public void MapGroupState_AggregatesPerGroupPcs()
    {
        var emu = PcsDataServer.BuildEmuMirror(GroupedUnit(), new PcsPhysicalConfig());

        // G1：1 台放电 + 1 台故障
        emu.PcsList[0].ActivePower = 100f;
        emu.PcsList[0].ReactivePower = 30f;
        emu.PcsList[0].SimulatorMode = OperationMode.Normal;
        emu.PcsList[1].ActivePower = -50f;
        emu.PcsList[1].ReactivePower = -10f;
        emu.PcsList[1].SimulatorMode = OperationMode.Normal;
        emu.PcsList[1].OperationStatus = 6;
        // G2：1 台停机
        emu.PcsList[2].ActivePower = 0f;
        emu.PcsList[2].SimulatorMode = OperationMode.Off;

        PcsMapper.MapGroupState(emu);

        var g1 = emu.Groups[0];
        Assert.Equal(50f, g1.TotalActivePower);
        Assert.Equal(20f, g1.TotalReactivePower);
        Assert.Equal(2, g1.TotalPcsCount);
        Assert.Equal(2, g1.OnlinePcsCount);
        Assert.Equal(1, g1.FaultPcsCount);
        Assert.Equal(0, g1.AlarmPcsCount);
        Assert.Equal(1, g1.Breaker!.Closed);

        var g2 = emu.Groups[1];
        Assert.Equal(0f, g2.TotalActivePower);
        Assert.Equal(1, g2.TotalPcsCount);
        Assert.Equal(0, g2.OnlinePcsCount);
        Assert.Equal(0, g2.FaultPcsCount);
    }

    [Fact]
    public void MapGroupState_CountsAlarmOnlyWhenNotFaulted()
    {
        var emu = PcsDataServer.BuildEmuMirror(GroupedUnit(), new PcsPhysicalConfig());
        emu.PcsList[0].SimulatorMode = OperationMode.Normal;
        emu.PcsList[0].InsulationAlarm = true; // 告警（非故障，置位 AlarmSummary1 位 0）
        emu.PcsList[1].SimulatorMode = OperationMode.Normal;
        emu.PcsList[1].OperationStatus = 6; // 故障优先于告警计数

        PcsMapper.MapGroupState(emu);

        Assert.Equal(1, emu.Groups[0].AlarmPcsCount);
        Assert.Equal(1, emu.Groups[0].FaultPcsCount);
    }

    [Fact]
    public void MapGroupState_FillsGroupMeters_FromGroupPcs()
    {
        var emu = PcsDataServer.BuildEmuMirror(GroupedUnit(), new PcsPhysicalConfig());
        emu.PcsList[0].LineVoltageAB = 690f;
        emu.PcsList[0].Frequency = 50f;
        emu.PcsList[0].PhaseACurrent = 100f;
        emu.PcsList[0].ActivePower = 100f;
        emu.PcsList[1].LineVoltageAB = 690f;
        emu.PcsList[1].Frequency = 50f;
        emu.PcsList[1].PhaseACurrent = 90f;
        emu.PcsList[1].ActivePower = 90f;

        PcsMapper.MapGroupState(emu);

        // G1 两台电表同母线：数值相同，电流按组内 PCS 求和、功率取组聚合
        foreach (var meter in emu.Groups[0].Meters)
        {
            Assert.Equal(690f, meter.LineVoltageAB, 0.1f);
            Assert.Equal(190f, meter.PhaseACurrent);
            Assert.Equal(190f, meter.TotalActivePower);
            Assert.Equal(50f, meter.Frequency);
        }
        // G2 无电表镜像：不刷新、不抛异常
        Assert.Empty(emu.Groups[1].Meters);
    }

    [Fact]
    public void MapGroupState_FlatUnit_IsNoOp()
    {
        var emu = new EnergyManagementData();
        emu.PcsList.Add(new PcsData { PcsId = 1, ActivePower = 10f });

        PcsMapper.MapGroupState(emu); // 无 Groups：空操作，不抛异常

        Assert.Empty(emu.Groups);
    }
}
