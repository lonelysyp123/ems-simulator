namespace EssSimulator.EmsStrategy.Domain;

/// <summary>有功基础模式。CloseLoopCurve 仅兼容旧 JSON，加载后强制为 CloseLoopFixed。</summary>
public enum ActiveMode
{
    OpenLoopFixed = 0,
    CloseLoopFixed = 1,
    CloseLoopCurve = 2
}

public enum ReactiveMode
{
    OpenLoopFixed = 0,
    CloseLoopFixed = 1,
    CloseLoopCurve = 2,
    PowerFactor = 3,
    VoltageFixed = 4
}

public enum LocalRemote
{
    Local = 0,
    Remote = 1
}

public enum ActionState
{
    Reset = 0,
    Action = 1
}

/// <summary>PI 离散化：兼容原周期采样，或按仿真 dt 积分。</summary>
public enum PidDiscretization
{
    CompatiblePeriod = 0,
    Dt = 1
}
