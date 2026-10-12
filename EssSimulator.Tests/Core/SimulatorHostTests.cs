using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssSimModelApi.BatteryManagementSystem;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;

namespace EssSimulator.Tests.Core;

[Collection("SimulatorHost")]
public class SimulatorHostTests : SimulatorHostTestBase
{
    [Fact]
    public void Reset_ClearsRegisteredKeys_AndRestoresStaticFacades()
    {
        SimulatorHost.Instance.Register("ess", new object());
        AfterPlantStep.Current = new RecordingAfterPlantStep();
        UiSnapshotNotifier.Current = new RecordingUiNotifier();

        SimulatorHostScope.Reset();

        Assert.False(SimulatorHost.Instance.Contains("ess"));
        Assert.Null(SimulatorHost.Instance.Get<object>("ess"));
        Assert.Same(NoOpAfterPlantStep.Instance, AfterPlantStep.Current);
        Assert.Same(NoOpUiSnapshotNotifier.Instance, UiSnapshotNotifier.Current);
    }

    [Fact]
    public void TypedAccess_RoundtripsAndKeepsStringKeys()
    {
        var host = SimulatorHost.Instance;
        Assert.Null(host.TryGetEss());
        Assert.Null(host.TryGetEmu(1));
        Assert.Null(host.TryGetBms(1));

        var emu = new EnergyManagementData();
        host.RegisterEmu(2, emu);
        Assert.Same(emu, host.TryGetEmu(2));
        Assert.True(host.Contains("emu2"));
        Assert.Null(host.TryGetEmu(0));

        var bms = new BatteryManagementSystemData();
        host.RegisterBms(1, bms);
        Assert.Same(bms, host.TryGetBms(1));
        Assert.True(host.Contains("bms1"));
        Assert.Null(host.TryGetBms(0));
    }

    [Fact]
    public void RegisterEmu_RejectsNonPositiveUnit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SimulatorHost.Instance.RegisterEmu(0, new EnergyManagementData()));
    }

    private sealed class RecordingAfterPlantStep : IAfterPlantStep
    {
        public void AfterPlantStep(EnergyStorageSystem ess, DateTime simTime, TimeSpan elapsed) { }
    }

    private sealed class RecordingUiNotifier : IUiSnapshotNotifier
    {
        public void RequestImmediatePush() { }
    }
}
