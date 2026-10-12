namespace EssSimulator.Protocol.Modbus
{
    /// <summary>
    /// 点表 CSV Type/Size → CLR 类型与寄存器字节序。
    /// 32 位量（int32/u32/float）与现有从站一致，使用 CDAB 字交换。
    /// </summary>
    public static class ModbusPointCodec
    {
        public static string ToClrType(MapEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            return ToClrType(entry.Type, entry.Size);
        }

        public static string ToClrType(string? csvType, int size)
        {
            switch (csvType?.Trim().ToLowerInvariant())
            {
                case "bool":
                    return "System.Boolean";
                case "u16":
                    return "System.UInt16";
                case "int16":
                case "s16":
                    return "System.Int16";
                case "u32":
                    return "System.UInt32";
                case "int32":
                case "s32":
                    return "System.Int32";
                case "float":
                case "single":
                    return "System.Single";
                case "u64":
                    return "System.UInt64";
                case "int64":
                case "s64":
                    return "System.Int64";
                case "double":
                    return "System.Double";
            }

            return size switch
            {
                1 => "System.Boolean",
                16 => "System.Int16",
                32 => "System.Int32",
                64 => "System.Int64",
                _ => throw new ArgumentException($"无法从 Type={csvType} Size={size} 解析点表类型")
            };
        }

        public static string ByteOrder(string clrType) =>
            clrType switch
            {
                "System.Int32" or "System.UInt32" or "System.Single" => "CDAB",
                "System.Int64" or "System.UInt64" or "System.Double" => "ABCDEFGH",
                _ => "AB"
            };

        public static byte[] Encode(object value, MapEntry entry, bool applyScale)
        {
            ArgumentNullException.ThrowIfNull(entry);
            string clr = ToClrType(entry);
            object raw = applyScale ? ScaleToRaw(value, clr, entry.Scale) : CoerceRaw(value, clr);
            byte[] littleEndian = Common.DataUnTranslation(raw, clr);
            if (clr == "System.Boolean")
                return littleEndian;
            return Common.ConverByteOrder(littleEndian, ByteOrder(clr));
        }

        public static object Decode(byte[] registerBytes, MapEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(registerBytes);
            string clr = ToClrType(entry);
            if (clr == "System.Boolean")
                return Common.DataTranslation(registerBytes, 0, entry.Size, clr);

            byte[] littleEndian = Common.ConverByteOrder(registerBytes, ByteOrder(clr));
            object raw = Common.DataTranslation(littleEndian, 0, entry.Size, clr);
            int scale = entry.Scale == 0 ? 1 : entry.Scale;
            return Convert.ToDouble(raw) / scale;
        }

        internal static object ScaleToRaw(object engineering, string clrType, int scale)
        {
            double d = Convert.ToDouble(engineering) * (scale == 0 ? 1 : scale);
            return CoerceNumeric(d, clrType, roundInteger: true);
        }

        internal static object CoerceRaw(object value, string clrType)
        {
            if (clrType == "System.Boolean")
                return ToBoolean(value);
            if (clrType == "System.Single")
                return Convert.ToSingle(value);
            double d = Convert.ToDouble(value);
            return CoerceNumeric(d, clrType, roundInteger: true);
        }

        private static object CoerceNumeric(double d, string clrType, bool roundInteger)
        {
            if (roundInteger && clrType is not ("System.Single" or "System.Double" or "System.Boolean"))
            {
                if (double.IsNaN(d) || double.IsInfinity(d))
                    d = 0;
                else
                    d = Math.Round(d);
            }

            return clrType switch
            {
                "System.Int16" => Convert.ToInt16(Math.Clamp(d, short.MinValue, short.MaxValue)),
                "System.UInt16" => Convert.ToUInt16(Math.Clamp(d, ushort.MinValue, ushort.MaxValue)),
                "System.Int32" => Convert.ToInt32(Math.Clamp(d, int.MinValue, int.MaxValue)),
                "System.UInt32" => Convert.ToUInt32(Math.Clamp(d, uint.MinValue, uint.MaxValue)),
                "System.Int64" => Convert.ToInt64(Math.Clamp(d, long.MinValue, long.MaxValue)),
                "System.UInt64" => Convert.ToUInt64(Math.Clamp(d, ulong.MinValue, (double)ulong.MaxValue)),
                "System.Single" => Convert.ToSingle(d),
                "System.Double" => d,
                "System.Boolean" => d != 0,
                _ => d
            };
        }

        private static bool ToBoolean(object value) =>
            value switch
            {
                bool b => b,
                string s when bool.TryParse(s, out var bv) => bv,
                string s when int.TryParse(s, out var iv) => iv != 0,
                _ => Convert.ToDouble(value) != 0
            };
    }
}
