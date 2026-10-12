using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Interface;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.EssDeviceSimModel.Solver
{
    public sealed class NetworkSolver : INetworkSolver
    {
        private readonly ElectricalNetwork _network;
        private readonly PccConfig _pccCfg;
        private readonly PcsPhysicalConfig _pcsCfg;
        private readonly EnergyStorageSystem? _legacyEss;

        public NetworkSolver(
            ElectricalNetwork network,
            PccConfig pccCfg,
            PcsPhysicalConfig pcsCfg,
            EnergyStorageSystem? legacyEss = null)
        {
            _network = network;
            _pccCfg = pccCfg;
            _pcsCfg = pcsCfg;
            _legacyEss = legacyEss;
        }

        public void Step(TimeSpan step, TimeSpan meterIntegrationStep)
        {
            var context = BuildContext();
            double bus35V = _network.StationBus35LineVoltageV > 1.0
                ? _network.StationBus35LineVoltageV
                : _pccCfg.StationBusNominalLineVoltage;

            // S1: 负载意图（35kV 母线电压用上一步或额定）
            SetAcInput(_network.Load.Port, bus35V, ThreePhaseConnection.Star, context, _network.SystemFrequencyHz);
            _network.Load.Step(context, step);
            double totalActiveKw = _network.Load.Port.Output.Ac!.Internal.ActivePowerKw;
            double totalReactiveKvar = _network.Load.Port.Output.Ac!.Internal.ReactivePowerKvar;

            // S1: PCS / 光伏功率意图（使用当前 Output 或 0）
            CollectPcsPower(ref totalActiveKw, ref totalReactiveKvar);
            CollectPvPower(ref totalActiveKw, ref totalReactiveKvar);

            // S2: 电网 Q-U + 主变路径
            _network.Grid.SetAggregatedReactivePowerKvar(totalReactiveKvar);
            _network.Grid.Step(context, step);
            SystemFrequencyResolver.Refresh(_network, context);

            double gridVoltage = _network.Grid.Port.Output.Ac!.Internal.LineVoltageV;
            WireMainPath(context, step, gridVoltage, totalActiveKw, totalReactiveKvar, ref bus35V);

            // S3/S4: 单元支路与 PCS
            SolveUnitBranches(context, step, bus35V);

            // S6: Q 反馈修正（一次迭代）
            CollectPcsPower(ref totalActiveKw, ref totalReactiveKvar);
            CollectPvPower(ref totalActiveKw, ref totalReactiveKvar);
            totalActiveKw += _network.Load.Port.Output.Ac!.Internal.ActivePowerKw;
            totalReactiveKvar += _network.Load.Port.Output.Ac!.Internal.ReactivePowerKvar;
            _network.Grid.SetAggregatedReactivePowerKvar(totalReactiveKvar);
            _network.Grid.Step(context, step);
            gridVoltage = _network.Grid.Port.Output.Ac!.Internal.LineVoltageV;
            SystemFrequencyResolver.Refresh(_network, context);
            if (context.MainBreakerClosed)
            {
                _network.PccLineVoltageV = gridVoltage;
                _network.StationBus35LineVoltageV = GridFeedbackConventions.DeriveStationBusVoltage(
                    gridVoltage,
                    _pccCfg.NominalLineVoltage,
                    _pccCfg.StationBusNominalLineVoltage);
            }
            else
            {
                _network.PccLineVoltageV = 0;
            }

            // S8: 电表按组态抽头采样
            var primarySample = MeterBusSampler.Sample(
                _network,
                graph: null,
                _network.PccMeter.Config.SourceBusId,
                _network.SystemFrequencyHz);
            _network.PccMeter.SampleFrom(primarySample, meterIntegrationStep);
            PublishBusQuantities();
        }

        private DeviceStepContext BuildContext() =>
            new()
            {
                SimulationTime = DateTime.Now,
                MainBreakerClosed = _network.MainBreaker.SwitchState.IsClosed && !_network.MainBreaker.SwitchState.IsTripped,
                UtilityGridAvailable = _network.MainBreaker.SwitchState.IsClosed
            };

        private void WireMainPath(
            DeviceStepContext context,
            TimeSpan step,
            double gridVoltage,
            double totalActiveKw,
            double totalReactiveKvar,
            ref double bus35V)
        {
            var secCurrent = AcQuantityConverter.FromLineVoltageAndPower(
                bus35V,
                totalActiveKw,
                totalReactiveKvar,
                ThreePhaseConnection.Star,
                _network.SystemFrequencyHz);

            SetAcInput(_network.MainBreaker.Primary, gridVoltage, ThreePhaseConnection.Star, context, _network.SystemFrequencyHz);
            SetAcInput(_network.MainBreaker.Secondary, _network.MainBreaker.ReferToRated(secCurrent), ThreePhaseConnection.Star, context);
            _network.MainBreaker.Step(context, step);

            double downstreamV = _network.MainBreaker.SwitchState.IsClosed
                ? _network.MainBreaker.Secondary.Output.Ac!.Internal.LineVoltageV
                : 0;

            if (_network.HasMainTransformer)
            {
                SetAcInput(_network.MainTransformer.Primary, downstreamV, ThreePhaseConnection.Star, context, _network.SystemFrequencyHz);
                SetAcInput(_network.MainTransformer.Secondary, secCurrent, ThreePhaseConnection.Star, context);
                _network.MainTransformer.Step(context, step);
            }

            if (context.MainBreakerClosed)
            {
                bus35V = _network.HasMainTransformer
                    ? _network.MainTransformer.Secondary.Output.Ac!.Internal.LineVoltageV
                    : downstreamV;
                _network.PccLineVoltageV = gridVoltage;
                _network.StationBus35LineVoltageV = _network.HasMainTransformer
                    ? GridFeedbackConventions.DeriveStationBusVoltage(
                        gridVoltage,
                        _pccCfg.NominalLineVoltage,
                        _pccCfg.StationBusNominalLineVoltage)
                    : downstreamV;
            }
            else
            {
                _network.PccLineVoltageV = 0;
                _network.StationBus35LineVoltageV = EstimateBus35WhenMainOpen();
            }
        }

        private void SolveUnitBranches(DeviceStepContext context, TimeSpan step, double bus35V)
        {
            for (int u = 0; u < _network.UnitTransformers.Count; u++)
            {
                var unitBreaker = _network.UnitBreakers[u];
                var unitTransformer = _network.UnitTransformers[u];
                bool unitClosed = unitBreaker.SwitchState.IsClosed && !unitBreaker.SwitchState.IsTripped;

                double unitP = 0;
                double unitQ = 0;
                var (baseChannel, pcsCount) = PcsUnitLayout.RangeOfUnit(_network.PcsPerUnit, u);
                for (int ch = 0; ch < pcsCount; ch++)
                {
                    int idx = baseChannel + ch;
                    if (TryGetPcsPower(idx, out double pKw, out double qKvar))
                    {
                        unitP += pKw;
                        unitQ += qKvar;
                    }
                    else if (idx < _network.PcsDevices.Count)
                    {
                        var ac = _network.PcsDevices[idx].Ac.Output.Ac?.Internal;
                        if (ac == null) continue;
                        unitP += ac.ActivePowerKw;
                        unitQ += ac.ReactivePowerKvar;
                    }
                }

                var unitCurrent = AcQuantityConverter.FromLineVoltageAndPower(
                    _pcsCfg.AcVoltageNominal,
                    unitP,
                    unitQ,
                    ThreePhaseConnection.Star,
                    _network.SystemFrequencyHz);

                SetAcInput(unitBreaker.Primary, bus35V, ThreePhaseConnection.Star, context, _network.SystemFrequencyHz);
                SetAcInput(unitBreaker.Secondary, unitBreaker.ReferToRated(unitCurrent), ThreePhaseConnection.Star, context);
                unitBreaker.Step(context, step);

                double primaryV = unitClosed ? bus35V : 0;
                SetAcInput(unitTransformer.Primary, primaryV, ThreePhaseConnection.Star, context, _network.SystemFrequencyHz);
                SetAcInput(unitTransformer.Secondary, unitCurrent, ThreePhaseConnection.Star, context);
                unitTransformer.Step(context, step);

                double bus690V = unitClosed
                    ? unitTransformer.Secondary.Output.Ac!.Internal.LineVoltageV
                    : 0;

                for (int ch = 0; ch < pcsCount; ch++)
                {
                    int idx = baseChannel + ch;
                    if (idx >= _network.PcsDevices.Count) continue;
                    SolvePcsBmsPair(context, step, idx, bus690V, unitClosed && context.MainBreakerClosed);
                }
            }
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

            SetAcInput(pcs.Ac, bus690V, ThreePhaseConnection.Star, context, _network.SystemFrequencyHz);
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

        private void CollectPcsPower(ref double totalActiveKw, ref double totalReactiveKvar)
        {
            foreach (var pcs in _network.PcsDevices)
            {
                var st = pcs.GetCurrentState();
                totalActiveKw += pcs.GetGridSideActivePower();
                totalReactiveKvar += st.ReactivePower;
            }
        }

        private void CollectPvPower(ref double totalActiveKw, ref double totalReactiveKvar)
        {
            if (_legacyEss == null)
                return;
            foreach (var pv in _legacyEss.PvUnits)
            {
                totalActiveKw += pv.HvActivePowerKw;
                totalReactiveKvar += pv.HvReactivePowerKvar;
            }
        }

        private bool TryGetPcsPower(int channelIndex, out double activeKw, out double reactiveKvar)
        {
            activeKw = 0;
            reactiveKvar = 0;
            if (channelIndex < 0 || channelIndex >= _network.PcsDevices.Count)
                return false;

            var pcs = _network.PcsDevices[channelIndex];
            var st = pcs.GetCurrentState();
            activeKw = pcs.GetGridSideActivePower();
            reactiveKvar = st.ReactivePower;
            return true;
        }

        private double EstimateBus35WhenMainOpen()
        {
            if (!NetworkControlBridge.IsBreakerClosed(_network.MainBreaker))
            {
                if (_legacyEss != null)
                {
                    return EssIslandBusLogic.EstimateIslandedBus35LineVoltageV(
                        _legacyEss._unitTransformers,
                        _legacyEss._unitBreakers,
                        _legacyEss._pcsList,
                        _legacyEss.PcsPerUnit);
                }
            }

            return EstimateIslandBus35Voltage();
        }

        private double EstimateIslandBus35Voltage()
        {
            double max690 = 0;
            foreach (var xfmr in _network.UnitTransformers)
            {
                max690 = Math.Max(max690, xfmr.Secondary.Output.Ac?.Internal.LineVoltageV ?? 0);
            }

            if (max690 <= 1.0 || _network.UnitTransformers.Count == 0)
                return 0;

            double ratio = _pccCfg.StationBusNominalLineVoltage / Math.Max(_pcsCfg.AcVoltageNominal, 1.0);
            return max690 * ratio;
        }

        private void PublishBusQuantities()
        {
            SetBusQuantity(RuntimeBusIds.Grid, _network.Grid.Port.Output.Ac!.Internal);
            SetBusQuantity(RuntimeBusIds.AfterMainBreaker, new AcInternalQuantities
            {
                Connection = ThreePhaseConnection.Star,
                LineVoltageV = contextMainClosed()
                    ? (_network.HasMainTransformer
                        ? _network.MainTransformer.Primary.Output.Ac?.Internal.LineVoltageV ?? 0
                        : _network.MainBreaker.Secondary.Output.Ac?.Internal.LineVoltageV ?? 0)
                    : 0,
                FrequencyHz = _network.SystemFrequencyHz
            });
            SetBusQuantity(RuntimeBusIds.Station35, new AcInternalQuantities
            {
                Connection = ThreePhaseConnection.Star,
                LineVoltageV = _network.StationBus35LineVoltageV,
                FrequencyHz = _network.SystemFrequencyHz
            });

            bool contextMainClosed() =>
                _network.MainBreaker.SwitchState.IsClosed && !_network.MainBreaker.SwitchState.IsTripped;
        }

        private void SetBusQuantity(string busId, AcInternalQuantities qty)
        {
            var bus = _network.GetBus(busId);
            if (bus != null)
                bus.BusQuantity = qty;
        }

        private static void SetAcInput(
            ElectricalPort port,
            double lineVoltageV,
            ThreePhaseConnection connection,
            DeviceStepContext context,
            double systemFrequencyHz)
        {
            port.Input = ElectricalPortSnapshot.FromAc(new AcInternalQuantities
            {
                Connection = connection,
                LineVoltageV = lineVoltageV,
                FrequencyHz = lineVoltageV > 1.0 ? systemFrequencyHz : 0
            });
        }

        private static void SetAcInput(
            ElectricalPort port,
            AcInternalQuantities currentIntent,
            ThreePhaseConnection connection,
            DeviceStepContext context)
        {
            port.Input = ElectricalPortSnapshot.FromAc(new AcInternalQuantities
            {
                Connection = connection,
                LineVoltageV = currentIntent.LineVoltageV,
                LineCurrentA = currentIntent.LineCurrentA,
                PhaseAngleDeg = currentIntent.PhaseAngleDeg,
                FrequencyHz = currentIntent.FrequencyHz
            });
        }
    }
}
