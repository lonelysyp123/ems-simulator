using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Solver;
using log4net;

namespace EssSimulator.EssDeviceSimModel.Propagation
{
    /// <summary>
    /// 径向网络前推回代求解：
    /// ① 叶子汇报 P/Q → ② 自下而上汇总 → ③ 电网 Q-U 定压 → ④ Coupler 链传播电压 → ⑤ 算电流并 Step 设备
    /// → ⑥ 按实测 Q 多轮 Q-U/V 反馈迭代直至收敛。
    /// </summary>
    public sealed class RadialPowerSweepEngine
    {
        private readonly ILog _log = LogManager.GetLogger(typeof(RadialPowerSweepEngine));
        private readonly RadialNetworkGraph _graph;
        private readonly ElectricalNetwork _network;
        private readonly EnergyStorageSystem _ess;
        private readonly PccConfig _pccCfg;
        private readonly PcsPhysicalConfig _pcsCfg;
        private readonly ISelfActivatingElectricalSource _grid;
        private readonly int _quvMaxIterations;
        private readonly double _voltageTolerancePu;
        private double _lastBus35LineVoltageV;

        public RadialPowerSweepEngine(
            RadialNetworkGraph graph,
            EnergyStorageSystem ess,
            PccConfig pccCfg,
            PcsPhysicalConfig pcsCfg,
            int propagationQuvMaxIterations = 3,
            double propagationVoltageTolerancePu = 0.001)
        {
            _graph = graph;
            _network = graph.Network;
            _ess = ess;
            _pccCfg = pccCfg;
            _pcsCfg = pcsCfg;
            _quvMaxIterations = Math.Max(1, propagationQuvMaxIterations);
            _voltageTolerancePu = Math.Max(0, propagationVoltageTolerancePu);
            _grid = _network.Grid as ISelfActivatingElectricalSource
                ?? throw new InvalidOperationException("Grid 必须实现 ISelfActivatingElectricalSource");
            _lastBus35LineVoltageV = pccCfg.StationBusNominalLineVoltage;
            _log.Info(
                $"[RadialSweep] 母线前推回代引擎已就绪（Q-U/V 最多 {_quvMaxIterations} 轮，容差 {_voltageTolerancePu:P3} pu）");
        }

        public RadialNetworkGraph Graph => _graph;

        /// <summary>电压源发起一次完整求解周期（默认 100ms 调用）。</summary>
        /// <param name="step">潮流/Coupler 步长。</param>
        /// <param name="meterIntegrationStep">电表电能积分步长（上次回调至本次的真实间隔 × IntegrationStepMultiplier）。</param>
        public void SolveCycle(DateTime simTime, TimeSpan step, TimeSpan meterIntegrationStep)
        {
            var context = BuildContext(simTime, step);
            NetworkControlBridge.SyncLoadPlan(_network, _ess._loadDevice, simTime);
            // 生产路径唯一一次：PlantEngine 不再重复同步；Solver 夹具走 NetworkStepOrchestrator.SyncBeforeSolverStep。
            NetworkControlBridge.SyncBmsLinksFromRacks(_network, _ess._bmsRackDevices);

            Phase1CollectLeafPower(context);
            Phase2AggregatePowerBottomUp(context);
            Phase3GridVoltageSolve(context, step);
            Phase4PropagateVoltageTopDown(context, step);
            Phase5AssignCurrentsAndStepDevices(context, step);
            RefreshSeriesDevicesAfterLeafStep(context, step);

            RunQuvRefinementIterations(context, step);
            SystemFrequencyResolver.Refresh(_network, context);
            SamplePccMeter(meterIntegrationStep);

            NetworkStepOrchestrator.ApplyGridResultsToEnergyStorageSystem(
                _network, _ess, simTime, step, _pcsCfg);
            NetworkControlBridge.ProjectBreakersToLegacy(_network, _ess);
            PublishBusQuantities();
        }

        /// <summary>① 叶子设备上报 P/Q 意图（不依赖电压源指定电流）。</summary>
        private void Phase1CollectLeafPower(DeviceStepContext context)
        {
            _graph.Bus35.ResetPowerAggregation();
            foreach (var bus690 in _graph.AllUnit690Buses)
                bus690.ResetPowerAggregation();
        }

