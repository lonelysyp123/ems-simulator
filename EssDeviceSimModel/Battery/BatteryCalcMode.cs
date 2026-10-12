namespace EssSimulator.EssDeviceSimModel
{
    /// <summary>
    /// 电池物理计算粒度。默认逐芯；关闭后每个簇只积分一节代表电芯，再按串并联倍数汇总簇电压/SOC/温度。
    /// 由单体信息界面切换，仿真线程只读该标志。
    /// </summary>
    public static class BatteryCalcMode
    {
        private static int _cellPhysicsEnabled = 1;

        /// <summary>true：逐芯 SOC/电压/温度；false：簇等效运算。</summary>
        public static bool CellPhysicsEnabled
        {
            get => Volatile.Read(ref _cellPhysicsEnabled) != 0;
            set => Volatile.Write(ref _cellPhysicsEnabled, value ? 1 : 0);
        }
    }
}
