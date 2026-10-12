using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;

namespace EssSimulator.Tests;

/// <summary>
/// 串行化并复位 SimulatorHost / AfterPlantStep / UiSnapshotNotifier，
/// 避免并行用例互抢 <c>ess</c> / <c>emu1</c> / <c>bms1</c>。
/// </summary>
[CollectionDefinition("SimulatorHost", DisableParallelization = true)]
public sealed class SimulatorHostCollection
{
}

public sealed class SimulatorHostScope : IDisposable
{
    public SimulatorHostScope() => Reset();

    public void Dispose() => Reset();

    public static void Reset()
    {
        SimulatorHost.Instance.Reset();
        AfterPlantStep.Reset();
        UiSnapshotNotifier.Reset();
        ExternalControlGate.Reset();
        PlantControlMailbox.Reset();
    }
}

[Collection("SimulatorHost")]
public abstract class SimulatorHostTestBase : IDisposable
{
    private readonly SimulatorHostScope _scope = new();

    public void Dispose() => _scope.Dispose();
}
