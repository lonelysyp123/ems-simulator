namespace EssSimulator.EssDeviceSimModel
{
    /// <summary>
    /// 内部电气符号约定。协议层再转换成现有充放电符号，本阶段不改点表。
    /// </summary>
    public static class ElectricalSignConvention
    {
        /// <summary>端口电流以流入设备为正。</summary>
        public const bool InflowPositive = true;
    }
}
