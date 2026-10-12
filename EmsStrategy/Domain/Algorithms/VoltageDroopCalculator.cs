using EssSimulator.EmsStrategy.Application;

namespace EssSimulator.EmsStrategy.Domain.Algorithms;

/// <summary>
/// 下垂调压，与一次调频同口径：
/// ΔQ = q_start + (U_span − U) / U0 × Q_rated / droop × 100，
/// droop 为百分数（4 表示 4%）。三段：死区外单斜率；五段：内段 k1、外段 k2。
/// VoltageCurveType=0 从死区边沿起算；=1 从额定电压起算。
/// </summary>
public static class VoltageDroopCalculator
{
    public static double RatedVoltageOrDefault(VoltageDroopConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        return cfg.RatedVoltageV <= 0 ? 35000 : cfg.RatedVoltageV;
    }

    public static double DeadbandV(VoltageDroopConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        return Math.Abs(RatedVoltageOrDefault(cfg)) * Math.Max(0, cfg.Deadband1Percent) / 100.0;
    }

    public static double OuterDeadbandV(VoltageDroopConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        double inner = DeadbandV(cfg);
        double outer = Math.Abs(RatedVoltageOrDefault(cfg)) * Math.Max(0, cfg.Deadband2Percent) / 100.0;
        return Math.Max(outer, inner);
    }

    /// <summary>
    /// 按额定电压就近选择 PCC 或 35kV 站内母线，避免 220kV 测点对 35kV 额定造成恒过压。
    /// </summary>
    public static double SelectMeasuredVoltage(
        double pccLineVoltageV,
        double stationBus35LineVoltageV,
        double ratedVoltageV)
    {
        double u0 = ratedVoltageV <= 0 ? 35000 : ratedVoltageV;
        if (stationBus35LineVoltageV <= 1)
            return pccLineVoltageV;
        if (pccLineVoltageV <= 1)
            return stationBus35LineVoltageV;
        return Math.Abs(pccLineVoltageV - u0) <= Math.Abs(stationBus35LineVoltageV - u0)
            ? pccLineVoltageV
            : stationBus35LineVoltageV;
    }

    public static bool IsOutsideDeadband(double voltageV, VoltageDroopConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        double u0 = RatedVoltageOrDefault(cfg);
        double db = DeadbandV(cfg);
        if (voltageV < u0 - db)
            return cfg.UnderVoltEnable;
        if (voltageV > u0 + db)
            return cfg.OverVoltEnable;
        return false;
    }

    public static double ComputeDeltaKvar(double voltageV, double plantRatedKvar, VoltageDroopConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        if (!IsOutsideDeadband(voltageV, cfg))
            return 0;

        double u0 = RatedVoltageOrDefault(cfg);
        double qRated = plantRatedKvar > 0 ? plantRatedKvar : 1;
        double db1 = DeadbandV(cfg);
        bool under = voltageV < u0;
        bool fromRated = cfg.VoltageCurveType != 0;
        double uSpan = fromRated ? u0 : (under ? u0 - db1 : u0 + db1);
        double k = Math.Max(1e-6, cfg.K1Percent);
        double qStart = 0;

        if (cfg.SegmentCount >= 5)
        {
            double db2 = OuterDeadbandV(cfg);
            double uBreak = under ? u0 - db2 : u0 + db2;
            bool outer = under ? voltageV < u0 - db2 : voltageV > u0 + db2;
            if (outer)
            {
                qStart = SegmentDelta(uSpan, uBreak, u0, qRated, k);
                uSpan = uBreak;
                k = Math.Max(1e-6, cfg.K2Percent > 0 ? cfg.K2Percent : cfg.K1Percent);
            }
        }

        double raw = qStart + SegmentDelta(uSpan, voltageV, u0, qRated, k);
        double coef = Math.Clamp(cfg.LimitCoefficient, 0, 10);
        double maxOut = Math.Max(0, cfg.MaxOutputKvar) * coef;
        double maxAbs = Math.Max(0, cfg.MaxAbsorbKvar) * coef;
        return Math.Clamp(raw, -maxAbs, maxOut);
    }

    /// <summary>ΔQ = (U_span − U) / U0 × Q_rated / droop × 100</summary>
    private static double SegmentDelta(double uSpan, double u, double u0, double qRated, double droopPercent) =>
        (uSpan - u) / u0 * qRated / Math.Max(1e-6, droopPercent) * 100.0;
}
