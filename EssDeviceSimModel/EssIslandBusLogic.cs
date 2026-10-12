using EssSimulator.EssDeviceSimModel.Devices;

namespace EssSimulator.EssDeviceSimModel
{
    /// <summary>离网/黑启动场景下的母线电压估算。</summary>
    public static class EssIslandBusLogic
    {
        /// <summary>PCS 是否处于离网 V/f 建压（黑启动或孤岛电压有效）。</summary>
        public static bool IsPcsIslandVoltageBuilding(PcsState st)
        {
            if (st.Mode != OperationMode.Normal || st.GMode != GridMode.Islanded)
                return false;
            if (st.BlackStartEnabled)
                return st.BlackStartPhase is BlackStartPhase.SoftStarting
                    or BlackStartPhase.VoltageRegulating
                    or BlackStartPhase.Synchronized;
            return st.IslandVoltageEffectiveV > 1.0;
        }

        /// <summary>
        /// 在主断分闸时估算离网 35kV 母线电压：各单元正在构网的 PCS 取平均 690V，
        /// 再按单元变变比折到 35kV；多单元取其中较高的反送。
        /// </summary>
        public static double EstimateIslandedBus35LineVoltageV(
            IReadOnlyList<TransformerDevice> unitTransformers,
            IReadOnlyList<Breaker> unitBreakers,
            IReadOnlyList<PcsDevice> pcsList,
            IReadOnlyList<int>? pcsPerUnit = null)
        {
            double bus35 = 0;
            for (int u = 0; u < unitTransformers.Count; u++)
            {
                if (u >= unitBreakers.Count || !unitBreakers[u].IsClosed)
                    continue;

                var (baseIdx, pcsCount) = PcsUnitLayout.RangeOfUnit(pcsPerUnit, u);
                double sum690 = 0;
                int forming = 0;
                void Acc(int idx)
                {
                    if (idx < 0 || idx >= pcsList.Count) return;
                    var st = pcsList[idx].GetCurrentState();
                    if (!IsPcsIslandVoltageBuilding(st) || st.AcVoltage <= 1.0) return;
                    sum690 += st.AcVoltage;
                    forming++;
                }

                for (int ch = 0; ch < pcsCount; ch++)
                    Acc(baseIdx + ch);
                if (forming == 0)
                    continue;
                double lv690 = sum690 / forming;

                bus35 = Math.Max(bus35, lv690 * unitTransformers[u].TurnsRatio);
            }
            return bus35;
        }

        /// <summary>
        /// 并机公共参考：对正在注入的源做频率算术平均、相位圆周平均。
        /// </summary>
        public static (double FrequencyHz, double PhaseRad) AverageFormingReference(
            IReadOnlyList<(double VoltageV, double FrequencyHz, double PhaseRad)> sources)
        {
            double sumF = 0;
            double sumSin = 0;
            double sumCos = 0;
            int n = 0;
            foreach (var (v, f, th) in sources)
            {
                if (v <= 1.0)
                    continue;
                sumF += f > 1.0 ? f : 0;
                sumSin += Math.Sin(th);
                sumCos += Math.Cos(th);
                n++;
            }

            if (n == 0)
                return (0, 0);
            return (sumF / n, Math.Atan2(sumSin, sumCos));
        }

        /// <summary>正在注入的构网源的孤岛电压设定算术平均（忽略 ≤0）。</summary>
        public static double AverageFormingVoltageCommand(IReadOnlyList<double> commands)
        {
            double sum = 0;
            int n = 0;
            foreach (var cmd in commands)
            {
                if (cmd <= 1.0)
                    continue;
                sum += cmd;
                n++;
            }

            return n > 0 ? sum / n : 0;
        }
    }
}
