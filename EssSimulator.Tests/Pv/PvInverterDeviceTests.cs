using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Pv;

namespace EssSimulator.Tests.Pv;

public class PvInverterDeviceTests
{
    private static PvInverterDevice CreateRunning()
    {
        var inv = PvInverterDevice.Create320kW("pv_inv1");
        inv.SyncExternalRunCommand(true);
        inv.TransitionToMode(OperationMode.Normal);
        inv.UpdateGridState(690, 50, isUtilityGridAvailable: true);
        return inv;
    }

    [Fact]
    public void Factory_WiresThirtyModulesTimesSixteenStrings()
    {
        var inv = PvInverterDevice.Create320kW("pv_inv1");
        Assert.Equal(16, inv.StringCount);
        Assert.Equal(30, inv.ModulesPerString);
        Assert.Equal(320, inv.RatedPowerKw);
        Assert.Equal(16 * 30, inv.TotalModuleCount);
        Assert.Equal(690, new PvInverterConfig().AcNominalLineVoltageV);
        Assert.Equal(50, new PvInverterConfig().FrequencyHz);
        Assert.Equal(0.01, new PvInverterConfig().GridLossCoefficient);
    }

    [Fact]
    public void Stc_ClipsToRatedAcPower()
    {
        var inv = CreateRunning();
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(5));

