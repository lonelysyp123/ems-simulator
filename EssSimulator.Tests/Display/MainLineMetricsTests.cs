using EssSimulator.Display;

namespace EssSimulator.Tests.Display;

/// <summary>
/// MainLineMetrics 的单元索引查找。
/// UnitBranchSnapshot 是 readonly record struct，FirstOrDefault 找不到时返回 default
/// （UnitIndex=0），曾经因此把"不存在的 unit 5"读成 0 号单元的值。
/// </summary>
public class MainLineMetricsTests
{
    private static MainLineSnapshot SnapshotWith(params int[] unitIndexes)
    {
        var units = unitIndexes
            .Select(i => new UnitBranchSnapshot(
                UnitIndex: i,
                UnitBreakerClosed: true,
                UnitBreakerTripped: false,
                UnitTransformerPrimary: default,
                UnitTransformerSecondary: default,
                Bus690: null,
                PcsChannels: Array.Empty<PcsChannelSnapshot?>()))
            .ToList();

        return new MainLineSnapshot { Units = units };
    }

    [Fact]
    public void UnitBreaker_ReadsMatchingUnit()
    {
        var snap = SnapshotWith(0, 1, 2);

        Assert.True(MainLineMetrics.TryRead("unit.1.breaker.closed", snap, out var value));
        Assert.Equal(1, value);
    }

    [Fact]
    public void UnknownUnit_ReturnsFalse_InsteadOfUnitsZeroState()
    {
        var snap = SnapshotWith(0, 1, 2);

        // 5 号单元不存在：不能静默回落到 0 号单元（那会让"单元 5 断路器"永远显示合闸）
        Assert.False(MainLineMetrics.TryRead("unit.5.breaker.closed", snap, out var value));
        Assert.Equal(0, value);
    }
}
