using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.EssDeviceSimModel.Propagation
{
    public sealed class PropagationSweepContext
    {
        public required DeviceStepContext DeviceContext { get; init; }
        public required TimeSpan Step { get; init; }
        public required ElectricalBusNode Bus35 { get; init; }
        public required PcsPhysicalConfig PcsCfg { get; init; }
        /// <summary>系统唯一频率（Hz），来自 <see cref="Solver.SystemFrequencyResolver"/>。</summary>
        public required double SystemFrequencyHz { get; init; }
        public required double LastBus35LineVoltageV { get; init; }
        public required double StationBusNominalLineVoltageV { get; init; }
        public required bool MainBreakerClosed { get; init; }
        /// <summary>并网开路 35 kV（PCC 折算），供下域 GridEmf。不得用已加载的 Bus35 回写，避免 GridZ 双计。</summary>
        public required double OpenCircuitBus35LineVoltageV { get; init; }
    }
}
