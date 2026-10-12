namespace EssSimulator.EmsStrategy.Domain.Algorithms;

/// <summary>
/// 视在限幅与阻尼，复刻 ppc_strategy ApparentLimit_* / DampCtrl_*。
/// 闭环用另一轴实测限制本轴；超圆且仍在远离实测时按 0.5 阻尼。
/// 开环用储能增量预测并网点，再回推储能指令。
/// </summary>
public static class ApparentPowerLimiter
{
    public const double DampFactor = 0.5;

    public static double LimitActive(double activeKw, double otherAxisKvar, double apparentRatedKva)
        => LimitGrid(activeKw, otherAxisKvar, apparentRatedKva);

    public static double LimitReactive(double reactiveKvar, double otherAxisKw, double apparentRatedKva)
        => LimitGrid(reactiveKvar, otherAxisKw, apparentRatedKva);

    /// <summary>ApparentLimit_Grid_Active / Grid_Reactive。</summary>
    public static double LimitGrid(double target, double otherMeas, double apparentRated)
    {
        if (apparentRated <= 0 || apparentRated < Math.Abs(otherMeas))
            return 0;
        double limit = Math.Sqrt(apparentRated * apparentRated - otherMeas * otherMeas);
        double sign = target < 0 ? -1 : 1;
        return limit < Math.Abs(target) ? sign * limit : target;
    }

    /// <summary>ApparentLimit_DampCtrl_Active / DampCtrl_Reactive。</summary>
    public static double Damp(
        double measured,
        double limited,
        double slopeTarget,
        double otherTarget,
        double apparentRated)
    {
        if (Math.Abs(limited) < Math.Abs(measured))
            return limited;
        if (slopeTarget * slopeTarget + otherTarget * otherTarget <= apparentRated * apparentRated)
            return limited;
        double sign = limited < 0 ? -1 : 1;
        return sign * (Math.Abs(measured) + (Math.Abs(limited) - Math.Abs(measured)) * DampFactor);
    }

    /// <summary>ApparentLimit_OpenCtrl_Active / OpenCtrl_Reactive。</summary>
    public static double LimitOpen(
        double gridThis,
        double gridOther,
        double targetStorage,
        double storageMeas,
        double apparentRated)
    {
        if (targetStorage <= 0.01 && targetStorage >= -0.01)
            return 0;
        if (apparentRated <= 0 || apparentRated < Math.Abs(gridOther))
            return 0;

        double limitG = Math.Sqrt(apparentRated * apparentRated - gridOther * gridOther);
        double predictG = gridThis + (targetStorage - storageMeas);
        if (Math.Abs(predictG) < limitG)
            return targetStorage;

        limitG = predictG < 0 ? -limitG : limitG;
        double limitS = targetStorage + limitG - predictG;
        return limitS * targetStorage < 0 ? 0 : limitS;
    }

    /// <summary>ApparentLimit_DampCtrl_Active_Open / DampCtrl_Reactive_Open。</summary>
    public static double DampOpen(
        double storageMeas,
        double limitedStorage,
        double gridThis,
        double otherTarget,
        double apparentRated)
    {
        double predictG = limitedStorage - storageMeas + gridThis;
        if (Math.Abs(predictG) < Math.Abs(gridThis))
            return limitedStorage;
        if (predictG * predictG + otherTarget * otherTarget <= apparentRated * apparentRated)
            return limitedStorage;
        double sign = limitedStorage < 0 ? -1 : 1;
        return sign * (Math.Abs(storageMeas) + (Math.Abs(limitedStorage) - Math.Abs(storageMeas)) * DampFactor);
    }
}
