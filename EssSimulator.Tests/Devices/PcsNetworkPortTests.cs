using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Tests.Devices;

public class PcsNetworkPortTests
{
    private static PcsDevice CreateGridPcs() =>
        PcsDeviceFactory.Create("pcs_net", new PcsDeviceConfig
        {
            AcNominalLineVoltageV = 690,
            FrequencyHz = 50,
            DcVoltageRangeMinV = 1000,
            DcVoltageRangeMaxV = 1500,
            MaxCurrentA = 2000,
            RatedPowerKw = 1250,
            MaxPowerKw = 1250,
            Efficiency = 0.99,
            GridLossCoefficient = 0.11,
            RampSlope = 500,
            RampIntervalMs = 100,
            RampDelayMs = 0,
        });

    private static void RampToCommand(PcsDevice pcs, double pKw, double qKvar)
    {
        pcs.SyncExternalRunCommand(true);
        pcs.UpdateGridState(690, 50, isUtilityGridAvailable: true);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.GridConnected);
        pcs.SetPowerCommand(pKw, qKvar);
        for (int i = 0; i < 30; i++)
            pcs.Update(1300, 0, DateTime.UtcNow, TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void Locked_PublishesKernelSolution_InjectionKeepsCommand()
    {
        var pcs = CreateGridPcs();
        RampToCommand(pcs, 800, 100);

        Assert.True(pcs.TryGetGridFollowingInjection(out double cmdW, out double cmdVar));
        Assert.InRange(cmdW, 800 * 0.89 * 1000 - 1, 800 * 0.89 * 1000 + 1);
        Assert.Equal(100_000, cmdVar, 1);

        double energyBefore = pcs.GetCurrentState().TotalDischargeEnergy;
        pcs.AcTerminalLockedToNetwork = true;
        pcs.SetNetworkSolvedPort(120, 45, idcIntoDeviceA: 90);
        pcs.Update(1300, 0, DateTime.UtcNow, TimeSpan.FromMilliseconds(100));

        var st = pcs.GetCurrentState();
        Assert.Equal(120, st.ActivePower, 3);
        Assert.Equal(45, st.ReactivePower, 3);
        Assert.Equal(90, st.DcCurrent, 3);
        Assert.True(st.AcCurrent < 0, "放电电流应为负");

        Assert.True(pcs.TryGetGridFollowingInjection(out double stillW, out double stillVar));
        Assert.Equal(cmdW, stillW, 3);
        Assert.Equal(cmdVar, stillVar, 3);

        double hours = TimeSpan.FromMilliseconds(100).TotalHours;
        Assert.Equal(120 * hours, st.TotalDischargeEnergy - energyBefore, 6);
    }

    [Fact]
    public void Unlocked_IgnoresKernelSolution()
    {
        var pcs = CreateGridPcs();
        RampToCommand(pcs, 800, 0);
        pcs.SetNetworkSolvedPort(120, 45, 90);
        pcs.Update(1300, 0, DateTime.UtcNow, TimeSpan.FromMilliseconds(100));

        Assert.InRange(pcs.GetCurrentState().ActivePower, 790, 810);
        Assert.NotEqual(90, pcs.GetCurrentState().DcCurrent);
    }
}
