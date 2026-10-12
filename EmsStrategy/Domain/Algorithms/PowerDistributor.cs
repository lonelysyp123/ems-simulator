using EssSimulator.EmsStrategy.Application;

namespace EssSimulator.EmsStrategy.Domain.Algorithms;

/// <summary>
/// 站级有功/无功分配到 PCS 支路。
/// 分配关：有功/无功均分到可运行支路（有功超限再二次分配）。
/// 分配开：无功按剩余 Q 容量加权；SOC 均衡开则有功按 SOC 加权，否则有功均分。
/// </summary>
public static class PowerDistributor
{
    public static IReadOnlyList<BranchCommand> Distribute(
        double plantActiveKw,
        IReadOnlyList<PcsBranchState> branches,
        DistributionConfig cfg) =>
        Distribute(plantActiveKw, 0, branches, cfg, applyReactive: false);

    public static IReadOnlyList<BranchCommand> Distribute(
        double plantActiveKw,
        double plantReactiveKvar,
        IReadOnlyList<PcsBranchState> branches,
        DistributionConfig cfg) =>
        Distribute(plantActiveKw, plantReactiveKvar, branches, cfg, applyReactive: true);

    private static IReadOnlyList<BranchCommand> Distribute(
        double plantActiveKw,
        double plantReactiveKvar,
        IReadOnlyList<PcsBranchState> branches,
        DistributionConfig cfg,
        bool applyReactive)
    {
        ArgumentNullException.ThrowIfNull(branches);
        cfg ??= new DistributionConfig();

        if (branches.Count == 0)
            return Array.Empty<BranchCommand>();

        var eligible = new List<PcsBranchState>(branches.Count);
        foreach (var b in branches)
        {
            if (IsEligible(b, plantActiveKw, cfg))
                eligible.Add(b);
        }

        var commands = new BranchCommand[branches.Count];
        for (int i = 0; i < branches.Count; i++)
        {
            var b = branches[i];
            commands[i] = new BranchCommand
            {
                Index = b.Index,
                UnitIndex0 = b.UnitIndex0,
                PcsIndexInUnit = b.PcsIndexInUnit,
                ActivePowerKw = 0,
                ReactivePowerKvar = applyReactive ? 0 : b.MeasuredReactiveKvar
            };
        }

        if (eligible.Count > 0)
        {
            var alloc = new Dictionary<int, double>(eligible.Count);
            foreach (var b in eligible)
                alloc[b.Index] = 0;

            if (cfg.Enabled && cfg.SocBalance)
                AllocateWeighted(plantActiveKw, eligible, cfg, alloc);
            else
                AllocateEqualThenRedistribute(plantActiveKw, eligible, alloc);

            for (int i = 0; i < commands.Length; i++)
            {
                var b = branches[i];
                if (!alloc.TryGetValue(b.Index, out double p))
                    continue;
                commands[i] = new BranchCommand
                {
                    Index = b.Index,
                    UnitIndex0 = b.UnitIndex0,
                    PcsIndexInUnit = b.PcsIndexInUnit,
                    ActivePowerKw = ClampBranch(b, p),
                    ReactivePowerKvar = commands[i].ReactivePowerKvar
                };
            }
        }

        if (applyReactive)
            MergeReactive(commands, branches, plantReactiveKvar, cfg);

        return commands;
    }

    private static void MergeReactive(
        BranchCommand[] commands,
        IReadOnlyList<PcsBranchState> branches,
        double plantReactiveKvar,
        DistributionConfig cfg)
    {
        var eligible = new List<int>();
        for (int i = 0; i < branches.Count; i++)
        {
            var b = branches[i];
            if (b.CommOk && !b.Fault && b.Running)
                eligible.Add(i);
        }

        if (eligible.Count == 0)
            return;

        if (!cfg.Enabled)
        {
            double each = plantReactiveKvar / eligible.Count;
            for (int n = 0; n < eligible.Count; n++)
            {
                int i = eligible[n];
                var p = commands[i];
                commands[i] = new BranchCommand
                {
                    Index = p.Index,
                    UnitIndex0 = p.UnitIndex0,
                    PcsIndexInUnit = p.PcsIndexInUnit,
                    ActivePowerKw = p.ActivePowerKw,
                    ReactivePowerKvar = each
                };
            }
            return;
        }

        double totalOmega = 0;
        var omega = new double[eligible.Count];
        for (int n = 0; n < eligible.Count; n++)
        {
            int i = eligible[n];
            var b = branches[i];
            double s = Math.Max(b.RatedKw, 1);
            omega[n] = RemainingAxis(s, b.MeasuredActiveKw);
            totalOmega += omega[n];
        }

        double absTarget = Math.Abs(plantReactiveKvar);
        bool inductive = plantReactiveKvar > 0;
        if (totalOmega <= 1e-9)
            return;

        for (int n = 0; n < eligible.Count; n++)
        {
            int i = eligible[n];
            var p = commands[i];
            double val = absTarget >= totalOmega
                ? omega[n]
                : omega[n] * (absTarget / totalOmega);
            if (!inductive)
                val = -val;
            commands[i] = new BranchCommand
            {
                Index = p.Index,
                UnitIndex0 = p.UnitIndex0,
                PcsIndexInUnit = p.PcsIndexInUnit,
                ActivePowerKw = p.ActivePowerKw,
                ReactivePowerKvar = val
            };
        }
    }

