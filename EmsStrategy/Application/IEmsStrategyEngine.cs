using EssSimulator.EmsStrategy.Domain;

namespace EssSimulator.EmsStrategy.Application;

public interface IEmsStrategyEngine
{
    void Initialize(EmsStrategyConfig config);
    void UpdateConfig(EmsStrategyConfig config);
    void UpdateMeasurements(PlantMeasurements meas);
    EmsControlOutput Step(TimeSpan dt);
    EmsStrategySnapshot GetSnapshot();
}
