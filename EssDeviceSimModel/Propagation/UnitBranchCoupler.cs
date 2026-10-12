using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Solver;

namespace EssSimulator.EssDeviceSimModel.Propagation
{
    /// <summary>单元支路（单元断 + 单元变）作为单段 Coupler：35kV → 690V。</summary>
    internal sealed class UnitBranchCoupler : IBusCoupler
    {
        private readonly BreakerSimulator _unitBreaker;
        private readonly TransformerDevice _unitTransformer;
        private readonly ElectricalBusNode _bus690;

        public UnitBranchCoupler(
            int unitIndex,
            BreakerSimulator unitBreaker,
            TransformerDevice unitTransformer,
            ElectricalBusNode upstreamBus35,
            ElectricalBusNode bus690)
        {
            _unitBreaker = unitBreaker;
            _unitTransformer = unitTransformer;
            UpstreamBus = upstreamBus35;
            DownstreamBus = bus690;
            _bus690 = bus690;
            CouplerId = $"unit_branch_u{unitIndex}";
        }

        public string CouplerId { get; }
        public ElectricalBusNode UpstreamBus { get; }
        public ElectricalBusNode DownstreamBus { get; }

        public void Attach() =>
            UpstreamBus.RegisterVoltageHandler(OnUpstreamVoltageChanged);

        private void OnUpstreamVoltageChanged(BusVoltageChangedEventArgs args)
        {
            bool unitClosed = _unitBreaker.SwitchState.IsClosed && !_unitBreaker.SwitchState.IsTripped;
            double bus35V = args.LineVoltageV;
            double primaryV = unitClosed ? bus35V : 0;

            var unitCurrent = primaryV > 1.0
                && (_bus690.TotalLineCurrentA > 1e-6
                    || Math.Abs(_bus690.TotalActivePowerKw) > 1e-3
                    || Math.Abs(_bus690.TotalReactivePowerKvar) > 1e-3)
                ? AcQuantityConverter.FromLineVoltageAndPower(
                    _bus690.LineVoltageV,
                    _bus690.TotalActivePowerKw,
                    _bus690.TotalReactivePowerKvar,
                    ThreePhaseConnection.Star,
                    args.Sweep.SystemFrequencyHz)
                : new AcInternalQuantities
                {
                    LineVoltageV = primaryV > 1.0 && _bus690.LineVoltageV > 1.0 ? _bus690.LineVoltageV : 0,
                    FrequencyHz = primaryV > 1.0 && _bus690.LineVoltageV > 1.0
                        ? args.Sweep.SystemFrequencyHz
                        : 0
                };

            PropagationPortBinding.SetAcVoltageInput(_unitBreaker.Primary, bus35V, ThreePhaseConnection.Star);
            if (unitClosed)
            {
                PropagationPortBinding.SetAcQuantitiesInput(_unitBreaker.Secondary, _unitBreaker.ReferToRated(unitCurrent));
            }
            else
            {
                PropagationPortBinding.SetAcQuantitiesInput(
                    _unitBreaker.Secondary,
                    new AcInternalQuantities { Connection = ThreePhaseConnection.Star });
            }

            _unitBreaker.Step(args.Sweep.DeviceContext, args.Sweep.Step);

            PropagationPortBinding.SetAcVoltageInput(_unitTransformer.Primary, primaryV, ThreePhaseConnection.Star);
            PropagationPortBinding.SetAcQuantitiesInput(_unitTransformer.Secondary, unitCurrent);
            _unitTransformer.Step(args.Sweep.DeviceContext, args.Sweep.Step);

            double bus690V = unitClosed
                ? _unitTransformer.Secondary.Output.Ac?.Internal.LineVoltageV ?? 0
                : 0;

            _bus690.SetVoltage(
                bus690V,
                bus690V > 1.0 ? args.FrequencyHz : 0,
                args.Sweep,
                notifyCouplers: false);
        }
    }
}
