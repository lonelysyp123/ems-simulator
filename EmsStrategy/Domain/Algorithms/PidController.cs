using EssSimulator.EmsStrategy.Application;

namespace EssSimulator.EmsStrategy.Domain.Algorithms;

/// <summary>
/// 电站 PI。死区内返回上次输出且积分不动。
/// 增量式：<c>ΔI = Ki·Ts·e − (u − real)·Kb</c>（兼容模式 Kb 不乘周期），
/// <c>u = u_prev + Kp·e + ΔI</c>，饱和后 <c>I = u</c>。
/// <c>real</c> 是储能区实发，不是并网点。
/// Dt 模式积分步长用仿真 dt，抗饱和用 sat−unsat，且 Kb 乘周期。
/// force=true 时立即计算（辅助服务首拍）；兼容模式 Ts 仍用 Period。
/// </summary>
public sealed class PidController
{
    public double Kp { get; private set; }
    public double Ki { get; private set; }
    public double Kb { get; private set; }
    public TimeSpan Period { get; private set; }
    public double Deadband { get; private set; }
    public double OutMin { get; private set; }
    public double OutMax { get; private set; }
    public PidDiscretization Discretization { get; private set; }
    public double Integral { get; private set; }
    public double Output { get; private set; }

    private TimeSpan _elapsed;

    public void Configure(PidConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        Kp = Math.Max(0, cfg.Kp);
        Ki = Math.Max(0, cfg.Ki);
        Kb = Math.Max(0, cfg.Kb);
        Period = cfg.Period <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(3000) : cfg.Period;
        Deadband = Math.Max(0, cfg.DeadbandKw);
        OutMin = cfg.OutMinKw;
        OutMax = cfg.OutMaxKw < cfg.OutMinKw ? cfg.OutMinKw : cfg.OutMaxKw;
        Discretization = cfg.Discretization;
        Output = Math.Clamp(Output, OutMin, OutMax);
        Integral = Math.Clamp(Integral, OutMin, OutMax);
    }

    public void Reset()
    {
        Integral = 0;
        Output = 0;
        _elapsed = TimeSpan.Zero;
    }

    /// <summary>仅有误差时：等价于 measure=0、real=上次输出。</summary>
    public double Step(double error, TimeSpan dt, bool force = false)
        => Compute(error, measure: 0, realValue: Output, dt, force);

    /// <summary>设定、并网点测点、储能实发。</summary>
    public double Compute(double setpoint, double measure, double realValue, TimeSpan dt, bool force = false)
    {
        if (dt < TimeSpan.Zero)
            dt = TimeSpan.Zero;

        _elapsed += dt;
        bool due = force
            || Discretization == PidDiscretization.Dt
            || _elapsed >= Period;
        if (!due)
            return Output;

        _elapsed = TimeSpan.Zero;

        if (measure >= setpoint - Deadband && measure <= setpoint + Deadband)
            return Output;

        double error = setpoint - measure;

        if (Discretization == PidDiscretization.CompatiblePeriod)
            return ComputeCompatible(error, realValue);

        return ComputeDt(error, dt);
    }

    /// <summary>ppc_strategy <c>p_compute</c>：恒压用。死区保持上次输出；u = measure + Kp·(set−measure)。</summary>
    public double PCompute(double setpoint, double measure)
    {
        if (measure >= setpoint - Deadband && measure <= setpoint + Deadband)
            return Output;

        Output = measure + Kp * (setpoint - measure);
        if (Output > OutMax)
            Output = OutMax;
        else if (Output < OutMin)
            Output = OutMin;
        return Output;
    }

    /// <summary>增量式：u = u_prev + Kp·e + ΔI。</summary>
    private double ComputeCompatible(double error, double realValue)
    {
        double stepSec = Period.TotalSeconds;
        if (stepSec < 0)
            stepSec = 0;

        double proportional = Kp * error;
        double deltaI = Ki * stepSec * error - (Output - realValue) * Kb;
        double unsat = Output + proportional + deltaI;
        if (unsat > OutMax)
        {
            Output = OutMax;
            Integral = Output;
        }
        else if (unsat < OutMin)
        {
            Output = OutMin;
            Integral = Output;
        }
        else
        {
            Integral += deltaI;
            Output = unsat;
        }

        return Output;
    }

    private double ComputeDt(double error, TimeSpan dt)
    {
        double stepSec = Math.Max(dt.TotalSeconds, 0);
        if (stepSec <= 0)
            return Output;

        double p = Kp * error;
        double deltaI = Ki * error * stepSec;
        double unsat = Output + p + deltaI;
        double sat = Math.Clamp(unsat, OutMin, OutMax);
        Integral += deltaI + Kb * (sat - unsat) * stepSec;
        Integral = Math.Clamp(Integral, OutMin, OutMax);
        Output = sat;
        return Output;
    }
}
