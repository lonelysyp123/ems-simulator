using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;
using EssSimulator.EssSimModelApi.Mappers;
using Xunit;

namespace EssSimulator.Tests.Devices;

public class PcsDisplayLabelsTests
{
    [Theory]
    [InlineData(OperationMode.Off, false, 0, false, 0, 1)]
    [InlineData(OperationMode.Off, true, 0, false, 3, 1)]
    [InlineData(OperationMode.Normal, false, 0, false, 0, 1)]
    [InlineData(OperationMode.Standby, true, 0, false, 0, 2)]
    [InlineData(OperationMode.Normal, true, 100, false, 0, 5)]
    [InlineData(OperationMode.Normal, true, -100, false, 0, 4)]
    [InlineData(OperationMode.Normal, true, 0, false, 0, 2)]
    [InlineData(OperationMode.Normal, true, 0, true, 0, 2)]
    [InlineData(OperationMode.Normal, true, 0, false, 1, 6)]
    public void ToOperationStatusCode_UsesProjectOperationStatusCodes(
        OperationMode mode, bool run, double powerKw, bool blackStart, ushort fault, int expected)
    {
        int code = PcsDisplayLabels.ToOperationStatusCode(mode, run, powerKw, blackStart, fault);
        Assert.Equal(expected, code);
    }

    [Fact]
    public void ToOperationStatusCode_FromState_MatchesGuiStopWithCommand()
    {
        var state = new PcsState
        {
            Mode = OperationMode.Off,
            FaultType = 2,
            ActivePower = 0
        };

        int code = PcsDisplayLabels.ToOperationStatusCode(state, externalRunCommand: true);
        Assert.Equal(1, code);
    }

    [Fact]
    public void MapPcsState_SyncsOperationStatusFromPhysicalState()
    {
        var src = new PcsState
        {
            Mode = OperationMode.Off,
            FaultType = 2,
            ActivePower = 0
        };
        var dst = new PcsData { pcsOnOffSwitch = true };

        PcsMapper.MapPcsState(src, dst, null!);

        Assert.Equal(1, dst.OperationStatus);
    }

    [Fact]
    public void MapPcsState_BatteryPower_IsKilowatts()
    {
        var src = new PcsState
        {
            DcVoltage = 1350,
            DcCurrent = 40,
            ActivePower = 50
        };
        var dst = new PcsData();

        PcsMapper.MapPcsState(src, dst, null!);

        Assert.Equal(54f, dst.BatteryPower, precision: 3);
    }

    [Fact]
    public void MapEmuState_SumsPcsActiveAndReactivePower()
    {
        var emu = new EnergyManagementData();
        emu.PcsList.Add(new PcsData { ActivePower = 100f, ReactivePower = 20f, SimulatorMode = OperationMode.Normal });
        emu.PcsList.Add(new PcsData { ActivePower = 50f, ReactivePower = -10f, SimulatorMode = OperationMode.Normal });

        PcsMapper.MapEmuState(emu, Array.Empty<BatteryRackSimulator>());

        Assert.Equal(150f, emu.Emu.OutputActivePower);
        Assert.Equal(10f, emu.Emu.OutputReactivePower);
        Assert.Equal(5, emu.Emu.OperationStatus);
    }

    [Fact]
    public void MapElectricityMeterState_FollowsPcsBusbarAndEmuAggregates()
    {
        var emu = new EnergyManagementData();
        emu.PcsList.Add(new PcsData { LineVoltageAB = 690f, Frequency = 50f, PhaseACurrent = 100f, PhaseBCurrent = 101f, PhaseCCurrent = 99f });
        emu.PcsList.Add(new PcsData { LineVoltageAB = 692f, Frequency = 50f, PhaseACurrent = 90f, PhaseBCurrent = 91f, PhaseCCurrent = 89f });
        emu.Emu.OutputActivePower = 1200f;
        emu.Emu.OutputReactivePower = 900f;

        PcsMapper.MapElectricityMeterState(emu);

        var meter = emu.ElectricityMeter;
        Assert.Equal(691f, meter.LineVoltageAB, 0.1f);   // 母线线电压取 PCS 均值
        Assert.Equal(691f, meter.LineVoltageCA, 0.1f);
        Assert.Equal(691f / MathF.Sqrt(3), meter.PhaseAVoltage, 0.1f);
        Assert.Equal(50f, meter.Frequency);
        Assert.Equal(190f, meter.PhaseACurrent);          // 相电流按 PCS 求和
        Assert.Equal(1200f, meter.TotalActivePower);      // 功率取 EMU 聚合值
        Assert.Equal(1500f, meter.TotalApparentPower, 0.1f);
        Assert.Equal(0.8f, meter.PowerFactor, 0.001f);
    }

    [Fact]
    public void BreakerMirror_ClosedAaEe_FollowsClosedState()
    {
        var breaker = new BreakerMirrorData();
        Assert.Equal(0xAA, breaker.ClosedAaEe);   // 缺省合闸 → AA 动作
        breaker.Closed = 0;
        Assert.Equal(0xEE, breaker.ClosedAaEe);   // 分闸 → EE 复归
    }

    [Fact]
    public void WithdrawExternalRunCommand_ClearsCommandWithoutEmsStopSideEffects()
    {
        var pcs = CreateTestPcs();
        pcs.SyncExternalRunCommand(true);
        Assert.True(pcs.IsExternalRunCommand);

        pcs.WithdrawExternalRunCommand();

        Assert.False(pcs.IsExternalRunCommand);
    }

    [Fact]
    public void SyncRunCommandFeedback_ClearsDtoWhenDeviceWithdrewCommand()
    {
        var pcs = CreateTestPcs();
        pcs.SyncExternalRunCommand(true);
        pcs.WithdrawExternalRunCommand();
        var dto = new PcsData { pcsOnOffSwitch = true };

        PcsMapper.SyncRunCommandFeedback(pcs, dto);

        Assert.False(dto.pcsOnOffSwitch);
    }

    [Fact]
    public void FaultTrip_WithdrawsRunCommand()
    {
        var pcs = CreateTestPcs();
        pcs.SyncExternalRunCommand(true);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.GetCurrentState().FaultType = 3;
        pcs.GetCurrentState().FaultMessage = "test fault";

        pcs.Update(1300, isBmsFault: 0, DateTime.UtcNow, TimeSpan.FromMilliseconds(200));

        Assert.Equal(OperationMode.Off, pcs.GetCurrentState().Mode);
        Assert.False(pcs.IsExternalRunCommand);
    }

    private static PcsDevice CreateTestPcs() =>
        PcsDeviceFactory.Create("pcs_test", new PcsDeviceConfig
        {
            AcNominalLineVoltageV = 690,
            FrequencyHz = 50,
            DcVoltageRangeMinV = 1000,
            DcVoltageRangeMaxV = 1500,
            MaxCurrentA = 1100,
            RatedPowerKw = 1250,
            MaxPowerKw = 1250,
            Efficiency = 0.99,
            GridLossCoefficient = 0.11,
            RampSlope = 500,
            RampIntervalMs = 100,
            RampDelayMs = 0,
        });
}
