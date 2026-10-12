using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;
using EssSimulator.EssSimModelApi.Mappers;
using PcsDeviceConfig = EssSimulator.Configuration.PcsDeviceConfig;

namespace EssSimulator.Tests.Emu;

/// <summary>
/// EMU 虚拟模型：系统级目标 P/Q 按所属 PCS 台数简单均分，
/// syst6 系统操作与 syst7 黑启动批量写入语义。
/// </summary>
public class EmuVirtualModelTests : SimulatorHostTestBase
{
    private static EnergyManagementData BuildEmu(int pcsCount)
    {
        var emu = new EnergyManagementData();
        for (int i = 0; i < pcsCount; i++)
            emu.PcsList.Add(new PcsData { PcsId = i + 1 });
        return emu;
    }

    [Fact]
    public void Dispatch_RemoteEnabled_SplitsTargetEvenly_TwoPcs()
    {
        var emu = BuildEmu(2);
        emu.Emu.RemoteControlEnable = 1;
        emu.Emu.RemoteControlMode = 1;
        emu.Emu.TargetActivePower = 1000;
        emu.Emu.TargetReactivePower = 200;

        EmuPowerDispatcher.Dispatch(emu);

        Assert.All(emu.PcsList, p =>
        {
            Assert.Equal(500, p.PCSActivePowerSetting);
            Assert.Equal(100, p.PCSReactivePowerSetting);
        });
    }

    [Fact]
    public void Dispatch_RemoteEnabled_SplitsTargetEvenly_FourPcs()
    {
        var emu = BuildEmu(4);
        emu.Emu.RemoteControlEnable = 1;
        emu.Emu.RemoteControlMode = 1;
        emu.Emu.TargetActivePower = 1000;
        emu.Emu.TargetReactivePower = -400;

        EmuPowerDispatcher.Dispatch(emu);

        Assert.Equal(4, emu.PcsList.Count);
        Assert.All(emu.PcsList, p =>
        {
            Assert.Equal(250, p.PCSActivePowerSetting);
            Assert.Equal(-100, p.PCSReactivePowerSetting);
        });
    }

    [Fact]
    public void Dispatch_LocalMode_KeepsPerPcsDirectSettings()
    {
        var emu = BuildEmu(2);
        emu.Emu.RemoteControlEnable = 1;
        emu.Emu.RemoteControlMode = 0; // 本地模式
        emu.Emu.TargetActivePower = 1000;
        emu.PcsList[0].PCSActivePowerSetting = 123;
        emu.PcsList[1].PCSActivePowerSetting = 456;

        EmuPowerDispatcher.Dispatch(emu);

        Assert.Equal(123, emu.PcsList[0].PCSActivePowerSetting);
        Assert.Equal(456, emu.PcsList[1].PCSActivePowerSetting);
    }

    [Fact]
    public void Dispatch_RemoteNotEnabled_KeepsPerPcsDirectSettings()
    {
        var emu = BuildEmu(2);
        emu.Emu.RemoteControlEnable = 0;
        emu.Emu.RemoteControlMode = 1;
        emu.Emu.TargetActivePower = 1000;
        emu.PcsList[0].PCSActivePowerSetting = 111;

        EmuPowerDispatcher.Dispatch(emu);

        Assert.Equal(111, emu.PcsList[0].PCSActivePowerSetting);
        Assert.False(EmuPowerDispatcher.IsRemoteDispatchActive(emu));
    }

    [Fact]
    public void SystemOperation_Start_StartsAllPcs()
    {
        var emu = BuildEmu(3);
        emu.Emu.SystemOperation = EmuSystemOperationApplier.OpStart;

        EmuSystemOperationApplier.Apply(emu);

        Assert.All(emu.PcsList, p => Assert.True(p.pcsOnOffSwitch));
    }

    [Fact]
    public void SystemOperation_Stop_StopsAllPcs()
    {
        var emu = BuildEmu(3);
        Assert.All(emu.PcsList, p => p.pcsOnOffSwitch = true);
        emu.Emu.SystemOperation = EmuSystemOperationApplier.OpStop;

        EmuSystemOperationApplier.Apply(emu);

        Assert.All(emu.PcsList, p => Assert.False(p.pcsOnOffSwitch));
    }

