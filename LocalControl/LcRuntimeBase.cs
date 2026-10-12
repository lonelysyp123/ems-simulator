using log4net;

namespace EssSimulator.LocalControl
{
    /// <summary>
    /// LC 运行时基类：按型号子类化，周期内采集/控制。不触碰物理仿真求解。
    /// </summary>
    internal abstract class LcRuntimeBase
    {
        protected ILog Log { get; }

        protected LcRuntimeBase(ILog log) => Log = log;

        public abstract void RunCycle(
            Func<string, ModbusSimServer?> resolveEmu,
            LocalControlModbusServer lc,
            int lcIdx,
            int emuPerGroup,
            int emuCount);

        /// <summary>子类可在周期末追加采集源（电表、BMS 等）。默认无操作。</summary>
        protected virtual void CollectExtra(
            Func<string, ModbusSimServer?> resolveEmu,
            LocalControlModbusServer lc,
            int lcIdx,
            int emuPerGroup,
            int emuCount)
        {
        }
    }
}
