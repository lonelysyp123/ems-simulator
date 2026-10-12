namespace EssSimulator.Protocol.Modbus
{
    /// <summary>
    /// Modbus TCP MBAP 头：事务号、协议标识、长度。长度是后续单元标识加 PDU 的字节数。
    /// </summary>
    internal static class ModbusTcpFrame
    {
        public const int HeaderLength = 6;
        public const int MinBodyLength = 1;
        public const int MaxBodyLength = 260;

        public static ushort ReadTransactionId(ReadOnlySpan<byte> header) =>
            (ushort)((header[0] << 8) | header[1]);

        public static bool TryGetBodyLength(ReadOnlySpan<byte> header, out ushort length)
        {
            length = 0;
            if (header.Length < HeaderLength)
                return false;

            length = (ushort)((header[4] << 8) | header[5]);
            return length >= MinBodyLength && length <= MaxBodyLength;
        }

        public static byte[] PrefixHeader(ushort transactionId, ReadOnlySpan<byte> body)
        {
            var frame = new byte[HeaderLength + body.Length];
            frame[0] = (byte)(transactionId >> 8);
            frame[1] = (byte)transactionId;
            frame[4] = (byte)(body.Length >> 8);
            frame[5] = (byte)body.Length;
            body.CopyTo(frame.AsSpan(HeaderLength));
            return frame;
        }
    }
}