        /// <summary>② 690V 汇总 → 35kV 全站 P/Q。</summary>
        private void Phase2AggregatePowerBottomUp(DeviceStepContext context)
        {
            _graph.Bus35.CollectFromContributors(context);

            foreach (var bus690 in _graph.AllUnit690Buses)
            {
                bus690.CollectFromContributors(context);
                _graph.Bus35.AddPower(bus690.TotalActivePowerKw, bus690.TotalReactivePowerKvar);
            }
        }

        /// <summary>③ 电网读全站 Q，Q-U 定 220kV 电压（电压源唯一职责）。</summary>
        private void Phase3GridVoltageSolve(
            DeviceStepContext context,
            TimeSpan step,
            double? totalReactiveKvarOverride = null)
        {
            double totalQ = totalReactiveKvarOverride ?? _graph.Bus35.TotalReactivePowerKvar;
            _network.Grid.SetAggregatedReactivePowerKvar(totalQ);
            _grid.Activate(context, step);

            _graph.BusGrid.LineVoltageV = _network.Grid.Port.Output.Ac?.Internal.LineVoltageV ?? 0;
            SystemFrequencyResolver.Refresh(_network, context);
            _graph.BusGrid.FrequencyHz = _network.SystemFrequencyHz;
        }

        /// <summary>④ 电压自上而下：经 Coupler 链 Grid → 主断 → 主变 → 35kV → 单元 → 690V。</summary>
        private void Phase4PropagateVoltageTopDown(DeviceStepContext context, TimeSpan step)
        {
            var sweep = BuildSweepContext(context, step);

            if (!context.MainBreakerClosed)
            {
                _network.PccLineVoltageV = 0;
                double islandV = EstimateBus35WhenMainOpen();
                _network.StationBus35LineVoltageV = islandV;
                if (_network.HasMainTransformer)
                {
                    // 主断分闸后 Coupler 不再驱动主变；必须本步 Step，否则端口残留并网电压。
                    // 35kV 有黑启动反送时，按变比折到一次侧供并网点抽头采样。
                    double afterV = islandV > 1.0
                        ? islandV * _network.MainTransformer.TurnsRatio
                        : 0;
                    double afterF = afterV > 1.0 ? _network.SystemFrequencyHz : 0;
                    _graph.BusAfterMainBreaker.SetVoltage(afterV, afterF, sweep, notifyCouplers: false);
                    StepMainBreakerIsolated(context, step, islandV, afterF);
                    StepMainTransformerIsolated(context, step, islandV, afterV, afterF);
                }

                _graph.PropagateVoltageIsland(sweep, islandV);
                _lastBus35LineVoltageV = islandV;
                return;
            }

            double v220 = _graph.BusGrid.LineVoltageV;
            _network.PccLineVoltageV = v220;
            _network.StationBus35LineVoltageV = GridFeedbackConventions.DeriveStationBusVoltage(
                v220, _pccCfg.NominalLineVoltage, _pccCfg.StationBusNominalLineVoltage);

            _graph.PropagateVoltageFromGrid(sweep);
            _lastBus35LineVoltageV = _graph.Bus35.LineVoltageV;
        }

        private PropagationSweepContext BuildSweepContext(DeviceStepContext context, TimeSpan step) =>
            new()
            {
                DeviceContext = context,
                Step = step,
                Bus35 = _graph.Bus35,
                PcsCfg = _pcsCfg,
                SystemFrequencyHz = _network.SystemFrequencyHz,
                LastBus35LineVoltageV = _lastBus35LineVoltageV,
                StationBusNominalLineVoltageV = _pccCfg.StationBusNominalLineVoltage,
                MainBreakerClosed = context.MainBreakerClosed
            };