        var st = inv.GetCurrentState();
        Assert.InRange(st.ActivePower, 318, 322);
        Assert.True(st.ActivePower >= 0);
        Assert.InRange(st.DcVoltage, 1100, 1400);
        Assert.Equal(16, inv.StringCurrentsA.Count);
        Assert.True(inv.AvailableDcPowerKw > st.ActivePower);
    }

    [Fact]
    public void SetPowerCommand_RejectsChargeAndCurtails()
    {
        var inv = CreateRunning();
        inv.SetPowerCommand(-200, 0);
        for (int i = 0; i < 40; i++)
            inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromMilliseconds(100));

        Assert.InRange(inv.GetCurrentState().ActivePower, -0.05, 0.05);

        inv.SetPowerCommand(160, 0);
        for (int i = 0; i < 40; i++)
            inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromMilliseconds(100));

        Assert.InRange(inv.GetCurrentState().ActivePower, 158, 162);
        Assert.True(inv.GetCurrentState().ActivePower >= 0);
    }

    [Fact]
    public void NightOrOff_OutputsZeroActivePower()
    {
        var inv = CreateRunning();
        inv.Update(0, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));
        Assert.Equal(0, inv.GetCurrentState().ActivePower);

        inv = CreateRunning();
        inv.SyncExternalRunCommand(false);
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));
        Assert.Equal(OperationMode.Off, inv.GetCurrentState().Mode);
        Assert.Equal(0, inv.GetCurrentState().ActivePower);
    }

    [Fact]
    public void HalfIrradiance_BelowRated_FollowsArray()
    {
        var inv = CreateRunning();
        inv.Update(500, 25, DateTime.UtcNow, TimeSpan.FromSeconds(5));
        var st = inv.GetCurrentState();
        Assert.True(st.ActivePower < 250);
        Assert.True(st.ActivePower > 100);
        Assert.True(st.ActivePower >= 0);
    }

    [Fact]
    public void ExtremeCold_DeratesBelowRatedUnlikeMildCold()
    {
        var inv = CreateRunning();
        inv.Update(1000, 0, DateTime.UtcNow, TimeSpan.FromSeconds(5));
        double mildCold = inv.GetCurrentState().ActivePower;
        Assert.InRange(mildCold, 318, 322);

        inv = CreateRunning();
        inv.Update(1000, -40, DateTime.UtcNow, TimeSpan.FromSeconds(5));
        Assert.True(inv.GetCurrentState().ActivePower < 50);
        Assert.True(inv.AvailableDcPowerKw < 50);
    }

    [Fact]
    public void ColdDerate_AtMinus25_IsAboutHalfOfFullAvailable()
    {
        var inv = CreateRunning();
        inv.Update(1000, -25, DateTime.UtcNow, TimeSpan.FromSeconds(5));
        Assert.InRange(inv.GetCurrentState().ActivePower, 140, 230);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(24)]
    public void Mppt_CoversEveryConfiguredStringAndConservesPower(int stringCount)
    {
        var inv = new PvInverterDevice("pv_inv1", new PvInverterConfig { StringCount = stringCount });
        inv.SyncExternalRunCommand(true);
        inv.UpdateGridState(690, 50, true);
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));
        var pvString = PvStringSimulator.CreateDefault();

        Assert.Equal(6, inv.MpptVoltageV.Count);
        Assert.Equal(6, inv.MpptCurrentA.Count);
        for (int m = 0; m < 6; m++)
        {
            int n = stringCount / 6 + (m < stringCount % 6 ? 1 : 0);
            if (n == 0)
            {
                Assert.Equal(0, inv.MpptVoltageV[m]);
                Assert.Equal(0, inv.MpptCurrentA[m]);
            }
            else
            {
                Assert.Equal(n * inv.StringCurrentsA[0], inv.MpptCurrentA[m], 6);
                Assert.Equal(n * pvString.CurrentAtVoltage(inv.MpptVoltageV[m], 1000, 25), inv.MpptCurrentA[m], 6);
            }
        }
        Assert.Equal(inv.StringCurrentsA.Sum(), inv.MpptCurrentA.Sum(), 6);
        AssertDcPowerBalance(inv);
    }

    [Fact]
    public void Curtailment_MovesAlongIvCurveInsteadOfReportingAvailableCurrent()
    {
        var inv = CreateRunning();
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));
        double fullVoltage = inv.MpptVoltageV[0];
        double fullCurrent = inv.MpptCurrentA[0];
        double available = inv.AvailableDcPowerKw;
        inv.SetPowerCommand(80, 0);
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));

        Assert.Equal(80, inv.GetCurrentState().ActivePower, 6);
        Assert.Equal(available, inv.AvailableDcPowerKw, 6);
        Assert.True(inv.MpptVoltageV[0] > fullVoltage);
        Assert.True(inv.MpptCurrentA[0] < fullCurrent);
        var pvString = PvStringSimulator.CreateDefault();
        Assert.Equal(3 * pvString.CurrentAtVoltage(inv.MpptVoltageV[0], 1000, 25), inv.MpptCurrentA[0], 6);
        AssertDcPowerBalance(inv);
    }

    [Fact]
    public void StoppedMppt_ShowsOpenCircuitVoltageAndZeroCurrent()
    {
        var inv = CreateRunning();
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));
        inv.SyncExternalRunCommand(false);
        Assert.All(inv.MpptCurrentA, current => Assert.Equal(0, current));
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));

        double voc = PvStringSimulator.CreateDefault().Evaluate(1000, 25).VocV;
        Assert.All(inv.MpptVoltageV, voltage => Assert.Equal(voc, voltage, 6));
        Assert.All(inv.MpptCurrentA, current => Assert.Equal(0, current));
        Assert.All(inv.StringCurrentsA, current => Assert.Equal(0, current));
        Assert.Equal(voc, inv.GetCurrentState().DcVoltage, 6);
        AssertDcPowerBalance(inv);
    }

    [Theory]
    [InlineData(6, "直流欠压")]
    [InlineData(40, "直流过压")]
    public void DcVoltageInhibit_ReportsMeasuredVoltageWithoutDrawingCurrent(int modules, string reason)
    {
        var inv = new PvInverterDevice("pv_inv1", new PvInverterConfig { ModulesPerString = modules });
        inv.SyncExternalRunCommand(true);
        inv.UpdateGridState(690, 50, true);
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));

        Assert.Equal(reason, inv.LimitReason);
        Assert.Equal(0, inv.AvailableDcPowerKw);
        Assert.Equal(0, inv.GetCurrentState().ActivePower);
        Assert.All(inv.MpptCurrentA, current => Assert.Equal(0, current));
        Assert.All(inv.MpptVoltageV, voltage => Assert.True(voltage > 0));
        AssertDcPowerBalance(inv);
    }

    [Fact]
    public void MinimumTrackingVoltage_ConstrainsAvailablePowerAndActualOperatingPoint()
    {
        var inv = new PvInverterDevice("pv_inv1", new PvInverterConfig
        {
            RatedPowerKw = 1000,
            MaxPowerKw = 1000,
            DcVoltageRangeMinV = 1400
        });
        inv.SyncExternalRunCommand(true);
        inv.UpdateGridState(690, 50, true);
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(5));

        var pvString = PvStringSimulator.CreateDefault();
        double expectedW = 16 * 1400 * pvString.CurrentAtVoltage(1400, 1000, 25);
        Assert.Equal(expectedW / 1000, inv.AvailableDcPowerKw, 6);
        Assert.Equal(1400, inv.GetCurrentState().DcVoltage, 6);
        AssertDcPowerBalance(inv);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void SuddenIrradianceDrop_DoesNotGenerateMoreThanAvailable(double irradiance)
    {
        var inv = new PvInverterDevice("pv_inv1", new PvInverterConfig { RampSlope = 0.1 });
        inv.SyncExternalRunCommand(true);
        inv.UpdateGridState(690, 50, true);
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(5));
        inv.Update(irradiance, 25, DateTime.UtcNow, TimeSpan.FromMilliseconds(100));

        Assert.True(inv.GetCurrentState().ActivePower <= inv.AvailableDcPowerKw * 0.99 + 1e-6);
        AssertDcPowerBalance(inv);
        if (irradiance == 0)
        {
            Assert.All(inv.MpptVoltageV, voltage => Assert.Equal(0, voltage));
            Assert.All(inv.MpptCurrentA, current => Assert.Equal(0, current));
        }
    }

    [Fact]
    public void ColdDeratingAndRamp_MaintainMpptPowerBalance()
    {
        var inv = new PvInverterDevice("pv_inv1", new PvInverterConfig { RampSlope = 0.1 });
        inv.SyncExternalRunCommand(true);
        inv.UpdateGridState(690, 50, true);
        for (int i = 0; i < 5; i++)
        {
            inv.Update(1000, -25, DateTime.UtcNow, TimeSpan.FromMilliseconds(100));
            AssertDcPowerBalance(inv);
            Assert.True(inv.GetCurrentState().ActivePower <= inv.AvailableDcPowerKw * 0.99 + 1e-6);
        }
    }

    private static void AssertDcPowerBalance(PvInverterDevice inv)
    {
        double mpptPowerW = Enumerable.Range(0, 6).Sum(m => inv.MpptVoltageV[m] * inv.MpptCurrentA[m]);
        var state = inv.GetCurrentState();
        Assert.Equal(state.ActivePower * 1000 / 0.99, mpptPowerW, 6);
        Assert.Equal(mpptPowerW, state.DcVoltage * state.DcCurrent, 6);
        Assert.Equal(inv.StringCurrentsA.Sum(), state.DcCurrent, 6);
    }

    [Fact]
    public void StoppedOnLiveGrid_RetainsVoltageAndFrequencyWithoutCurrent()
    {
        var inv = CreateRunning();
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));
        inv.SyncExternalRunCommand(false);
        inv.UpdateGridState(680, 49.8, true);
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));

        var state = inv.GetCurrentState();
        Assert.Equal(680, state.AcVoltage, 6);
        Assert.Equal(49.8, state.Frequency, 6);
        Assert.Equal(0, state.AcCurrent);
        Assert.Equal(0, state.DcCurrent);
        Assert.Equal(0, state.ActivePower);
        Assert.Equal(0, state.ReactivePower);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(-100)]
    public void PureReactivePower_HasAcCurrentAndNoDcActiveCurrent(double reactiveKvar)
    {
        var inv = CreateRunning();
        inv.SetPowerCommand(0, reactiveKvar);
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));

        var state = inv.GetCurrentState();
        Assert.Equal(0, state.ActivePower);
        Assert.Equal(reactiveKvar, state.ReactivePower, 6);
        Assert.Equal(690, state.AcVoltage, 6);
        Assert.Equal(50, state.Frequency, 6);
        Assert.Equal(0, state.DcCurrent);
        Assert.Equal(-100000 / (690 * Math.Sqrt(3)), state.AcCurrent, 6);
    }

    [Fact]
    public void ZeroPowerOnLiveGrid_RetainsVoltageAndFrequency()
    {
        var inv = CreateRunning();
        inv.SetPowerCommand(0, 0);
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));

        var state = inv.GetCurrentState();
        Assert.Equal(690, state.AcVoltage, 6);
        Assert.Equal(50, state.Frequency, 6);
        Assert.Equal(0, state.AcCurrent);
        Assert.Equal(0, state.DcCurrent);
    }

    [Fact]
    public void GridUnavailable_ZeroesAcTelemetryAndPower()
    {
        var inv = CreateRunning();
        inv.SetPowerCommand(80, 25);
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));
        inv.UpdateGridState(690, 50, false);
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));

        var state = inv.GetCurrentState();
        Assert.Equal(0, state.AcVoltage);
        Assert.Equal(0, state.Frequency);
        Assert.Equal(0, state.AcCurrent);
        Assert.Equal(0, state.ActivePower);
        Assert.Equal(0, state.ReactivePower);
    }

    [Theory]
    [InlineData(100, 75)]
    [InlineData(100, -75)]
    public void AcCurrent_MatchesApparentPower(double activeKw, double reactiveKvar)
    {
        var inv = CreateRunning();
        inv.SetPowerCommand(activeKw, reactiveKvar);
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));

        var state = inv.GetCurrentState();
        double measuredVa = Math.Sqrt(3) * state.AcVoltage * Math.Abs(state.AcCurrent);
        Assert.Equal(125000, measuredVa, 6);
        Assert.Equal(activeKw, state.DcVoltage * state.DcCurrent * 0.99 / 1000, 6);
        Assert.True(state.AcCurrent < 0);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(-25)]
    public void Start_PreservesPowerSetBeforeRun(double reactiveKvar)
    {
        var inv = PvInverterDevice.Create320kW("pv_inv1");
        inv.SetPowerCommand(80, reactiveKvar);
        inv.UpdateGridState(690, 50, true);
        inv.SyncExternalRunCommand(true);
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));

        Assert.True(inv.IsExternalRunCommand);
        Assert.Equal(OperationMode.Normal, inv.GetCurrentState().Mode);
        Assert.Equal(80, inv.ActivePowerSettingKw);
        Assert.Equal(reactiveKvar, inv.ReactivePowerSettingKvar);
        Assert.Equal(80, inv.GetCurrentState().ActivePower, 6);
        Assert.Equal(reactiveKvar, inv.GetCurrentState().ReactivePower, 6);
    }

    [Fact]
    public void StopAndRestart_PreservesCommandsAndRestartsRampFromZero()
    {
        var inv = new PvInverterDevice("pv_inv1", new PvInverterConfig { RampSlope = 0.1 });
        inv.SetPowerCommand(80, -25);
        inv.UpdateGridState(690, 50, true);
        inv.SyncExternalRunCommand(true);
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));
        inv.SyncExternalRunCommand(false);

        Assert.False(inv.IsExternalRunCommand);
        Assert.Equal(OperationMode.Off, inv.GetCurrentState().Mode);
        Assert.Equal(0, inv.GetCurrentState().ActivePower);
        Assert.Equal(0, inv.GetCurrentState().ReactivePower);
        Assert.Equal(80, inv.ActivePowerSettingKw);
        Assert.Equal(-25, inv.ReactivePowerSettingKvar);

        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));
        inv.SyncExternalRunCommand(true);
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromMilliseconds(100));

        Assert.Equal(10, inv.GetCurrentState().ActivePower, 6);
        Assert.Equal(-10, inv.GetCurrentState().ReactivePower, 6);
    }

    [Fact]
    public void SetPowerWhileStopped_DoesNotStartOrAdvanceRamp()
    {
        var inv = new PvInverterDevice("pv_inv1", new PvInverterConfig { RampSlope = 0.1 });
        inv.UpdateGridState(690, 50, true);
        inv.SyncExternalRunCommand(false);
        inv.ActivePowerSettingKw = 80;
        inv.ReactivePowerSettingKvar = 25;
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));

        Assert.False(inv.IsExternalRunCommand);
        Assert.Equal(0, inv.GetCurrentState().ActivePower);
        Assert.Equal(0, inv.GetCurrentState().ReactivePower);

        inv.SyncExternalRunCommand(true);
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromMilliseconds(100));
        Assert.Equal(10, inv.GetCurrentState().ActivePower, 6);
        Assert.Equal(10, inv.GetCurrentState().ReactivePower, 6);
    }

    [Fact]
    public void GridLossAndRecovery_RetainsCommandsWithoutAnotherWrite()
    {
        var inv = CreateRunning();
        inv.SetPowerCommand(80, -25);
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));
        inv.UpdateGridState(0, 0, false);

        Assert.Equal(0, inv.GetCurrentState().ActivePower);
        Assert.Equal(0, inv.GetCurrentState().ReactivePower);
        Assert.True(inv.IsExternalRunCommand);
        Assert.Equal(80, inv.ActivePowerSettingKw);
        Assert.Equal(-25, inv.ReactivePowerSettingKvar);

        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));
        inv.UpdateGridState(690, 50, true);
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));

        Assert.Equal(80, inv.GetCurrentState().ActivePower, 6);
        Assert.Equal(-25, inv.GetCurrentState().ReactivePower, 6);
    }

    [Fact]
    public void GridRecovery_AfterStopDoesNotRestart()
    {
        var inv = CreateRunning();
        inv.SetPowerCommand(80, 25);
        inv.UpdateGridState(0, 0, false);
        inv.SyncExternalRunCommand(false);
        inv.UpdateGridState(690, 50, true);
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));

        Assert.False(inv.IsExternalRunCommand);
        Assert.Equal(OperationMode.Off, inv.GetCurrentState().Mode);
        Assert.Equal(0, inv.GetCurrentState().ActivePower);
        Assert.Equal(0, inv.GetCurrentState().ReactivePower);
        Assert.Equal(80, inv.ActivePowerSettingKw);
        Assert.Equal(25, inv.ReactivePowerSettingKvar);
    }

    [Fact]
    public void IndependentPowerSettings_PreserveOtherChannelAndClamp()
    {
        var inv = CreateRunning();
        inv.SetPowerCommand(80, -25);
        inv.ActivePowerSettingKw = 90;
        Assert.Equal(-25, inv.ReactivePowerSettingKvar);
        inv.ReactivePowerSettingKvar = 30;
        Assert.Equal(90, inv.ActivePowerSettingKw);

        inv.ActivePowerSettingKw = -10;
        Assert.Equal(0, inv.ActivePowerSettingKw);
        Assert.Equal(30, inv.ReactivePowerSettingKvar);
        inv.ActivePowerSettingKw = 1000;
        inv.ReactivePowerSettingKvar = -1000;
        Assert.Equal(352, inv.ActivePowerSettingKw);
        Assert.Equal(-352, inv.ReactivePowerSettingKvar);
        inv.ReactivePowerSettingKvar = 1000;
        Assert.Equal(352, inv.ReactivePowerSettingKvar);
    }

    [Fact]
    public void RepeatedCommands_DoNotResetRamp()
    {
        var inv = new PvInverterDevice("pv_inv1", new PvInverterConfig { RampSlope = 0.1 });
        inv.UpdateGridState(690, 50, true);
        for (int i = 1; i <= 5; i++)
        {
            inv.SyncExternalRunCommand(true);
            inv.SetPowerCommand(80, -80);
            inv.ActivePowerSettingKw = 80;
            inv.ReactivePowerSettingKvar = -80;
            inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromMilliseconds(100));

            Assert.Equal(i * 10, inv.GetCurrentState().ActivePower, 6);
            Assert.Equal(-i * 10, inv.GetCurrentState().ReactivePower, 6);
        }
    }

    [Fact]
    public void ConcurrentIndependentSettings_DoNotLoseOtherChannel()
    {
        var inv = CreateRunning();
        Parallel.Invoke(
            () => { for (int i = 0; i < 100; i++) inv.ActivePowerSettingKw = 80; },
            () => { for (int i = 0; i < 100; i++) inv.ReactivePowerSettingKvar = -25; },
            () => { for (int i = 0; i < 100; i++) inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromMilliseconds(100)); });
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));

        Assert.Equal(80, inv.ActivePowerSettingKw);
        Assert.Equal(-25, inv.ReactivePowerSettingKvar);
        Assert.Equal(80, inv.GetCurrentState().ActivePower, 6);
        Assert.Equal(-25, inv.GetCurrentState().ReactivePower, 6);
    }

    [Fact]
    public void LimitReason_ReportsOffIrradianceSetpointRatedAndCold()
    {
        var inv = PvInverterDevice.Create320kW("pv_inv1");
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));
        Assert.Equal("停机", inv.LimitReason);

        inv = CreateRunning();
        inv.Update(0, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));
        Assert.Equal("辐照不足", inv.LimitReason);

        inv = CreateRunning();
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(5));
        Assert.Equal("已达额定", inv.LimitReason);
        Assert.InRange(inv.GetCurrentState().DcVoltage, 1100, 1400);
        Assert.True(inv.GetCurrentState().DcCurrent > 1);

        inv = CreateRunning();
        inv.SetPowerCommand(160, 0);
        for (int i = 0; i < 40; i++)
            inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromMilliseconds(100));
        Assert.Equal("有功设定", inv.LimitReason);

        inv = CreateRunning();
        inv.Update(1000, -25, DateTime.UtcNow, TimeSpan.FromSeconds(5));
        Assert.Equal("低温降额", inv.LimitReason);
    }
}
