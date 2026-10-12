using EssSimulator.Configuration;

namespace EssSimulator.EssDeviceSimModel.Solver
{
    /// <summary>电气网络求解步编排：同步控制意图 → Solver → 回写电网/PCS 网侧状态。</summary>
    public static class NetworkStepOrchestrator
    {
        /// <summary>
        /// Solver 夹具步进前同步。生产路径由 <c>RadialPowerSweepEngine.SolveCycle</c> 自行同步，勿再从 PlantEngine 调用。
        /// </summary>
        public static void SyncBeforeSolverStep(ElectricalNetwork network, EnergyStorageSystem ess, DateTime simTime)
        {
            NetworkControlBridge.SyncLoadPlan(network, ess._loadDevice, simTime);
            NetworkControlBridge.SyncBmsLinksFromRacks(network, ess._bmsRackDevices);
        }

        public static void SolverPrimaryStep(
            ElectricalNetwork network,
            EnergyStorageSystem ess,
            DateTime simTime,
            TimeSpan step,
            TimeSpan meterIntegrationStep,
            PcsPhysicalConfig pcsCfg)
        {
            SyncBeforeSolverStep(network, ess, simTime);
            if (network.Solver == null)
                throw new InvalidOperationException("NetworkSolver 仅测试夹具使用，生产路径请走 RadialPowerSweepEngine");
            network.Solver.Step(step, meterIntegrationStep);
            ApplyGridResultsToEnergyStorageSystem(network, ess, simTime, step, pcsCfg);
            NetworkControlBridge.ProjectBreakersToLegacy(network, ess);
        }

        public static void ApplyGridResultsToEnergyStorageSystem(
            ElectricalNetwork network,
            EnergyStorageSystem ess,
            DateTime simTime,
            TimeSpan step,
            PcsPhysicalConfig pcsCfg)
        {
            ess.ApplyNetworkGridVoltages(network.PccLineVoltageV, network.StationBus35LineVoltageV);
            ess._loadDevice.ComputeLoadCurrentA(network.StationBus35LineVoltageV, simTime);

            bool mainClosed = network.MainBreaker.SwitchState.IsClosed
                && !network.MainBreaker.SwitchState.IsTripped;

            ApplyMainTransformerFromNetwork(network, ess, simTime, step, mainClosed);
            ApplyUnitBranchesFromNetwork(network, ess, pcsCfg, mainClosed);
        }

        private static void ApplyMainTransformerFromNetwork(
            ElectricalNetwork network,
            EnergyStorageSystem ess,
            DateTime simTime,
            TimeSpan step,
            bool mainClosed)
        {
            var sec = network.MainTransformer.Secondary.Output.Ac?.Internal;
            if (sec == null)
                return;

            double apparentKva = Math.Sqrt(sec.ActivePowerKw * sec.ActivePowerKw + sec.ReactivePowerKvar * sec.ReactivePowerKvar);
            double powerFactor = apparentKva > 0 ? sec.ActivePowerKw / apparentKva : 1.0;

            if (mainClosed)
            {
                if (network.HasMainTransformer)
                    ess._mainTransformer.OverrideSecondaryVoltage(network.StationBus35LineVoltageV);
                return;
            }

            if (!network.HasMainTransformer)
                return;

            if (network.StationBus35LineVoltageV > 1.0)
            {
                ess._mainTransformer.RefreshIslandReverseExcitation(
                    network.StationBus35LineVoltageV,
                    sec.LineCurrentA,
                    powerFactor,
                    apparentKva,
                    sec.ReactivePowerKvar,
                    simTime,
                    step);
                return;
            }

            if (ess._mainTransformer._currentState.PrimaryVoltage > 1.0)
                ess._mainTransformer.Update(0, 0, powerFactor, apparentKva, sec.ReactivePowerKvar, simTime, step, applyReactiveVoltageShift: false);
            ess._mainTransformer.OverrideSecondaryVoltage(0);
            ess._mainTransformer.WriteDeenergizedPorts();
        }

        private static void ApplyUnitBranchesFromNetwork(
            ElectricalNetwork network,
            EnergyStorageSystem ess,
            PcsPhysicalConfig pcsCfg,
            bool mainClosed)
        {
            for (int u = 0; u < ess._unitTransformers.Count; u++)
            {
                int baseIdx = ess.PcsBaseIndexOfUnit(u);
                int count = ess.PcsCountOfUnit(u);

                if (u >= network.UnitBreakers.Count || u >= network.UnitTransformers.Count)
                {
                    DeenergizeUnitPcs(ess, baseIdx, count);
                    continue;
                }

                bool unitClosed = network.UnitBreakers[u].SwitchState.IsClosed
                    && !network.UnitBreakers[u].SwitchState.IsTripped;

                if (!unitClosed)
                {
                    DeenergizeUnitPcs(ess, baseIdx, count);
                    continue;
                }

                var unitSec = network.UnitTransformers[u].Secondary.Output.Ac?.Internal;
                if (unitSec == null)
                    continue;

                if (network.StationBus35LineVoltageV <= 1.0 && unitSec.LineVoltageV <= 1.0)
                {
                    DeenergizeUnitPcs(ess, baseIdx, count);
                    continue;
                }

                double lv690 = unitSec.LineVoltageV;
                bool gridAvailable = mainClosed && lv690 > pcsCfg.AcVoltageNominal * 0.1;

                double gridFreq = network.SystemFrequencyHz;
                var duals = network.DualEarsOfUnit(u);
                var assignment = u < network.SplitEarAssignments.Count ? network.SplitEarAssignments[u] : null;
                for (int ch = 0; ch < count; ch++)
                {
                    int idx = baseIdx + ch;
                    if (idx >= ess._pcsList.Count)
                        continue;
                    double chV = lv690;
                    if (duals.Count > 0 && assignment != null && assignment.TryGetEar(idx, out int xfmrIndex, out bool right)
                        && xfmrIndex >= 0 && xfmrIndex < duals.Count)
                    {
                        var dual = duals[xfmrIndex];
                        chV = (right ? dual.SecondaryRight : dual.SecondaryLeft).Output.Ac?.Internal.LineVoltageV ?? lv690;
                    }
                    bool chGrid = mainClosed && chV > pcsCfg.AcVoltageNominal * 0.1;
                    ess._pcsList[idx].UpdateGridState(chV, gridFreq, chGrid);
                }
            }
        }

        private static void DeenergizeUnitPcs(EnergyStorageSystem ess, int baseIdx, int count)
        {
            for (int ch = 0; ch < count; ch++)
            {
                int idx = baseIdx + ch;
                if (idx < ess._pcsList.Count)
                    ess.ApplyPcsGridWhenUnitDeenergized(idx, ess._pcsList[idx]);
            }
        }
    }
}
