using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.EssDeviceSimModel.Propagation
{
    /// <summary>断路器母线耦合：上游电压 + 下游 P/Q 决定的电流 → Step → 下游母线电压。</summary>
    internal sealed class BreakerBusCoupler : IBusCoupler
    {
        private readonly BreakerSimulator _breaker;
        private readonly Func<PropagationSweepContext, AcInternalQuantities> _resolveSecondaryCurrent;

        public BreakerBusCoupler(
            BreakerSimulator breaker,
            ElectricalBusNode upstreamBus,
            ElectricalBusNode downstreamBus,
            Func<PropagationSweepContext, AcInternalQuantities> resolveSecondaryCurrent)
        {
            _breaker = breaker;
            UpstreamBus = upstreamBus;
            DownstreamBus = downstreamBus;
            _resolveSecondaryCurrent = resolveSecondaryCurrent;
            CouplerId = breaker.DeviceId;
        }

        public string CouplerId { get; }
        public ElectricalBusNode UpstreamBus { get; }
        public ElectricalBusNode DownstreamBus { get; }

        public void Attach() =>
            UpstreamBus.RegisterVoltageHandler(OnUpstreamVoltageChanged);

        private void OnUpstreamVoltageChanged(BusVoltageChangedEventArgs args)
        {
            PropagationPortBinding.SetAcVoltageInput(
                _breaker.Primary, args.LineVoltageV, ThreePhaseConnection.Star);

            var secondaryCurrent = _breaker.ReferToRated(_resolveSecondaryCurrent(args.Sweep));
            PropagationPortBinding.SetAcQuantitiesInput(_breaker.Secondary, secondaryCurrent);

            _breaker.Step(args.Sweep.DeviceContext, args.Sweep.Step);

            bool closed = _breaker.SwitchState.IsClosed && !_breaker.SwitchState.IsTripped;
            double downstreamV = closed
                ? _breaker.Secondary.Output.Ac?.Internal.LineVoltageV ?? 0
                : 0;

            DownstreamBus.SetVoltage(
                downstreamV,
                downstreamV > 1.0 ? args.FrequencyHz : 0,
                args.Sweep);
        }
    }
}
