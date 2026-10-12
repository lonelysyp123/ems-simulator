namespace EssSimulator.EmsStrategy.Application;

/// <summary>
/// 把补丁覆盖到已 Clone 的配置上。嵌套对象整段替换；未出现的子树保持原值。
/// 标量开关在嵌套对象之后写入，便于旧客户端只发 <c>*Enabled</c>。
/// </summary>
public static class EmsStrategyConfigPatcher
{
    public static void Apply(EmsStrategyConfig cfg, EmsStrategyPatchRequest? req)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        if (req == null)
            return;

        if (req.Enabled is { } en)
            cfg.Enabled = en;
        if (req.SystemSwitch is { } sw)
            cfg.SystemSwitch = sw;
        if (req.ActiveEnable is { } ae)
            cfg.ActiveEnable = ae;
        if (req.ReactiveEnable is { } re)
            cfg.ReactiveEnable = re;
        if (req.ActiveMode is { } mode)
            cfg.ActiveMode = mode;
        if (req.ReactiveMode is { } rm)
            cfg.ReactiveMode = rm;
        if (req.LocalRemote is { } lr)
            cfg.LocalRemote = lr;
        if (req.LocalActiveSetKw is { } p)
            cfg.LocalActiveSetKw = p;
        if (req.RemoteActiveSetKw is { } rp)
            cfg.RemoteActiveSetKw = rp;
        if (req.LocalReactiveSetKvar is { } q)
            cfg.LocalReactiveSetKvar = q;
        if (req.RemoteReactiveSetKvar is { } rq)
            cfg.RemoteReactiveSetKvar = rq;
        if (req.PowerFactorSet is { } pf)
            cfg.PowerFactorSet = pf;
        if (req.PfSign is { } sign)
            cfg.PfSign = sign;
        if (req.VoltageSetV is { } u)
            cfg.VoltageSetV = u;
        if (req.VoltageKp is { } kp)
            cfg.VoltageKp = kp;
        if (req.VoltageFixedK is { } vk)
            cfg.VoltageFixedK = vk;
        if (req.ApparentRatedKva is { } s)
            cfg.ApparentRatedKva = s;
        if (req.PlantRatedKw is { } rated)
            cfg.PlantRatedKw = rated;
        if (req.ApparentLimitEnabled is { } ale)
            cfg.ApparentLimitEnabled = ale;
        if (req.DampEnabled is { } de)
            cfg.DampEnabled = de;

        if (req.Slope is { } slope)
            cfg.Slope = slope.Clone();
        if (req.ReactiveSlope is { } rs)
            cfg.ReactiveSlope = rs.Clone();
        if (req.ActivePid is { } pid)
            cfg.ActivePid = pid.Clone();
        if (req.ReactivePid is { } rpid)
            cfg.ReactivePid = rpid.Clone();
        if (req.PrimaryFrequency is { } pfr)
            cfg.PrimaryFrequency = pfr.Clone();
        if (req.Inertia is { } inertia)
            cfg.Inertia = inertia.Clone();
        if (req.VoltageDroop is { } droop)
            cfg.VoltageDroop = droop.Clone();
        if (req.Distribution is { } dist)
            cfg.Distribution = dist.Clone();

        if (req.SlopeEnabled is { } se)
            cfg.Slope.Enabled = se;
        if (req.ReactiveSlopeEnabled is { } rse)
            cfg.ReactiveSlope.Enabled = rse;
        if (req.ActivePidEnabled is { } ape)
            cfg.ActivePid.Enabled = ape;
        if (req.ReactivePidEnabled is { } rpe)
            cfg.ReactivePid.Enabled = rpe;
        if (req.DistributionEnabled is { } dste)
            cfg.Distribution.Enabled = dste;
        if (req.PrimaryFrequencyEnabled is { } pfe)
            cfg.PrimaryFrequency.Enabled = pfe;
        if (req.InertiaEnabled is { } ie)
            cfg.Inertia.Enabled = ie;
        if (req.VoltageDroopEnabled is { } vd)
            cfg.VoltageDroop.Enabled = vd;
        if (req.SocBalance is { } soc)
            cfg.Distribution.SocBalance = soc;

        EmsStrategyConfig.NormalizeModes(cfg);
    }
}