        /// <summary>⑤ 在已知母线电压下由 P/Q 算电流，驱动各设备 Step。</summary>
        private void Phase5AssignCurrentsAndStepDevices(DeviceStepContext context, TimeSpan step)
        {
            double bus35V = _graph.Bus35.LineVoltageV;

            PropagationPortBinding.SetAcVoltageInput(
                _network.Load.Port, bus35V, ThreePhaseConnection.Star);
            _network.Load.Step(context, step);

            for (int u = 0; u < _graph.UnitBuses690.Count; u++)
            {
                if (u >= _network.UnitBreakers.Count)
                    continue;

                bool unitClosed = _network.UnitBreakers[u].SwitchState.IsClosed
                    && !_network.UnitBreakers[u].SwitchState.IsTripped;
                bool gridAvailable = context.MainBreakerClosed && unitClosed;

                var (baseChannel, pcsCount) = PcsUnitLayout.RangeOfUnit(_network.PcsPerUnit, u);
                for (int ch = 0; ch < pcsCount; ch++)
                {
                    int idx = baseChannel + ch;
                    if (idx >= _network.PcsDevices.Count)
                        continue;

                    double bus690V = _graph.Bus690ForPcsChannel(idx).LineVoltageV;
                    SolvePcsBmsPair(context, step, idx, bus690V, gridAvailable && bus690V > 1.0);
                }
            }
        }

        /// <summary>设备 Step 后，按实测 Q 多轮 Q-U 定压并完整重跑电压传播直至收敛。</summary>
        private void RunQuvRefinementIterations(DeviceStepContext context, TimeSpan step)
        {
            if (!context.MainBreakerClosed || _quvMaxIterations <= 1)
                return;

            for (int i = 1; i < _quvMaxIterations; i++)
            {
                double totalQ = CollectFeedbackReactivePowerKvar();
                double prevGridV = _graph.BusGrid.LineVoltageV;

                Phase3GridVoltageSolve(context, step, totalQ);
                Phase4PropagateVoltageTopDown(context, step);

                if (QuvConvergence.IsLineVoltageConverged(
                        prevGridV,
                        _graph.BusGrid.LineVoltageV,
                        _pccCfg.NominalLineVoltage,
                        _voltageTolerancePu))
                {
                    // _log.Debug($"[RadialSweep] Q-U/V 反馈迭代 {i} 轮后电压收敛");
                    break;
                }
            }
        }

        /// <summary>叶子设备 Step 后，用最新下游母线电压重算串联设备（主变/单元变）端口。</summary>
        private void RefreshSeriesDevicesAfterLeafStep(DeviceStepContext context, TimeSpan step)
        {
            if (!context.MainBreakerClosed || !_network.HasMainTransformer)
                return;

            var sweep = BuildSweepContext(context, step);
            double bus35V = _graph.Bus35.LineVoltageV;
            var stationCurrent = _graph.ResolveStationSecondaryCurrent(sweep);

            PropagationPortBinding.SetAcVoltageInput(
                _network.MainTransformer.Primary,
                _graph.BusAfterMainBreaker.LineVoltageV,
                ThreePhaseConnection.Star);
            PropagationPortBinding.SetAcQuantitiesInput(_network.MainTransformer.Secondary, stationCurrent);
            _network.MainTransformer.Step(context, step);

            for (int u = 0; u < _graph.UnitBuses690.Count; u++)
            {
                if (u >= _network.UnitBreakers.Count)
                    continue;

                bool unitClosed = _network.UnitBreakers[u].SwitchState.IsClosed
                    && !_network.UnitBreakers[u].SwitchState.IsTripped;
                var duals = _network.DualEarsOfUnit(u);
                if (duals.Count > 0)
                {
                    for (int t = 0; t < duals.Count; t++)
                    {
                        var dual = duals[t];
                        var left = _graph.FindBus(RuntimeBusIds.Unit690Ear(u, t, right: false));
                        var right = _graph.FindBus(RuntimeBusIds.Unit690Ear(u, t, right: true));
                        var leftCurrent = unitClosed && bus35V > 1.0 && left != null
                            ? AcQuantityConverter.FromLineVoltageAndPower(
                                left.LineVoltageV, left.TotalActivePowerKw, left.TotalReactivePowerKvar,
                                ThreePhaseConnection.Star, _network.SystemFrequencyHz)
                            : new AcInternalQuantities();
                        var rightCurrent = unitClosed && bus35V > 1.0 && right != null
                            ? AcQuantityConverter.FromLineVoltageAndPower(
                                right.LineVoltageV, right.TotalActivePowerKw, right.TotalReactivePowerKvar,
                                ThreePhaseConnection.Star, _network.SystemFrequencyHz)
                            : new AcInternalQuantities();

                        PropagationPortBinding.SetAcVoltageInput(dual.Primary, unitClosed ? bus35V : 0, ThreePhaseConnection.Star);
                        PropagationPortBinding.SetAcQuantitiesInput(dual.SecondaryLeft, leftCurrent);
                        PropagationPortBinding.SetAcQuantitiesInput(dual.SecondaryRight, rightCurrent);
                        dual.Step(context, step);
                    }

                    continue;
                }

                if (u >= _network.UnitTransformers.Count)
                    continue;

                var bus690 = _graph.UnitBuses690[u];
                var unitCurrent = unitClosed && bus35V > 1.0
                    && (bus690.TotalLineCurrentA > 1e-6 || Math.Abs(bus690.TotalPhaseAngleDeg) > 1e-6)
                    ? AcQuantityConverter.FromLineVoltageAndPower(
                        bus690.LineVoltageV,
                        bus690.TotalActivePowerKw,
                        bus690.TotalReactivePowerKvar,
                        ThreePhaseConnection.Star,
                        _network.SystemFrequencyHz)
                    : new AcInternalQuantities
                    {
                        LineVoltageV = unitClosed && bus35V > 1.0 && bus690.LineVoltageV > 1.0
                            ? bus690.LineVoltageV
                            : 0
                    };

                PropagationPortBinding.SetAcVoltageInput(
                    _network.UnitTransformers[u].Primary,
                    unitClosed ? bus35V : 0,
                    ThreePhaseConnection.Star);
                PropagationPortBinding.SetAcQuantitiesInput(_network.UnitTransformers[u].Secondary, unitCurrent);
                _network.UnitTransformers[u].Step(context, step);
            }
        }