    [Fact]
    public void SystemOperation_Standby_KeepsRunningButClearsTargets()
    {
        var emu = BuildEmu(2);
        Assert.All(emu.PcsList, p => p.pcsOnOffSwitch = true);
        emu.Emu.TargetActivePower = 800;
        emu.Emu.TargetReactivePower = 100;
        Assert.All(emu.PcsList, p =>
        {
            p.PCSActivePowerSetting = 400;
            p.PCSReactivePowerSetting = 50;
        });
        emu.Emu.SystemOperation = EmuSystemOperationApplier.OpStandby;

        EmuSystemOperationApplier.Apply(emu);

        Assert.All(emu.PcsList, p => Assert.True(p.pcsOnOffSwitch));
        Assert.Equal(0, emu.Emu.TargetActivePower);
        Assert.Equal(0, emu.Emu.TargetReactivePower);
        Assert.All(emu.PcsList, p =>
        {
            Assert.Equal(0, p.PCSActivePowerSetting);
            Assert.Equal(0, p.PCSReactivePowerSetting);
        });
    }

    [Fact]
    public void SystemOperation_Reset_StopsAllPcs()
    {
        // 启停写 0 由 ApplyEmuCommands → SyncExternalRunCommand(false) 清除故障锁存
        var emu = BuildEmu(2);
        Assert.All(emu.PcsList, p => p.pcsOnOffSwitch = true);
        emu.Emu.SystemOperation = EmuSystemOperationApplier.OpReset;

        EmuSystemOperationApplier.Apply(emu);

        Assert.All(emu.PcsList, p => Assert.False(p.pcsOnOffSwitch));
    }

    [Fact]
    public void SystemOperation_SameCodeTwice_OnlyAppliesOnce()
    {
        var emu = BuildEmu(2);
        emu.Emu.SystemOperation = EmuSystemOperationApplier.OpStandby;
        emu.Emu.TargetActivePower = 500;
        EmuSystemOperationApplier.Apply(emu); // 首次：目标清零
        Assert.Equal(0, emu.Emu.TargetActivePower);

        emu.Emu.TargetActivePower = 500; // 边沿未变化，不应再次清零
        EmuSystemOperationApplier.Apply(emu);

        Assert.Equal(500, emu.Emu.TargetActivePower);
    }

    [Fact]
    public void BlackStartWrite_EdgeApplied_ToAllPcs()
    {
        var emu = BuildEmu(3);
        emu.Emu.BlackStartModeWrite = 1;
        EmuSystemOperationApplier.Apply(emu);
        Assert.All(emu.PcsList, p => Assert.True(p.BlackStartEnabled));

        emu.Emu.BlackStartModeWrite = 0;
        EmuSystemOperationApplier.Apply(emu);
        Assert.All(emu.PcsList, p => Assert.False(p.BlackStartEnabled));
    }
}

