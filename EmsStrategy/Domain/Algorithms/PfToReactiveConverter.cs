namespace EssSimulator.EmsStrategy.Domain.Algorithms;

/// <summary>
/// 功率因数反算无功：Q ≈ |P| × √(1 − PF²) / PF。
/// sign：+1 与常用容性为正一致，可由配置取反。
/// </summary>
public static class PfToReactiveConverter
{
    public static double FromPf(double activePowerKw, double powerFactor, double sign = 1)
    {
        double pf = Math.Clamp(Math.Abs(powerFactor), 1e-3, 1);
        double q = Math.Abs(activePowerKw) * Math.Sqrt(Math.Max(0, 1 - pf * pf)) / pf;
        return (sign >= 0 ? 1 : -1) * q;
    }
}
