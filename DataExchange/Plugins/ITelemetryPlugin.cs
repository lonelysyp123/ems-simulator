using EssSimulator.DataExchange.Adapters;

namespace EssSimulator.DataExchange.Plugins
{
    /// <summary>
    /// 设备遥测插件：对点表中以 model=plugin 标记的点位做特殊取值/组合
    /// （如协议故障字按位组合仿真故障布尔量）。
    /// 不同设备型号可注册各自插件；未覆盖的字键保持默认值 0。
    /// </summary>
    public interface ITelemetryPlugin
    {
        /// <summary>是否处理该字键（点表 ModelSim 的 arg1）。</summary>
        bool CanHandle(string wordKey);

        /// <summary>
        /// 从仿真数据计算点位值。
        /// </summary>
        /// <param name="wordKey">字键（如 ModuleWarningWord1）。</param>
        /// <param name="deviceRoot">设备根路径（如 emu1.PcsList[0]，点表 ModelSim 的 arg2）。</param>
        /// <returns>点位值；返回 null 表示放弃处理，点位保持默认值。</returns>
        object? Compute(string wordKey, string deviceRoot, ISimulationDataAdapter simulation);
    }
}
