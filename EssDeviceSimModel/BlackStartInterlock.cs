namespace EssSimulator.EssDeviceSimModel
{
    /// <summary>
    /// 黑启动与断路器联锁（设备层纯逻辑）：
    /// 主断与所属单元高压均合闸时开启黑启动 → 向电网侧建压短路风险。
    /// </summary>
    public static class BlackStartInterlock
    {
        public static bool IsStationShortCircuitRisk(
            bool mainBreakerClosed,
            bool unitBreakerClosed,
            bool blackStartActiveOrRequested) =>
            blackStartActiveOrRequested && mainBreakerClosed && unitBreakerClosed;

        public static bool IsStationShortCircuitRisk(EnergyStorageSystem ess, int pcsSimIndex, bool blackStartActiveOrRequested)
        {
            if (!blackStartActiveOrRequested)
                return false;

            if (!ess.IsMainBreakerClosed)
                return false;

            int unit = ess.UnitIndexOfPcs(pcsSimIndex);
            return ess.IsUnitBreakerClosed(unit);
        }
    }
}
