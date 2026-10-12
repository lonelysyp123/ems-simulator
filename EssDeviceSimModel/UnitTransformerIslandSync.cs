using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel.Devices;

namespace EssSimulator.EssDeviceSimModel
{
    /// <summary>
    /// 离网/黑启动场景下单元变与 35kV 母线耦合，以及站用电在在线 PCS 间的分摊。
    /// 在 PCS.Update 之后调用，使变压器状态与当步 PCS 输出同 tick 对齐。
    /// </summary>
    public static class UnitTransformerIslandSync
    {
        public static void SyncAfterPcsUpdate(
            bool mainBreakerClosed,
            double stationBus35LineVoltageV,
            IReadOnlyList<TransformerDevice> unitTransformers,
            TransformerDevice mainTransformer,
            IReadOnlyList<PcsDevice> pcsList,
            Func<int, bool> isUnitBreakerClosed,
            PcsPhysicalConfig pcsCfg,
            IReadOnlyList<int>? pcsPerUnit,
            DateTime simTime,
            TimeSpan simStep)
        {
            int unitCount = unitTransformers.Count;
            if (unitCount == 0)
                return;

            var unitHvClosed = new bool[unitCount];
            var localUnitP = new double[unitCount];
            var localUnitQ = new double[unitCount];
            var localLv690 = new double[unitCount];
            var unitPrimaryV = new double[unitCount];

            double sharedBus35kVFromIsland = 0;

            for (int u = 0; u < unitCount; u++)
            {
                var (baseIdx, pcsCount) = PcsUnitLayout.RangeOfUnit(pcsPerUnit, u);
                unitHvClosed[u] = isUnitBreakerClosed(u);
                if (!unitHvClosed[u])
                    continue;

                double sum690 = 0;
                int forming = 0;
                void Accumulate(int pcsIdx)
                {
                    if (pcsIdx < 0 || pcsIdx >= pcsList.Count) return;
                    var st = pcsList[pcsIdx].GetCurrentState();
                    if (!EssIslandBusLogic.IsPcsIslandVoltageBuilding(st) || st.AcVoltage <= 1.0) return;
                    sum690 += st.AcVoltage;
                    forming++;
                    localUnitP[u] += pcsList[pcsIdx].GetGridSideActivePower();
                    localUnitQ[u] += st.ReactivePower;
                }

                for (int ch = 0; ch < pcsCount; ch++)
                    Accumulate(baseIdx + ch);
                if (forming > 0)
                    localLv690[u] = sum690 / forming;

                if (mainBreakerClosed || localLv690[u] <= 0)
                    continue;

                sharedBus35kVFromIsland = Math.Max(
                    sharedBus35kVFromIsland, localLv690[u] * unitTransformers[u].TurnsRatio);
            }

            for (int u = 0; u < unitCount; u++)
            {
                if (!unitHvClosed[u])
                {
                    unitTransformers[u].Update(0, 0, 1.0, 0, 0, simTime, simStep);
                    continue;
                }

                double turnsRatio = unitTransformers[u].TurnsRatio;

                unitPrimaryV[u] = mainBreakerClosed
                    ? stationBus35LineVoltageV
                    : sharedBus35kVFromIsland;

                if (unitPrimaryV[u] <= 0)
                {
                    unitTransformers[u].Update(0, 0, 1.0, 0, 0, simTime, simStep);
                    continue;
                }

                double secV = Math.Max(unitPrimaryV[u] / Math.Max(turnsRatio, 1e-6), 1.0);
                double unitS = Math.Sqrt(localUnitP[u] * localUnitP[u] + localUnitQ[u] * localUnitQ[u]);
                double unitPf = unitS > 0 ? localUnitP[u] / unitS : 1.0;
                double unitSecCurrentMag = unitS * 1000.0 / (secV * Math.Sqrt(3.0));
                double unitSecCurrent = Math.Abs(localUnitP[u]) > 1e-6
                    ? (localUnitP[u] >= 0 ? -unitSecCurrentMag : unitSecCurrentMag)
                    : unitSecCurrentMag;

                unitTransformers[u].Update(
                    unitPrimaryV[u], unitSecCurrent, unitPf, unitS, localUnitQ[u], simTime, simStep,
                    applyReactiveVoltageShift: true);
            }

            ApplyBlackStartStationElectricalLoadAcrossBus(
                unitTransformers, mainTransformer, pcsList, pcsCfg,
                localUnitP, unitPrimaryV, mainBreakerClosed, stationBus35LineVoltageV, pcsPerUnit);
        }

