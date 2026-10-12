namespace EssSimulator.LocalControl
{
    /// <summary>
    /// 黑启动 LC 直流通道运行状态：0 关机，1 运行，2 待机。
    /// 开机且有功、无功均为 0 为待机，其余开机状态为运行。
    /// </summary>
    internal static class DcChannelRunStatus
    {
        public const int Off = 0;
        public const int Running = 1;
        public const int Standby = 2;

        public static int Encode(bool isOn, double activePower, double reactivePower)
        {
            if (!isOn)
                return Off;
            if (activePower == 0 && reactivePower == 0)
                return Standby;
            return Running;
        }
    }
}
