using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Tests.Devices;

public class PcsOvercurrentTests
{
    private static PcsDevice CreateGridPcs(double maxCurrentA = 1100) =>
        PcsDeviceFactory.Create("pcs_test", new PcsDeviceConfig
        {
            AcNominalLineVoltageV = 690,
            FrequencyHz = 50,
            DcVoltageRangeMinV = 1000,
            DcVoltageRangeMaxV = 1500,
            MaxCurrentA = maxCurrentA,
            RatedPowerKw = 1250,
            MaxPowerKw = 1250,
            Efficiency = 0.99,
            GridLossCoefficient = 0.11,
            RampSlope = 500,
            RampIntervalMs = 100,
            RampDelayMs = 0,
        });

    [Fact]
    public void GridConnected_FullChargeAtRatedPower_DoesNotTripOvercurrent()
    {
        var pcs = CreateGridPcs(maxCurrentA: 1100);
        pcs.SyncExternalRunCommand(true);
        pcs.UpdateGridState(690, 50, isUtilityGridAvailable: true);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.GridConnected);
        pcs.SetPowerCommand(-1250, 0);

        for (int i = 0; i < 30; i++)
            pcs.Update(1300, 0, DateTime.UtcNow, TimeSpan.FromMilliseconds(100));

        var st = pcs.GetCurrentState();
        Assert.Equal(OperationMode.Normal, st.Mode);
        Assert.Equal(0, st.FaultType);
        Assert.InRange(Math.Abs(st.AcCurrent), 1000, 1100);
        Assert.True(Math.Abs(st.AcCurrent) <= 1100);
    }

    [Fact]
    public void GridConnected_AcCurrent_UsesTerminalPower_NotGridLossInflatedPower()
    {
        var pcs = CreateGridPcs(maxCurrentA: 2000);
        pcs.SyncExternalRunCommand(true);
        pcs.UpdateGridState(690, 50, isUtilityGridAvailable: true);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.GridConnected);
        pcs.SetPowerCommand(-1250, 0);

        for (int i = 0; i < 30; i++)
            pcs.Update(1300, 0, DateTime.UtcNow, TimeSpan.FromMilliseconds(100));

        var st = pcs.GetCurrentState();
        double expectedI = 1250000 / (690 * Math.Sqrt(3));
        Assert.InRange(Math.Abs(st.AcCurrent), expectedI - 5, expectedI + 5);
        Assert.True(Math.Abs(pcs.GetGridSideActivePower()) > Math.Abs(st.ActivePower));
        Assert.True(st.AcCurrent > 0, "并网充电电流应为正（从母线取电）");
    }

    [Fact]
    public void GridConnected_Discharge_AcCurrentIsNegative()
    {
        var pcs = CreateGridPcs(maxCurrentA: 2000);
        pcs.SyncExternalRunCommand(true);
        pcs.UpdateGridState(690, 50, isUtilityGridAvailable: true);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.GridConnected);
        pcs.SetPowerCommand(800, 0);

        for (int i = 0; i < 30; i++)
            pcs.Update(1300, 0, DateTime.UtcNow, TimeSpan.FromMilliseconds(100));

        var st = pcs.GetCurrentState();
        Assert.True(st.ActivePower > 100, $"放电有功应>0，实际 {st.ActivePower:F1} kW");
        Assert.True(st.AcCurrent < 0, $"并网放电电流应为负，实际 {st.AcCurrent:F1} A");
    }

    [Fact]
    public void Islanded_Discharge_AcCurrentIsNegative_SameAsGridConnected()
    {
        var pcs = PcsDeviceFactory.Create("pcs_island_i", new PcsDeviceConfig
        {
            AcNominalLineVoltageV = 690,
            FrequencyHz = 50,
            DcVoltageRangeMinV = 1000,
            DcVoltageRangeMaxV = 1500,
            MaxCurrentA = 2000,
            RatedPowerKw = 1725,
            MaxPowerKw = 1897.5,
            BlackStartPrechargeDelayMs = 0,
            BlackStartVoltageRampVs = 400,
            DvDtTripThresholdVPerSec = 10_000,
            InrushPeakMultiplier = 0.3
        });
        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(690);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);
        pcs.SetBlackStartInrushDemand(200, 0);

        var t = DateTime.UtcNow;
        PcsState st = pcs.GetCurrentState();
        for (int i = 0; i < 15; i++)
        {
            t = t.AddMilliseconds(200);
            pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
            st = pcs.GetCurrentState();
        }

        Assert.True(st.ActivePower > 50, $"离网放电有功应>0，实际 {st.ActivePower:F1} kW");
        Assert.True(st.AcCurrent < 0, $"离网放电电流应与并网同号为负，实际 {st.AcCurrent:F1} A");
    }
}