/// <summary>EMU 拓扑：每单元 N 台 PCS 的配置展开、通道映射与电气网络构建。</summary>
public class PcsPerUnitTopologyTests
{
    [Fact]
    public void Config_UnitCount_SumsPcsPerUnit()
    {
        var cfg = new SimulatorConfig
        {
            Devices =
            {
                new EssUnitConfig { Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig() } },
                new EssUnitConfig
                {
                    Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig(), new PcsDeviceConfig(), new PcsDeviceConfig() }
                }
            }
        };

        Assert.Equal(6, cfg.UnitCount);
        Assert.Equal(new[] { 2, 4 }, cfg.GetPcsCountsPerUnit());
        Assert.Equal(6, cfg.GetPcsDeviceConfigs().Count);
        Assert.Equal(6, cfg.GetBmsDeviceConfigs().Count);
    }

    [Fact]
    public void Config_EmptyPcsDefaultsToTwo()
    {
        var cfg = new SimulatorConfig
        {
            Devices = { new EssUnitConfig(), new EssUnitConfig() }
        };

        Assert.Equal(4, cfg.UnitCount);
        Assert.Equal(new[] { 2, 2 }, cfg.GetPcsCountsPerUnit());
    }

    [Fact]
    public void Layout_UnitAndSlotMapping()
    {
        IReadOnlyList<int> layout = new[] { 2, 4 };

        Assert.Equal(0, PcsUnitLayout.BaseIndexOfUnit(layout, 0));
        Assert.Equal(2, PcsUnitLayout.BaseIndexOfUnit(layout, 1));
        Assert.Equal(4, PcsUnitLayout.CountOfUnit(layout, 1));

        Assert.Equal(0, PcsUnitLayout.UnitIndexOf(layout, 0));
        Assert.Equal(0, PcsUnitLayout.UnitIndexOf(layout, 1));
        Assert.Equal(1, PcsUnitLayout.UnitIndexOf(layout, 2));
        Assert.Equal(1, PcsUnitLayout.UnitIndexOf(layout, 5));
        Assert.Equal(3, PcsUnitLayout.SlotOfChannel(layout, 5));

        // 布局缺失回退每单元 2 台（默认回归路径）
        Assert.Equal(1, PcsUnitLayout.UnitIndexOf(null, 3));
        Assert.Equal(2, PcsUnitLayout.BaseIndexOfUnit(null, 1));
    }

    [Fact]
    public void EnergyStorageSystem_BuildsChannelsPerUnitLayout()
    {
        var cfg = new SimulatorConfig
        {
            Devices =
            {
                new EssUnitConfig { Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig() } },
                new EssUnitConfig
                {
                    Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig(), new PcsDeviceConfig() }
                }
            }
        };

        using var ess = new EnergyStorageSystem(
            cfg,
            new PcsPhysicalConfig { AcVoltageNominal = 690 },
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            new PccConfig(),
            new MeterConfig());

        Assert.Equal(5, ess._pcsList.Count);
        Assert.Equal(5, ess._batteryRacks.Count);
        Assert.Equal(new[] { 2, 3 }, ess.PcsPerUnit);

        Assert.Equal(0, ess.UnitIndexOfPcs(0));
        Assert.Equal(0, ess.UnitIndexOfPcs(1));
        Assert.Equal(1, ess.UnitIndexOfPcs(2));
        Assert.Equal(1, ess.UnitIndexOfPcs(4));
        Assert.Equal(2, ess.PcsBaseIndexOfUnit(1));
        Assert.Equal(3, ess.PcsCountOfUnit(1));

        // 电气网络与直流链路同步 N 化
        Assert.Equal(5, ess.ElectricalNetwork.PcsDevices.Count);
        Assert.Equal(5, ess.ElectricalNetwork.DcLinks.Count);
        Assert.Equal(2, ess.ElectricalNetwork.UnitBreakers.Count);
    }
}

/// <summary>PcsData 通用化：自定义告警字框架与扩展区。</summary>
public class PcsDataGenericExtensionTests
{
    [Fact]
    public void CustomAlarmWords_SetAndClearBits()
    {
        var pcs = new PcsData();

        pcs.SetAlarmBit(7, 0, true);
        pcs.SetAlarmBit(7, 3, true);
        Assert.Equal(0b1001, pcs.GetAlarmWord(7));

        pcs.SetAlarmBit(7, 0, false);
        Assert.Equal(0b1000, pcs.GetAlarmWord(7));

        // 未写入的字读 0
        Assert.Equal(0, pcs.GetAlarmWord(15));
    }

    [Fact]
    public void CustomAlarmWords_RejectsReservedWordsAndInvalidBits()
    {
        var pcs = new PcsData();

        pcs.SetAlarmBit(0, 0, true);  // word0~6 保留给 AlarmSummary1~7
        pcs.SetAlarmBit(7, 16, true); // 位越界
        Assert.Empty(pcs.CustomAlarmWords);
    }

    [Fact]
    public void GetAlarmWord_RoutesWordsZeroToSix_ToAlarmSummaries()
    {
        var pcs = new PcsData { InsulationAlarm = true }; // AlarmSummary1 bit0

        Assert.Equal(pcs.AlarmSummary1, pcs.GetAlarmWord(0));
        Assert.Equal(pcs.AlarmSummary2, pcs.GetAlarmWord(1));
        Assert.Equal(pcs.AlarmSummary7, pcs.GetAlarmWord(6));
        Assert.True(pcs.GetAlarmWord(0) != 0);
    }

    [Fact]
    public void ExtraExtensionAreas_AreIndexableAndWritable()
    {
        var pcs = new PcsData();

        pcs.ExtraAnalogValues[5] = 123.4f;
        pcs.ExtraDigitalValues[9] = true;

        Assert.Equal(123.4f, pcs.ExtraAnalogValues[5]);
        Assert.True(pcs.ExtraDigitalValues[9]);
        Assert.Equal(16, pcs.ExtraAnalogValues.Length);
        Assert.Equal(16, pcs.ExtraDigitalValues.Length);
    }
}
