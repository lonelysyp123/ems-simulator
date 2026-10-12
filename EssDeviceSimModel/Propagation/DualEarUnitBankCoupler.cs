using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Solver;

namespace EssSimulator.EssDeviceSimModel.Propagation
{
    /// <summary>
    /// 单元双耳箱变组：共用单元断路器 Step 一次，再分别 Step 各台双耳变压器并写左右 690V 母线。
    /// </summary>
    internal sealed class DualEarUnitBankCoupler : IBusCoupler
    {
        private readonly BreakerSimulator _unitBreaker;
        private readonly IReadOnlyList<(DualEarTransformerDevice Dual, ElectricalBusNode Left, ElectricalBusNode Right)> _xfmrs;

        public DualEarUnitBankCoupler(
            int unitIndex,
            BreakerSimulator unitBreaker,
            ElectricalBusNode upstreamBus35,
            IReadOnlyList<(DualEarTransformerDevice Dual, ElectricalBusNode Left, ElectricalBusNode Right)> xfmrs)
        {
            _unitBreaker = unitBreaker;
            _xfmrs = xfmrs;
            UpstreamBus = upstreamBus35;
            DownstreamBus = xfmrs[0].Left;
            CouplerId = $"unit_branch_dual_u{unitIndex}";
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
            double freq = args.Sweep.SystemFrequencyHz;

            double sumP = 0;
            double sumQ = 0;
            double vRef = 0;
            foreach (var xfmr in _xfmrs)
            {
                sumP += xfmr.Left.TotalActivePowerKw + xfmr.Right.TotalActivePowerKw;
                sumQ += xfmr.Left.TotalReactivePowerKvar + xfmr.Right.TotalReactivePowerKvar;
                if (vRef <= 1.0 && xfmr.Left.LineVoltageV > 1.0)
                    vRef = xfmr.Left.LineVoltageV;
                if (vRef <= 1.0 && xfmr.Right.LineVoltageV > 1.0)
                    vRef = xfmr.Right.LineVoltageV;
            }

            var combined = AcQuantityConverter.FromLineVoltageAndPower(
                vRef, sumP, sumQ, ThreePhaseConnection.Star, freq);

            PropagationPortBinding.SetAcVoltageInput(_unitBreaker.Primary, bus35V, ThreePhaseConnection.Star);
            if (unitClosed)
                PropagationPortBinding.SetAcQuantitiesInput(_unitBreaker.Secondary, _unitBreaker.ReferToRated(combined));
            else
            {
                PropagationPortBinding.SetAcQuantitiesInput(
                    _unitBreaker.Secondary,
                    new AcInternalQuantities { Connection = ThreePhaseConnection.Star });
            }

            _unitBreaker.Step(args.Sweep.DeviceContext, args.Sweep.Step);

            foreach (var xfmr in _xfmrs)
            {
                var leftCurrent = EarCurrent(xfmr.Left, primaryV, freq);
                var rightCurrent = EarCurrent(xfmr.Right, primaryV, freq);
                PropagationPortBinding.SetAcVoltageInput(xfmr.Dual.Primary, primaryV, ThreePhaseConnection.Star);
                PropagationPortBinding.SetAcQuantitiesInput(xfmr.Dual.SecondaryLeft, leftCurrent);
                PropagationPortBinding.SetAcQuantitiesInput(xfmr.Dual.SecondaryRight, rightCurrent);
                xfmr.Dual.Step(args.Sweep.DeviceContext, args.Sweep.Step);

                double leftV = unitClosed ? xfmr.Dual.SecondaryLeft.Output.Ac?.Internal.LineVoltageV ?? 0 : 0;
                double rightV = unitClosed ? xfmr.Dual.SecondaryRight.Output.Ac?.Internal.LineVoltageV ?? 0 : 0;
                xfmr.Left.SetVoltage(leftV, leftV > 1.0 ? args.FrequencyHz : 0, args.Sweep, notifyCouplers: false);
                xfmr.Right.SetVoltage(rightV, rightV > 1.0 ? args.FrequencyHz : 0, args.Sweep, notifyCouplers: false);
            }
        }

        private static AcInternalQuantities EarCurrent(ElectricalBusNode bus, double primaryV, double freq)
        {
            if (primaryV > 1.0
                && (bus.TotalLineCurrentA > 1e-6
                    || Math.Abs(bus.TotalActivePowerKw) > 1e-3
                    || Math.Abs(bus.TotalReactivePowerKvar) > 1e-3))
            {
                return AcQuantityConverter.FromLineVoltageAndPower(
                    bus.LineVoltageV,
                    bus.TotalActivePowerKw,
                    bus.TotalReactivePowerKvar,
                    ThreePhaseConnection.Star,
                    freq);
            }

            return new AcInternalQuantities
            {
                LineVoltageV = primaryV > 1.0 && bus.LineVoltageV > 1.0 ? bus.LineVoltageV : 0,
                FrequencyHz = primaryV > 1.0 && bus.LineVoltageV > 1.0 ? freq : 0
            };
        }
    }
}
