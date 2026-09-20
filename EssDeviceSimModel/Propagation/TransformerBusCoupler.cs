using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.EssDeviceSimModel.Propagation
{
    /// <summary>变压器母线耦合：上游电压 + 下游电流意图 → Step → 下游母线电压。</summary>
    internal sealed class TransformerBusCoupler : IBusCoupler
    {
        private readonly TransformerDevice _transformer;
        private readonly Func<PropagationSweepContext, AcInternalQuantities> _resolveSecondaryCurrent;
        private readonly Func<PropagationSweepContext, bool> _isEnergized;

        public TransformerBusCoupler(
            TransformerDevice transformer,
            ElectricalBusNode upstreamBus,
            ElectricalBusNode downstreamBus,
            Func<PropagationSweepContext, AcInternalQuantities> resolveSecondaryCurrent,
            Func<PropagationSweepContext, bool>? isEnergized = null)
        {
            _transformer = transformer;
            UpstreamBus = upstreamBus;
            DownstreamBus = downstreamBus;
            _resolveSecondaryCurrent = resolveSecondaryCurrent;
            _isEnergized = isEnergized ?? (_ => true);
            CouplerId = transformer.DeviceId;
        }

        public string CouplerId { get; }
        public ElectricalBusNode UpstreamBus { get; }
        public ElectricalBusNode DownstreamBus { get; }

        public void Attach() =>
            UpstreamBus.RegisterVoltageHandler(OnUpstreamVoltageChanged);

        private void OnUpstreamVoltageChanged(BusVoltageChangedEventArgs args)
        {
            bool energized = _isEnergized(args.Sweep);
            double primaryV = energized ? args.LineVoltageV : 0;

            PropagationPortBinding.SetAcVoltageInput(
                _transformer.Primary, primaryV, ThreePhaseConnection.Star);

            var secondaryCurrent = _resolveSecondaryCurrent(args.Sweep);
            PropagationPortBinding.SetAcQuantitiesInput(_transformer.Secondary, secondaryCurrent);

            _transformer.Step(args.Sweep.DeviceContext, args.Sweep.Step);

            double downstreamV = energized
                ? _transformer.Secondary.Output.Ac?.Internal.LineVoltageV ?? 0
                : 0;

            if (DownstreamBus.KernelOwned)
                return;

            DownstreamBus.SetVoltage(
                downstreamV,
                downstreamV > 1.0 ? args.FrequencyHz : 0,
                args.Sweep);
        }
    }
}
