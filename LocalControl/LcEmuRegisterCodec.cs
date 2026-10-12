using EssSimulator.Protocol.Modbus;

namespace EssSimulator.LocalControl
{
    /// <summary>
    /// LC 读点得到工程值；EMU <c>SetDataObjectByMesurePointName</c> 按原始寄存器写入。
    /// 转发前按 EMU 点表 Scale 换成 raw，避免 yt0/yt1 Scale=10 把 100 kW 写成 10 kW。
    /// </summary>
    internal static class LcEmuRegisterCodec
    {
        public static object ToControlRegisterRaw(
            IReadOnlyList<MapEntry> controlMaps,
            string paramName,
            double engineering)
        {
            var entry = Find(controlMaps, paramName);
            if (entry == null)
                return engineering;
            return ModbusPointCodec.ScaleToRaw(engineering, ModbusPointCodec.ToClrType(entry), entry.Scale);
        }

        private static MapEntry? Find(IReadOnlyList<MapEntry> controlMaps, string paramName)
        {
            for (int i = 0; i < controlMaps.Count; i++)
            {
                var e = controlMaps[i];
                if (string.Equals(e.ParamName, paramName, StringComparison.Ordinal))
                    return e;
            }

            return null;
        }
    }
}
