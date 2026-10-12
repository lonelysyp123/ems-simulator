using EssSimulator.Protocol.Modbus;
using log4net;

namespace EssSimulator.LocalControl
{
    /// <summary>
    /// LC 运行时始终为协议桥；选中互斥 EMU 点表时由从站 <c>UsesDataExchange</c> 跳过桥接。
    /// </summary>
    internal static class LcRuntimeFactory
    {
        public static LcRuntimeBase Create(ILog? log = null) =>
            new StandardLcRuntime(log ?? LogManager.GetLogger(typeof(LcRuntimeFactory)));

        /// <summary>兼容旧调用；型号 id 已无意义，一律协议桥。</summary>
        public static LcRuntimeBase Create(string? lcModelId, ILog? log = null)
        {
            _ = lcModelId;
            return Create(log);
        }
    }
}
