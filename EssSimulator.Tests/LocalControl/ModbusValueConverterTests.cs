using EssSimulator.LocalControl;
using Xunit;

namespace EssSimulator.Tests.LocalControl;

public class ModbusValueConverterTests
{
    [Theory]
    [InlineData(0xAA, true)]
    [InlineData(0xEE, false)]
    public void TryNormalizeHvBreakerCommand_accepts_valid_codes(int raw, bool expectClosed)
    {
        Assert.True(ModbusValueConverter.TryNormalizeHvBreakerCommand(raw, out var normalized, out var closed));
        Assert.Equal(raw, (int)normalized);
        Assert.Equal(expectClosed, closed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(0xAB)]
    public void TryNormalizeHvBreakerCommand_rejects_invalid_codes(int raw)
    {
        Assert.False(ModbusValueConverter.TryNormalizeHvBreakerCommand(raw, out _, out _));
    }
}
