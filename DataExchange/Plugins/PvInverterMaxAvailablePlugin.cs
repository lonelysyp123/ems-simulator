using System;
using EssSimulator.Core;
using EssSimulator.DataExchange.Adapters;
using EssSimulator.EssSimModelApi.Mappers;

namespace EssSimulator.DataExchange.Plugins
{
    /// <summary>
    /// 逆变器最大可发有功（kW）：取模型按 MPPT/组串 IV 可发量算出的
    /// <c>AvailableAcPowerKw</c>，再按额定功率和**单机最大放电上限**封顶，
    /// 与 UI 的 <c>availableAcPowerKw</c>、Logger 的 <c>yc20</c> 口径一致。
    /// 点表用 <c>model=plugin|arg1=InverterMaxAvailableKw|arg2=pvN.Inverters[i]</c> 绑定。
    /// 不用瞬时 MPPT 电压×电流求和：限发时那是当前出力，不是最大可发。
    /// </summary>
    public sealed class PvInverterMaxAvailablePlugin : ITelemetryPlugin
    {
        public const string WordKey = "InverterMaxAvailableKw";

        public bool CanHandle(string wordKey) =>
            string.Equals(wordKey, WordKey, StringComparison.OrdinalIgnoreCase);

        public object? Compute(string wordKey, string deviceRoot, ISimulationDataAdapter simulation)
        {
            if (string.IsNullOrWhiteSpace(deviceRoot))
                return null;

            double available;
            double rated;
            if (TryParseDeviceRoot(deviceRoot, out int pv1Based, out int inverter0Based) &&
                TryGetInverter(pv1Based, inverter0Based, out double modelAvailable, out double modelRated))
            {
                available = modelAvailable;
                rated = modelRated;
            }
            else if (!TryReadDouble(simulation, $"{deviceRoot}.AvailableAcPowerKw", out available) ||
                     !TryReadDouble(simulation, $"{deviceRoot}.RatedPowerKw", out rated))
            {
                return null;
            }

            double max = Math.Min(Math.Max(0, available), Math.Max(0, rated));
            if (pv1Based > 0 &&
                DeviceControlFacade.TryReadPvInverterMaxDischarge(pv1Based, inverter0Based, out double capKw))
            {
                max = Math.Min(max, Math.Max(0, capKw));
            }

            return double.IsFinite(max) ? max : null;
        }

        private static bool TryGetInverter(int pv1Based, int inverter0Based, out double available, out double rated)
        {
            available = 0;
            rated = 0;
            var ess = SimulatorHost.Instance.TryGetEss();
            if (ess == null || pv1Based < 1 || pv1Based > ess.PvUnits.Count)
                return false;

            var inverters = ess.PvUnits[pv1Based - 1].Inverters;
            if (inverter0Based < 0 || inverter0Based >= inverters.Count)
                return false;

            available = inverters[inverter0Based].AvailableAcPowerKw;
            rated = inverters[inverter0Based].RatedPowerKw;
            return true;
        }

        private static bool TryParseDeviceRoot(string deviceRoot, out int pv1Based, out int inverter0Based)
        {
            pv1Based = 0;
            inverter0Based = 0;
            const string marker = ".Inverters[";
            int dot = deviceRoot.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (dot <= 2 || !deviceRoot.StartsWith("pv", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!int.TryParse(deviceRoot.AsSpan(2, dot - 2), out pv1Based) || pv1Based < 1)
                return false;

            int start = dot + marker.Length;
            int end = deviceRoot.IndexOf(']', start);
            return end > start &&
                   int.TryParse(deviceRoot.AsSpan(start, end - start), out inverter0Based) &&
                   inverter0Based >= 0;
        }

        private static bool TryReadDouble(ISimulationDataAdapter simulation, string path, out double value)
        {
            value = 0;
            try
            {
                object? raw = simulation.Read(path);
                if (raw == null)
                    return false;

                value = raw switch
                {
                    double d => d,
                    float f => f,
                    _ => Convert.ToDouble(raw)
                };
                return double.IsFinite(value);
            }
            catch
            {
                return false;
            }
        }
    }
}