        /// <summary>汇总设备 Step 后的全站无功（并网点 Q-U 反馈，不含 Phase2 意图重复计数）。</summary>
        private double CollectFeedbackReactivePowerKvar()
        {
            double totalQ = _network.Load.Port.Output.Ac?.Internal.ReactivePowerKvar ?? 0;
            foreach (var pcs in _network.PcsDevices)
                totalQ += pcs.GetCurrentState().ReactivePower;
            return totalQ;
        }

        private void SolvePcsBmsPair(
            DeviceStepContext context,
            TimeSpan step,
            int channelIndex,
            double bus690V,
            bool gridAvailable)
        {
            var pcs = _network.PcsDevices[channelIndex];
            var bms = _network.BmsDevices[channelIndex];
            var link = _network.DcLinks[channelIndex];

            bms.IsLinked = link.IsClosed;
            pcs.SetGridAvailable(gridAvailable && bus690V > 1.0);

            PropagationPortBinding.SetAcVoltageInput(pcs.Ac, bus690V, ThreePhaseConnection.Star);
            pcs.Step(context, step);

            var dcCurrentA = pcs.Dc.Output.Dc?.CurrentA ?? 0;
            bms.ApplyDcInputFromPcs(dcCurrentA);
            bms.Step(context, step);

            var dcVoltage = bms.Port.Output.Dc ?? new DcSnapshot();
            pcs.Dc.Input = ElectricalPortSnapshot.FromDc(new DcSnapshot
            {
                VoltageV = link.IsClosed ? dcVoltage.VoltageV : 0,
                CurrentA = dcCurrentA
            });
            pcs.Step(context, step);
        }

        private void SamplePccMeter(TimeSpan integrationStep)
        {
            var primarySample = MeterBusSampler.Sample(
                _network,
                _graph,
                _network.PccMeter.Config.SourceBusId,
                _network.SystemFrequencyHz);
            _network.PccMeter.SampleFrom(primarySample, integrationStep);
        }

