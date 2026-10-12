namespace EssSimulator.EmsStrategy.Domain.Algorithms;

/// <summary>
/// 斜率限制，复刻 ppc_strategy <c>slope_calculate</c>。
/// 速率字段 JSON 名仍是 RiseKwPerSec，语义是 C 的 kW/min（period_ms/60000）。
/// 每拍基准是测点，不是上次指令。关闭时输出等于设定。
/// </summary>
public sealed class SlopeLimiter
{
    public bool Enabled { get; private set; }
    public double RiseKwPerMin { get; private set; }
    public double FallKwPerMin { get; private set; }
    public double Current { get; private set; }

    public void Configure(bool enabled, double riseKwPerMin, double fallKwPerMin)
    {
        Enabled = enabled;
        RiseKwPerMin = Math.Max(0, riseKwPerMin);
        FallKwPerMin = Math.Max(0, fallKwPerMin);
    }

    public void Reset(double value = 0) => Current = value;

    /// <summary>兼容旧调用：以内部 Current 为测点。</summary>
    public double Step(double target, TimeSpan dt)
        => Step(target, Current, dt);

    public double Step(double target, double measure, TimeSpan dt)
    {
        if (!Enabled || dt <= TimeSpan.Zero)
        {
            Current = target;
            return Current;
        }

        Current = Calculate(target, RiseKwPerMin, FallKwPerMin, dt, measure);
        return Current;
    }

    /// <summary>ppc_strategy algorithm.c slope_calculate。period 按 dt 换成分钟。</summary>
    internal static double Calculate(
        double setpoint,
        double upSpeedKwPerMin,
        double downSpeedKwPerMin,
        TimeSpan period,
        double measure)
    {
        double minutes = period.TotalMilliseconds / (60.0 * 1000.0);
        int sig = measure < 0 ? -1 : 1;
        double output;

        if (measure * setpoint > 0)
        {
            if (Math.Abs(setpoint) > Math.Abs(measure))
            {
                output = measure + upSpeedKwPerMin * minutes * sig;
                if (setpoint > 0 && output > setpoint) output = setpoint;
                if (setpoint < 0 && output < setpoint) output = setpoint;
            }
            else
            {
                output = measure - downSpeedKwPerMin * minutes * sig;
                if (setpoint > 0 && output < setpoint) output = setpoint;
                if (setpoint < 0 && output > setpoint) output = setpoint;
            }
        }
        else if (measure * setpoint < 0)
        {
            output = measure - downSpeedKwPerMin * minutes * sig;
            if (setpoint > 0 && output > setpoint) output = setpoint;
            if (setpoint < 0 && output < setpoint) output = setpoint;
        }
        else if (Math.Abs(setpoint) >= Math.Abs(measure))
        {
            sig = setpoint < 0 ? -1 : 1;
            output = measure + upSpeedKwPerMin * minutes * sig;
            if (setpoint >= 0 && output > setpoint) output = setpoint;
            if (setpoint < 0 && output < setpoint) output = setpoint;
        }
        else
        {
            output = measure - downSpeedKwPerMin * minutes * sig;
            if (output * measure < 0)
                output = setpoint;
        }

        return output;
    }
}