        private static void ApplyBlackStartStationElectricalLoadAcrossBus(
            IReadOnlyList<TransformerDevice> unitTransformers,
            TransformerDevice mainTransformer,
            IReadOnlyList<PcsDevice> pcsList,
            PcsPhysicalConfig pcsCfg,
            double[] localUnitP,
            double[] unitPrimaryV,
            bool mainBreakerClosed,
            double stationBus35LineVoltageV,
            IReadOnlyList<int>? pcsPerUnit)
        {
            double totalMagQ = 0;
            double totalLossP = 0;
            double lineCoeff = Math.Clamp(pcsCfg.GridLossCoefficient, 0, 0.5);

            for (int u = 0; u < unitTransformers.Count; u++)
            {
                if (u >= unitPrimaryV.Length || unitPrimaryV[u] <= 0)
                    continue;
                var xf = unitTransformers[u];
                totalMagQ += xf.GetSecondaryMagnetizingReactiveKvar();
                double ironKw = xf.GetSecondaryNoLoadActivePowerKw();
                double lineKw = Math.Abs(localUnitP[u]) * lineCoeff / Math.Max(1e-6, 1.0 - lineCoeff);
                totalLossP += ironKw + lineKw;
            }

            if (!mainBreakerClosed && stationBus35LineVoltageV > 1.0)
                totalMagQ += mainTransformer.GetSecondaryMagnetizingReactiveKvar();

            for (int i = 0; i < pcsList.Count; i++)
            {
                pcsList[i].SetTransformerMagnetizingReactiveKvar(0);
                pcsList[i].SetBlackStartSharedLossActivePowerKw(0);
                pcsList[i].SetBlackStartInrushDemand(0, 0);
            }

            var weights = new double[pcsList.Count];
            bool leaderOnly = string.Equals(
                pcsCfg.BlackStartSteadyLossShareMode, "LeaderOnly", StringComparison.OrdinalIgnoreCase);

            int leaderIdx = -1;
            double leaderV = -1;
            for (int i = 0; i < pcsList.Count; i++)
            {
                if (!pcsList[i].TakesIslandStationLoad)
                    continue;
                if (leaderOnly)
                {
                    double v = pcsList[i].GetCurrentState().AcVoltage;
                    if (!pcsList[i].TryGetIslandBusVoltageInjection(out var inj, out _) || inj <= 1.0)
                        inj = v;
                    if (inj > leaderV)
                    {
                        leaderV = inj;
                        leaderIdx = i;
                    }
                }
                else
                    weights[i] = pcsList[i].BlackStartStationLoadShare;
            }

            if (leaderOnly && leaderIdx >= 0)
                weights[leaderIdx] = 1.0;

            double weightSum = 0;
            for (int i = 0; i < weights.Length; i++)
                weightSum += Math.Max(0, weights[i]);

            if (weightSum <= 1e-9)
                return;

            ApplyUnitTransformerInrushDemand(unitTransformers, unitPrimaryV, pcsList, weights, pcsPerUnit);

            for (int i = 0; i < pcsList.Count; i++)
            {
                double w = Math.Max(0, weights[i]);
                if (w <= 1e-12)
                    continue;
                pcsList[i].SetTransformerMagnetizingReactiveKvar(totalMagQ * w / weightSum);
                pcsList[i].SetBlackStartSharedLossActivePowerKw(totalLossP * w / weightSum);
            }
        }

        private static void ApplyUnitTransformerInrushDemand(
            IReadOnlyList<TransformerDevice> unitTransformers,
            double[] unitPrimaryV,
            IReadOnlyList<PcsDevice> pcsList,
            double[] weights,
            IReadOnlyList<int>? pcsPerUnit)
        {
            var inrushP = new double[pcsList.Count];
            var inrushQ = new double[pcsList.Count];
            double globalW = 0;
            for (int i = 0; i < weights.Length; i++)
                globalW += Math.Max(0, weights[i]);
            if (globalW <= 1e-9)
                return;

            for (int u = 0; u < unitTransformers.Count; u++)
            {
                if (u >= unitPrimaryV.Length || unitPrimaryV[u] <= 0)
                    continue;

                var (pInrush, qInrush) = unitTransformers[u].GetInrushDemandKwKvar();
                if (pInrush <= 1e-6 && qInrush <= 1e-6)
                    continue;

                var unitWeights = new double[pcsList.Count];
                double unitW = 0;
                var (baseIdx, pcsCount) = PcsUnitLayout.RangeOfUnit(pcsPerUnit, u);
                for (int ch = 0; ch < pcsCount; ch++)
                {
                    int idx = baseIdx + ch;
                    if (idx < 0 || idx >= pcsList.Count)
                        continue;
                    double w = Math.Max(0, weights[idx]);
                    unitWeights[idx] = w;
                    unitW += w;
                }

        // 本单元尚无构网 PCS 时，空载变涌流不派给其他单元（由稳态励磁 MagQ 承接）。
                if (unitW <= 1e-9)
                    continue;
                double denom = unitW;

                for (int i = 0; i < pcsList.Count; i++)
                {
                    double w = unitWeights[i];
                    if (w <= 1e-12)
                        continue;
                    inrushP[i] += pInrush * w / denom;
                    inrushQ[i] += qInrush * w / denom;
                }
            }

            for (int i = 0; i < pcsList.Count; i++)
            {
                if (inrushP[i] > 0 || inrushQ[i] > 0)
                    pcsList[i].SetBlackStartInrushDemand(inrushP[i], inrushQ[i]);
            }
        }
    }
}
