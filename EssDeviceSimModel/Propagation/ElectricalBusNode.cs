using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.EssDeviceSimModel.Propagation
{
    /// <summary>
    /// 求解母线节点：持有电压、汇总的电流相量，以及注册的功率贡献者。
    /// </summary>
    public sealed class ElectricalBusNode
    {
        private readonly List<IBusPowerContributor> _contributors = new();
        private readonly List<Action<BusVoltageChangedEventArgs>> _voltageHandlers = new();
        private readonly List<IBusVoltageSource> _voltageSources = new();
        private double _sumCurrentReal;
        private double _sumCurrentImag;

        public ElectricalBusNode(
            string busId,
            double nominalLineVoltageV,
            ThreePhaseConnection connection = ThreePhaseConnection.Star)
        {
            BusId = busId;
            NominalLineVoltageV = nominalLineVoltageV;
            Connection = connection;
            LineVoltageV = 0;
            FrequencyHz = 0;
        }

        public string BusId { get; }
        public double NominalLineVoltageV { get; }
        public ThreePhaseConnection Connection { get; }

        public double LineVoltageV { get; set; }
        public double FrequencyHz { get; set; }

        public double TotalLineCurrentA =>
            Math.Sqrt(_sumCurrentReal * _sumCurrentReal + _sumCurrentImag * _sumCurrentImag);

        /// <summary>
        /// 汇总 P/Q 用的电压：已带电用本步母线电压；尚未定压时用额定，避免构造初值 0 把意图功率算成 0。
        /// </summary>
        private double AggregationVoltageV => LineVoltageV > 1.0 ? LineVoltageV : NominalLineVoltageV;

        public double TotalPhaseAngleDeg =>
            Math.Abs(TotalLineCurrentA) > 1e-9
                ? Math.Atan2(_sumCurrentImag, _sumCurrentReal) * 180.0 / Math.PI
                : 0;

        public double TotalActivePowerKw =>
            AcQuantityConverter.ComputeActivePowerKw(AggregationVoltageV, TotalLineCurrentA, TotalPhaseAngleDeg);

        public double TotalReactivePowerKvar =>
            AcQuantityConverter.ComputeReactivePowerKvar(AggregationVoltageV, TotalLineCurrentA, TotalPhaseAngleDeg);

        public IReadOnlyList<IBusPowerContributor> Contributors => _contributors;

        public void RegisterContributor(IBusPowerContributor contributor) =>
            _contributors.Add(contributor);

        public void RegisterVoltageHandler(Action<BusVoltageChangedEventArgs> handler) =>
            _voltageHandlers.Add(handler);

        public void RegisterVoltageSource(IBusVoltageSource source) =>
            _voltageSources.Add(source);

        /// <summary>
        /// 合并本地电压源（如构网 PCS）注入：下垂后取各源电压/频率算术平均，作为公共母线量。
        /// </summary>
        public bool ApplyLocalVoltageSources(PropagationSweepContext sweep)
        {
            if (_voltageSources.Count == 0)
                return false;

            double sumV = 0;
            double sumF = 0;
            int n = 0;

            foreach (var source in _voltageSources)
            {
                if (!source.IsInjecting(sweep.DeviceContext))
                    continue;

                var (v, f) = source.GetInjection(sweep.DeviceContext);
                if (v <= 1.0)
                    continue;

                sumV += v;
                sumF += f > 1.0 ? f : sweep.SystemFrequencyHz;
                n++;
            }

            if (n == 0)
                return false;

            double commonV = sumV / n;
            double commonF = sumF / n;
            if (Math.Abs(commonV - LineVoltageV) <= 1e-6 && Math.Abs(commonF - FrequencyHz) <= 1e-9)
                return false;

            SetVoltage(commonV, commonF, sweep, notifyCouplers: false);
            return true;
        }

        /// <summary>更新母线电压并通知已注册的 Coupler（<paramref name="notifyCouplers"/> 为 true 时）。</summary>
        public void SetVoltage(
            double lineVoltageV,
            double frequencyHz,
            PropagationSweepContext sweep,
            bool notifyCouplers = true)
        {
            LineVoltageV = lineVoltageV;
            FrequencyHz = frequencyHz;

            if (!notifyCouplers || _voltageHandlers.Count == 0)
                return;

            var args = new BusVoltageChangedEventArgs
            {
                SourceBus = this,
                LineVoltageV = lineVoltageV,
                FrequencyHz = frequencyHz,
                Sweep = sweep
            };

            foreach (var handler in _voltageHandlers)
                handler(args);
        }

        public void ResetPowerAggregation()
        {
            _sumCurrentReal = 0;
            _sumCurrentImag = 0;
        }

        public void AddCurrentPhasor(double lineCurrentA, double phaseAngleDeg)
        {
            double rad = phaseAngleDeg * Math.PI / 180.0;
            _sumCurrentReal += lineCurrentA * Math.Cos(rad);
            _sumCurrentImag += lineCurrentA * Math.Sin(rad);
        }

        /// <summary>兼容测试：由 P/Q 意图叠加电流相量。</summary>
        public void AddPower(double activeKw, double reactiveKvar)
        {
            double v = LineVoltageV > 1.0 ? LineVoltageV : NominalLineVoltageV;
            var phasor = AcQuantityConverter.FromPowerToPhasor(v, activeKw, reactiveKvar);
            AddCurrentPhasor(phasor.LineCurrentA, phasor.PhaseAngleDeg);
        }

        /// <summary>从注册的贡献者收集 P/Q 意图，换算为电流相量并累加。</summary>
        public void CollectFromContributors(DeviceStepContext context)
        {
            double v = LineVoltageV > 1.0 ? LineVoltageV : NominalLineVoltageV;

            foreach (var contributor in _contributors)
            {
                var p = contributor.GetBusPowerContribution(context);
                var phasor = AcQuantityConverter.FromPowerToPhasor(v, p.ActivePowerKw, p.ReactivePowerKvar);
                AddCurrentPhasor(phasor.LineCurrentA, phasor.PhaseAngleDeg);
            }
        }

        /// <summary>由汇总电流相量与本地电压生成线电流意图。</summary>
        public AcInternalQuantities ToCurrentIntent() =>
            new()
            {
                Connection = Connection,
                LineVoltageV = LineVoltageV,
                LineCurrentA = TotalLineCurrentA,
                PhaseAngleDeg = TotalPhaseAngleDeg,
                FrequencyHz = FrequencyHz
            };

        public AcInternalQuantities ToVoltageIntent() =>
            new()
            {
                Connection = Connection,
                LineVoltageV = LineVoltageV,
                FrequencyHz = LineVoltageV > 1.0 ? FrequencyHz : 0
            };
    }
}
