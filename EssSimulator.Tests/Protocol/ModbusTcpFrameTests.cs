using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.Protocol;

public class ModbusTcpFrameTests
{
    [Fact]
    public void TryGetBodyLength_ReadsBigEndianLength()
    {
        byte[] header = { 0x12, 0x34, 0x00, 0x00, 0x00, 0x06 };

        Assert.True(ModbusTcpFrame.TryGetBodyLength(header, out ushort length));
        Assert.Equal((ushort)6, length);
        Assert.Equal((ushort)0x1234, ModbusTcpFrame.ReadTransactionId(header));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(261)]
    public void TryGetBodyLength_RejectsOutOfRange(int length)
    {
        byte[] header = { 0x00, 0x01, 0x00, 0x00, (byte)(length >> 8), (byte)length };

        Assert.False(ModbusTcpFrame.TryGetBodyLength(header, out _));
    }

    [Fact]
    public void PrefixHeader_KeepsTransactionId_AndWritesBodyLength()
    {
        byte[] body = { 0x01, 0x03, 0x02, 0x00, 0x2A };

        byte[] frame = ModbusTcpFrame.PrefixHeader(0x1234, body);

        Assert.Equal(0x12, frame[0]);
        Assert.Equal(0x34, frame[1]);
        Assert.Equal(0x00, frame[2]);
        Assert.Equal(0x00, frame[3]);
        Assert.Equal(0x00, frame[4]);
        Assert.Equal((byte)body.Length, frame[5]);
        Assert.Equal(body, frame.AsSpan(ModbusTcpFrame.HeaderLength).ToArray());
    }
}
