using log4net;

namespace EssSimulator.EssDeviceSimModel.Diagnostics
{
    /// <summary>PCS/BMS 状态变化结构化日志，便于联调溯源。</summary>
    public static class SimStateChangeLogger
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SimStateChangeLogger));

        public static void PcsModeChanged(string label, OperationMode from, OperationMode to, string reason)
        {
            if (from == to)
                return;
            Log.Info($"[PCS状态] {label} 运行模式 {FormatOpMode(from)}→{FormatOpMode(to)} | 原因: {reason}");
        }

        public static void PcsGridModeChanged(string label, GridMode from, GridMode to, string reason)
        {
            if (from == to)
                return;
            Log.Info($"[PCS状态] {label} 并离网 {FormatGridMode(from)}→{FormatGridMode(to)} | 原因: {reason}");
        }

        public static void PcsFaultChanged(string label, ushort faultType, string? message, bool cleared)
        {
            if (cleared)
            {
                Log.Info($"[PCS状态] {label} 故障已清除");
                return;
            }

            Log.Info($"[PCS状态] {label} 故障锁存 type={faultType} | {message?.Trim()}");
        }

        public static void PcsPowerSetpointChanged(
            string label, double fromP, double toP, double fromQ, double toQ)
        {
            if (!ShouldLogNumericChange(fromP, toP) && !ShouldLogNumericChange(fromQ, toQ))
                return;
            Log.Info(
                $"[PCS状态] {label} 功率设定 P {fromP:0.##}→{toP:0.##} kW  Q {fromQ:0.##}→{toQ:0.##} kvar");
        }

        public static void BreakerChanged(string label, bool fromClosed, bool toClosed)
        {
            if (fromClosed == toClosed)
                return;
            Log.Info($"[断路器] {label} {(fromClosed ? "合闸" : "分闸")}→{(toClosed ? "合闸" : "分闸")}");
        }

        public static void LoadSetpointChanged(string field, double from, double to)
        {
            if (!ShouldLogNumericChange(from, to))
                return;
            Log.Info($"[负载] {field}设定 {from:0.##}→{to:0.##}");
        }

        /// <summary>数值边沿：小于 epsilon 的抖动不打日志（避免爬坡/量化造成周期性刷屏）。</summary>
        public static bool ShouldLogNumericChange(double from, double to, double epsilon = 0.05) =>
            Math.Abs(from - to) >= epsilon;

        public static void BmsStateChanged(string label, string field, object from, object to, string reason)
        {
            if (Equals(from, to))
                return;
            Log.Info($"[BMS状态] {label} {field}: {from}→{to} | 原因: {reason}");
        }

        public static string FormatOpMode(OperationMode mode) => mode switch
        {
            OperationMode.Off => "停机(Off)",
            OperationMode.Standby => "待机(Standby)",
            OperationMode.Normal => "运行(Normal)",
            _ => mode.ToString()
        };

        public static string FormatGridMode(GridMode mode) => mode switch
        {
            GridMode.GridConnected => "并网",
            GridMode.Islanded => "离网",
            _ => mode.ToString()
        };

        public static string FormatGridConnectStatus(ushort status) => status switch
        {
            0 => "0(空闲)",
            1 => "1(并网中)",
            2 => "2(成功)",
            3 => "3(失败)",
            _ => status.ToString()
        };

        public static string FormatRackFault(ushort fault) => fault switch
        {
            0 => "0(无)",
            1 => "1(充电故障)",
            2 => "2(放电故障)",
            3 => "3(充放故障/其他)",
            _ => fault.ToString()
        };

        public static string FormatBlackStartStatus(ushort status) => status switch
        {
            0 => "0(未启动)",
            3 => "3(黑启动中)",
            4 => "4(进入失败)",
            5 => "5(已退出)",
            _ => status.ToString()
        };
    }
}
