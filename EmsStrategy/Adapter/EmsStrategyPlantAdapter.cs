using EssSimulator.EssDeviceSimModel;
using EssSimulator.EmsStrategy.Application;
using EssSimulator.EmsStrategy.Domain;

namespace EssSimulator.EmsStrategy.Adapter;

public sealed class EmsStrategyPlantAdapter : IAfterPlantStep
{
    private readonly EmsStrategyRuntime _runtime;

    public EmsStrategyPlantAdapter(EmsStrategyRuntime runtime) =>
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public void AfterPlantStep(EnergyStorageSystem ess, DateTime simTime, TimeSpan elapsed)
    {
        if (!_runtime.Config.Enabled)
            return;

        PlantMeasurements meas = PlantMeasurementSampler.Sample(ess, simTime);
        _runtime.Engine.UpdateMeasurements(meas);
        EmsControlOutput output = _runtime.Engine.Step(elapsed);
        PcsCommandSink.Apply(ess, output);
    }
}