    public static bool IsEligible(PcsBranchState b, double plantActiveKw, DistributionConfig cfg)
    {
        if (!b.CommOk || b.Fault || !b.Running)
            return false;
        if (plantActiveKw > 0 && (b.DischargeProhibited || b.Soc <= cfg.SocMin))
            return false;
        if (plantActiveKw < 0 && (b.ChargeProhibited || b.Soc >= cfg.SocMax))
            return false;
        if (plantActiveKw > 0)
            return MaxDischarge(b) > 1e-9;
        if (plantActiveKw < 0)
            return MaxCharge(b) > 1e-9;
        return true;
    }

    private static void AllocateWeighted(
        double plantActiveKw,
        List<PcsBranchState> eligible,
        DistributionConfig cfg,
        Dictionary<int, double> alloc)
    {
        double sumW = 0;
        var weights = new double[eligible.Count];
        for (int i = 0; i < eligible.Count; i++)
        {
            var b = eligible[i];
            double w = plantActiveKw >= 0
                ? Math.Max(0, b.Soc - cfg.SocMin)
                : Math.Max(0, cfg.SocMax - b.Soc);
            weights[i] = w;
            sumW += w;
        }

        if (sumW <= 1e-12)
        {
            AllocateEqualThenRedistribute(plantActiveKw, eligible, alloc);
            return;
        }

        for (int i = 0; i < eligible.Count; i++)
            alloc[eligible[i].Index] = plantActiveKw * weights[i] / sumW;

        RedistributeOverflow(eligible, alloc);
    }

    private static void AllocateEqualThenRedistribute(
        double plantActiveKw,
        List<PcsBranchState> eligible,
        Dictionary<int, double> alloc)
    {
        double each = plantActiveKw / eligible.Count;
        foreach (var b in eligible)
            alloc[b.Index] = each;
        RedistributeOverflow(eligible, alloc);
    }

    private static void RedistributeOverflow(List<PcsBranchState> eligible, Dictionary<int, double> alloc)
    {
        for (int pass = 0; pass < eligible.Count; pass++)
        {
            double surplus = 0;
            var receivers = new List<PcsBranchState>();
            foreach (var b in eligible)
            {
                double p = alloc[b.Index];
                double clamped = ClampBranch(b, p);
                double extra = p - clamped;
                alloc[b.Index] = clamped;
                if (Math.Abs(extra) > 1e-9)
                    surplus += extra;
                else if (Headroom(b, clamped) > 1e-9)
                    receivers.Add(b);
            }

            if (Math.Abs(surplus) <= 1e-9 || receivers.Count == 0)
                return;

            double share = surplus / receivers.Count;
            foreach (var b in receivers)
                alloc[b.Index] += share;
        }
    }

    private static double ClampBranch(PcsBranchState b, double p)
    {
        double maxDis = MaxDischarge(b);
        double maxChg = MaxCharge(b);
        if (b.DischargeProhibited)
            maxDis = 0;
        if (b.ChargeProhibited)
            maxChg = 0;
        double pFromS = RemainingAxis(Math.Max(b.RatedKw, 1), b.MeasuredReactiveKvar);
        maxDis = Math.Min(maxDis, pFromS);
        maxChg = Math.Min(maxChg, pFromS);
        return Math.Clamp(p, -maxChg, maxDis);
    }

    private static double RemainingAxis(double rated, double other)
    {
        double s2 = rated * rated;
        double o2 = other * other;
        if (o2 >= s2)
            return 0;
        return Math.Sqrt(s2 - o2);
    }

    private static double MaxDischarge(PcsBranchState b)
    {
        double cap = b.MaxDischargeKw > 0 ? b.MaxDischargeKw : b.RatedKw;
        return Math.Max(0, cap);
    }

    private static double MaxCharge(PcsBranchState b)
    {
        double cap = b.MaxChargeKw > 0 ? b.MaxChargeKw : b.RatedKw;
        return Math.Max(0, cap);
    }

    private static double Headroom(PcsBranchState b, double current)
    {
        if (current >= 0)
            return MaxDischarge(b) - current;
        return MaxCharge(b) + current;
    }
}
