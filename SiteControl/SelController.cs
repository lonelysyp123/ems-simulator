namespace EssSimulator.SiteControl;

internal readonly record struct SelReferenceCommand(double? VoltageV, double? FrequencyHz);

internal enum SelWriteError : byte
{
    None = 0,
    IllegalAddress = 2,
    IllegalValue = 3
}

internal sealed class SelController
{
    internal const int RegisterCount = 7;
    private readonly ushort[] _registers = { 0, 0, 0, 5000, 0, 0, 0 };
    private bool _hasVoltageCommand;
    private bool _hasFrequencyCommand;
    private TimeSpan _rampStartedAt;
    private double _rampDurationSeconds;
    private double _rampTargetPercent;
    private long _lastSentSecond;

    public double CurrentVoltagePercent { get; private set; }
    public double StartVoltagePercent { get; private set; }
    public bool IsRamping { get; private set; }

    public ushort[] ReadRegisters(ushort startAddress, ushort count)
    {
        if (count == 0 || startAddress + count > RegisterCount)
            throw new ArgumentOutOfRangeException(nameof(startAddress));
        return _registers.AsSpan(startAddress, count).ToArray();
    }

    public SelWriteError TryWriteRegisters(
        ushort startAddress, ReadOnlySpan<ushort> values, TimeSpan now,
        out SelReferenceCommand? command)
    {
        command = null;
        if (values.Length == 0 || startAddress + values.Length > RegisterCount)
            return SelWriteError.IllegalAddress;

        for (int i = 0; i < values.Length; i++)
        {
            bool valid = (startAddress + i) switch
            {
                0 or 4 => values[i] <= 1,
                2 => values[i] <= 1000,
                3 => values[i] is >= 4500 and <= 6500,
                5 => values[i] <= 10000,
                _ => true
            };
            if (!valid)
                return SelWriteError.IllegalValue;
        }

        ushort previousVoltage = _registers[2];
        ushort previousFrequency = _registers[3];
        values.CopyTo(_registers.AsSpan(startAddress));
        _registers[0] = 0;
        int endAddress = startAddress + values.Length;
        bool includesMode = startAddress <= 4 && endAddress > 4;
        bool includesDuration = startAddress <= 5 && endAddress > 5;

        if ((includesMode && _registers[4] == 0) || includesDuration)
        {
            IsRamping = false;
            StartVoltagePercent = CurrentVoltagePercent;
        }

        double? frequency = null;
        if (startAddress <= 3 && endAddress > 3
            && (!_hasFrequencyCommand || previousFrequency != _registers[3]))
        {
            _hasFrequencyCommand = true;
            frequency = _registers[3] / 100.0;
        }

        double? voltage = null;
        if (startAddress <= 2 && endAddress > 2
            && (!_hasVoltageCommand || previousVoltage != _registers[2]))
        {
            _hasVoltageCommand = true;
            StartVoltagePercent = CurrentVoltagePercent;
            _rampTargetPercent = _registers[2] / 10.0;
            _rampDurationSeconds = _registers[5] / 100.0;
            IsRamping = _registers[4] == 1 && _rampDurationSeconds > 0
                && _rampTargetPercent != CurrentVoltagePercent;
            if (IsRamping)
            {
                _rampStartedAt = now;
                _lastSentSecond = 0;
            }
            else
            {
                CurrentVoltagePercent = _rampTargetPercent;
                voltage = ToVoltage(CurrentVoltagePercent);
            }
        }

        if (voltage.HasValue || frequency.HasValue)
            command = new SelReferenceCommand(voltage, frequency);
        return SelWriteError.None;
    }

    public SelReferenceCommand? Tick(TimeSpan now)
    {
        if (!IsRamping)
            return null;
        long second = (long)Math.Floor((now - _rampStartedAt).TotalSeconds);
        if (second <= _lastSentSecond)
            return null;

        _lastSentSecond = second;
        double progress = Math.Min(second / _rampDurationSeconds, 1.0);
        CurrentVoltagePercent = StartVoltagePercent
            + (_rampTargetPercent - StartVoltagePercent) * progress;
        if (progress >= 1.0)
            IsRamping = false;
        return new SelReferenceCommand(ToVoltage(CurrentVoltagePercent), null);
    }

    public void StopRamp()
    {
        IsRamping = false;
        StartVoltagePercent = CurrentVoltagePercent;
    }

    /// 下发 PCS 失败时回滚，保证寄存器影子与「已下发」百分比不被未生效的目标污染。
    public Snapshot Capture() => new(
        (ushort[])_registers.Clone(), _hasVoltageCommand, _hasFrequencyCommand, _rampStartedAt,
        _rampDurationSeconds, _rampTargetPercent, _lastSentSecond,
        CurrentVoltagePercent, StartVoltagePercent, IsRamping);

    public void Restore(in Snapshot snapshot)
    {
        snapshot.Registers.CopyTo(_registers, 0);
        _hasVoltageCommand = snapshot.HasVoltageCommand;
        _hasFrequencyCommand = snapshot.HasFrequencyCommand;
        _rampStartedAt = snapshot.RampStartedAt;
        _rampDurationSeconds = snapshot.RampDurationSeconds;
        _rampTargetPercent = snapshot.RampTargetPercent;
        _lastSentSecond = snapshot.LastSentSecond;
        CurrentVoltagePercent = snapshot.CurrentVoltagePercent;
        StartVoltagePercent = snapshot.StartVoltagePercent;
        IsRamping = snapshot.IsRamping;
    }

    public readonly record struct Snapshot(
        ushort[] Registers, bool HasVoltageCommand, bool HasFrequencyCommand, TimeSpan RampStartedAt,
        double RampDurationSeconds, double RampTargetPercent, long LastSentSecond,
        double CurrentVoltagePercent, double StartVoltagePercent, bool IsRamping);

    private static double ToVoltage(double percent) => 690.0 * percent / 100.0;
}
