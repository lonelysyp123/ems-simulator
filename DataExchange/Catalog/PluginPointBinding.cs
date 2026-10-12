namespace EssSimulator.DataExchange.Catalog
{
    /// <summary>
    /// 插件点位绑定：ModelSim 为 model=plugin|arg1=&lt;字键&gt;|arg2=&lt;设备根路径&gt; 的遥测点。
    /// 取值不走反射路径，由遥测插件按字键组合计算。
    /// </summary>
    public sealed class PluginPointBinding
    {
        public required MapEntry Entry { get; init; }
        public required string ParamName { get; init; }

        /// <summary>字键（ModelSim arg1），如 ModuleWarningWord1。</summary>
        public required string WordKey { get; init; }

        /// <summary>设备根路径（ModelSim arg2），如 emu1.PcsList[0]。</summary>
        public required string DeviceRoot { get; init; }
    }
}