        private void PublishBusQuantities()
        {
            SetBusQuantity(RuntimeBusIds.Grid, _network.Grid.Port.Output.Ac!.Internal);
            SetBusQuantity(RuntimeBusIds.AfterMainBreaker, new AcInternalQuantities
            {
                Connection = ThreePhaseConnection.Star,
                LineVoltageV = _graph.BusAfterMainBreaker.LineVoltageV,
                LineCurrentA = _graph.BusAfterMainBreaker.TotalLineCurrentA,
                PhaseAngleDeg = _graph.BusAfterMainBreaker.TotalPhaseAngleDeg,
                FrequencyHz = _graph.BusAfterMainBreaker.FrequencyHz
            });
            SetBusQuantity(RuntimeBusIds.Station35, new AcInternalQuantities
            {
                Connection = ThreePhaseConnection.Star,
                LineVoltageV = _graph.Bus35.LineVoltageV,
                FrequencyHz = _network.SystemFrequencyHz
            });
        }

        private void SetBusQuantity(string busId, AcInternalQuantities qty)
        {
            var bus = _network.GetBus(busId);
            if (bus != null)
                bus.BusQuantity = qty;
        }

        private double EstimateBus35WhenMainOpen()
        {
            if (!NetworkControlBridge.IsBreakerClosed(_network.MainBreaker))
            {
                return EssIslandBusLogic.EstimateIslandedBus35LineVoltageV(
                    _ess._unitTransformers,
                    _ess._unitBreakers,
                    _ess._pcsList);
            }

            double max690 = 0;
            foreach (var xfmr in _network.UnitTransformers)
                max690 = Math.Max(max690, xfmr.Secondary.Output.Ac?.Internal.LineVoltageV ?? 0);

            if (max690 <= 1.0 || _network.UnitTransformers.Count == 0)
                return 0;

            double ratio = _pccCfg.StationBusNominalLineVoltage / Math.Max(_pcsCfg.AcVoltageNominal, 1.0);
            return max690 * ratio;
        }

        /// <summary>
        /// 主断分闸：Coupler 不再驱动主断。无反送则二次为 0；有 35kV 反送则二次为岛压，一次仍跟电网。
        /// </summary>
        private void StepMainBreakerIsolated(
            DeviceStepContext context,
            TimeSpan step,
            double island35V,
            double frequencyHz)
        {
            var brk = _network.MainBreaker;
            double gridV = _graph.BusGrid.LineVoltageV;
            double gridF = gridV > 1.0 ? _network.Grid.NominalFrequencyHz : 0;
            PropagationPortBinding.SetAcVoltageInput(brk.Primary, gridV, ThreePhaseConnection.Star, gridF);

            var secQty = island35V > 1.0
                ? new AcInternalQuantities
                {
                    Connection = ThreePhaseConnection.Star,
                    LineVoltageV = island35V,
                    LineCurrentA = 0,
                    FrequencyHz = frequencyHz
                }
                : new AcInternalQuantities { Connection = ThreePhaseConnection.Star };

            PropagationPortBinding.SetAcQuantitiesInput(brk.Secondary, secQty);
            brk.Step(context, step);
        }

        /// <summary>
        /// 主断分闸：主变与电网隔离。无反送则一次/二次均为 0；有 35kV 反送则一次按变比折算。
        /// </summary>
        private void StepMainTransformerIsolated(
            DeviceStepContext context,
            TimeSpan step,
            double island35V,
            double afterMainBreakerV,
            double frequencyHz)
        {
            var xf = _network.MainTransformer;
            var secQty = island35V > 1.0
                ? AcQuantityConverter.FromLineVoltageAndPower(
                    island35V,
                    _graph.Bus35.TotalActivePowerKw,
                    _graph.Bus35.TotalReactivePowerKvar,
                    ThreePhaseConnection.Star,
                    frequencyHz)
                : new AcInternalQuantities { Connection = ThreePhaseConnection.Star };

            PropagationPortBinding.SetAcVoltageInput(
                xf.Primary, afterMainBreakerV, ThreePhaseConnection.Star, frequencyHz);
            PropagationPortBinding.SetAcQuantitiesInput(xf.Secondary, secQty);
            xf.Step(context, step);
        }

        private DeviceStepContext BuildContext(DateTime simTime, TimeSpan step) =>
            new()
            {
                SimulationTime = simTime,
                Step = step,
                MainBreakerClosed = _network.MainBreaker.SwitchState.IsClosed
                    && !_network.MainBreaker.SwitchState.IsTripped,
                UtilityGridAvailable = _network.MainBreaker.SwitchState.IsClosed
            };
    }
}
