using EssSimulator.EmsStrategy.Application;

namespace EssSimulator.EmsStrategy.Domain.Algorithms;

/// <summary>
/// 一次调频下垂。公式：ΔP = p_start + (f_span − f) / f0 × P_rated / droop × 100，
/// 再按最大输出/吸收与限幅系数钳位。droop 为百分数（3 表示 3%）。
/// 三段：死区外单斜率；五段：内段 droop、外段 droop2。
/// </summary>
public static class FrequencyDroopCalculator
{
    public static double DeadbandHz(PrimaryFrequencyConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        return Math.Abs(cfg.RatedFrequencyHz) * Math.Max(0, cfg.Deadband1Percent) / 100.0;
    }

    public static double OuterDeadbandHz(PrimaryFrequencyConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        double inner = DeadbandHz(cfg);
        double outer = Math.Abs(cfg.RatedFrequencyHz) * Math.Max(0, cfg.Deadband2Percent) / 100.0;
        return Math.Max(outer, inner);
    }

    public static bool IsOutsideDeadband(double frequencyHz, PrimaryFrequencyConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        double f0 = cfg.RatedFrequencyHz;
        double db = DeadbandHz(cfg);
        if (frequencyHz < f0 - db)
            return cfg.UnderFreqEnable;
        if (frequencyHz > f0 + db)
            return cfg.OverFreqEnable;
        return false;
    }

    public static double ComputeDeltaKw(double frequencyHz, double plantRatedKw, PrimaryFrequencyConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        if (!IsOutsideDeadband(frequencyHz, cfg))
            return 0;

        double f0 = cfg.RatedFrequencyHz <= 0 ? 50 : cfg.RatedFrequencyHz;
        double pRated = plantRatedKw > 0 ? plantRatedKw : 1;
        double db1 = DeadbandHz(cfg);
        bool under = frequencyHz < f0;
        double fSpan = under ? f0 - db1 : f0 + db1;
        double pStart = 0;
        double droop = InnerDroop(cfg, under);

        if (cfg.SegmentCount >= 5)
        {
            double db2 = OuterDeadbandHz(cfg);
            double fBreak = under ? f0 - db2 : f0 + db2;
            bool outer = under ? frequencyHz < f0 - db2 : frequencyHz > f0 + db2;
            if (outer)
            {
                pStart = SegmentDelta(fSpan, fBreak, f0, pRated, droop);
                fSpan = fBreak;
                droop = OuterDroop(cfg, under);
            }
        }

        double raw = pStart + SegmentDelta(fSpan, frequencyHz, f0, pRated, droop);
        double coef = Math.Clamp(cfg.LimitCoefficient, 0, 10);
        double maxOut = Math.Max(0, cfg.MaxOutputKw) * coef;
        double maxAbs = Math.Max(0, cfg.MaxAbsorbKw) * coef;
        return Math.Clamp(raw, -maxAbs, maxOut);
    }

    /// <summary>ΔP = (f_span − f) / f0 × P_rated / droop × 100</summary>
    private static double SegmentDelta(double fSpan, double f, double f0, double pRated, double droopPercent) =>
        (fSpan - f) / f0 * pRated / Math.Max(1e-6, droopPercent) * 100.0;

    private static double InnerDroop(PrimaryFrequencyConfig cfg, bool under)
    {
        double specific = under ? cfg.UnderDroopPercent : cfg.OverDroopPercent;
        return Math.Max(1e-6, specific > 0 ? specific : cfg.DroopPercent);
    }

    private static double OuterDroop(PrimaryFrequencyConfig cfg, bool under)
    {
        double specific = under ? cfg.UnderDroop2Percent : cfg.OverDroop2Percent;
        return Math.Max(1e-6, specific > 0 ? specific : cfg.Droop2Percent);
    }
}
