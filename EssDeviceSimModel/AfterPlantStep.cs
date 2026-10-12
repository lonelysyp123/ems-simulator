using System;

namespace EssSimulator.EssDeviceSimModel
{
    /// <summary>物理步进结束后的协议 DTO 投影。Api 层实现；无宿主时为 no-op。</summary>
    public interface IAfterPlantStep
    {
        void AfterPlantStep(EnergyStorageSystem ess, DateTime simTime, TimeSpan elapsed);
    }

    public static class AfterPlantStep
    {
        private static IAfterPlantStep _current = NoOpAfterPlantStep.Instance;

        public static IAfterPlantStep Current
        {
            get => _current;
            set => _current = value ?? NoOpAfterPlantStep.Instance;
        }

        public static void Invoke(EnergyStorageSystem ess, DateTime simTime, TimeSpan elapsed) =>
            _current.AfterPlantStep(ess, simTime, elapsed);

        public static void Reset() => _current = NoOpAfterPlantStep.Instance;
    }

    public sealed class NoOpAfterPlantStep : IAfterPlantStep
    {
        public static readonly NoOpAfterPlantStep Instance = new();
        public void AfterPlantStep(EnergyStorageSystem ess, DateTime simTime, TimeSpan elapsed) { }
    }

    /// <summary>按顺序调用多个 <see cref="IAfterPlantStep"/>（如 EMS 策略再协议投影）。</summary>
    public sealed class CompositeAfterPlantStep : IAfterPlantStep
    {
        private readonly IAfterPlantStep[] _steps;

        public CompositeAfterPlantStep(params IAfterPlantStep[] steps) =>
            _steps = steps ?? Array.Empty<IAfterPlantStep>();

        public void AfterPlantStep(EnergyStorageSystem ess, DateTime simTime, TimeSpan elapsed)
        {
            foreach (var step in _steps)
                step?.AfterPlantStep(ess, simTime, elapsed);
        }
    }
}
