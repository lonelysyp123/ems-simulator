namespace EssSimulator.EmsStrategy.Domain.Algorithms;

/// <summary>
/// 辅助服务 ACTION/RESET 滞环。首次进入 ACTION 立即计算，之后按 control_cycle 刷新。
/// 回到死区后需持续 reset_time 才回到 RESET。
/// </summary>
public sealed class AuxActionMachine
{
    public ActionState State { get; private set; } = ActionState.Reset;
    public double Output { get; private set; }
    public TimeSpan CycleElapsed { get; private set; }
    public TimeSpan ResetElapsed { get; private set; }

    public void Reset()
    {
        State = ActionState.Reset;
        Output = 0;
        CycleElapsed = TimeSpan.Zero;
        ResetElapsed = TimeSpan.Zero;
    }

    public double Step(
        bool outsideDeadband,
        TimeSpan dt,
        TimeSpan controlCycle,
        TimeSpan resetTime,
        Func<double> compute)
    {
        ArgumentNullException.ThrowIfNull(compute);
        if (dt < TimeSpan.Zero)
            dt = TimeSpan.Zero;

        if (State == ActionState.Reset)
        {
            if (!outsideDeadband)
            {
                Output = 0;
                return Output;
            }

            State = ActionState.Action;
            CycleElapsed = TimeSpan.Zero;
            ResetElapsed = TimeSpan.Zero;
            Output = compute();
            return Output;
        }

        if (outsideDeadband)
        {
            ResetElapsed = TimeSpan.Zero;
            CycleElapsed += dt;
            if (CycleElapsed >= controlCycle)
            {
                CycleElapsed = TimeSpan.Zero;
                Output = compute();
            }
            return Output;
        }

        ResetElapsed += dt;
        if (ResetElapsed >= resetTime)
        {
            Reset();
            return 0;
        }

        return Output;
    }
}
