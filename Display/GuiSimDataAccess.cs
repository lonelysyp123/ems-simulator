using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using log4net;

namespace EssSimulator.Display
{
    /// <summary>GUI/Web 读取仿真对象路径的辅助方法。</summary>
    public static class GuiSimDataAccess
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(GuiSimDataAccess));

        public static int GetEssUnitCount()
        {
            try
            {
                var list = SimServer.GetExtIfVariableVal("ess._pcsList");
                return list is System.Collections.ICollection c ? c.Count : 0;
            }
            catch (Exception ex)
            {
                Log.Debug("GetEssUnitCount 失败，回退 2", ex);
                return 2;
            }
        }

        public static int GetPvUnitCount()
        {
            try
            {
                var list = SimServer.GetExtIfVariableVal("ess.PvUnits");
                return list is System.Collections.ICollection c ? c.Count : 0;
            }
            catch (Exception ex)
            {
                Log.Debug("GetPvUnitCount 失败，回退 0", ex);
                return 0;
            }
        }

        /// <summary>各储能单元下属 PCS 台数布局；ESS 未建/读取失败时返回空列表（调用方自行回退每单元 2 台）。</summary>
        public static IReadOnlyList<int> GetPcsPerUnit()
        {
            try
            {
                if (SimServer.GetExtIfVariableVal("ess.PcsPerUnit") is IReadOnlyList<int> list && list.Count > 0)
                    return list;
            }
            catch (Exception ex)
            {
                Log.Debug("GetPcsPerUnit 失败，返回空布局", ex);
            }
            return Array.Empty<int>();
        }

        /// <summary>物理储能单元（EMU）个数；布局缺失时回退按通道数每单元 2 台推算。</summary>
        public static int GetPhysicalUnitCount()
        {
            var layout = GetPcsPerUnit();
            if (layout.Count > 0)
                return layout.Count;
            int channelCount = Math.Max(1, GetEssUnitCount());
            return Math.Max(1, (int)Math.Ceiling(channelCount / 2.0));
        }

        public static int GetMainLineSectionCount(int unitsPerSection)
        {
            int unitCount = Math.Max(1, GetPhysicalUnitCount());
            return Math.Max(1, (int)Math.Ceiling(unitCount / (double)Math.Max(1, unitsPerSection)));
        }

        public static int ClampMainLineSectionIndex(int requestedSectionIndex, int unitsPerSection)
        {
            int sectionCount = GetMainLineSectionCount(unitsPerSection);
            return Math.Clamp(requestedSectionIndex, 0, sectionCount - 1);
        }

        /// <summary>从 bms1 数据模型读取簇数量，失败时回退 12。</summary>
        public static int GetClusterCount()
        {
            try
            {
                var v = SimServer.GetExtIfVariableVal("bms1.BatteryStacks[0].Cluseter.Count");
                if (v is int i) return Math.Max(1, i);
                if (v is long l) return (int)Math.Max(1, Math.Min(int.MaxValue, l));
                if (v != null && int.TryParse(v.ToString(), out int p)) return Math.Max(1, p);
            }
            catch (Exception ex)
            {
                Log.Debug("GetClusterCount 失败，回退 12", ex);
            }
            return 12;
        }

        public static double SafeGetDouble(string path, double fallback = 0)
        {
            try
            {
                var o = SimServer.GetExtIfVariableVal(path);
                if (o == null) return fallback;
                double value = Convert.ToDouble(o);
                return double.IsFinite(value) ? value : fallback;
            }
            catch (Exception ex)
            {
                Log.Debug($"SafeGetDouble 失败: {path}", ex);
                return fallback;
            }
        }

        public static bool SafeGetBool(string path, bool fallback = false)
        {
            try
            {
                var o = SimServer.GetExtIfVariableVal(path);
                if (o == null) return fallback;
                return Convert.ToBoolean(o);
            }
            catch (Exception ex)
            {
                Log.Debug($"SafeGetBool 失败: {path}", ex);
                return fallback;
            }
        }

        public static string SafeGetString(string path, string fallback = "")
        {
            try
            {
                var o = SimServer.GetExtIfVariableVal(path);
                return o?.ToString() ?? fallback;
            }
            catch (Exception ex)
            {
                Log.Debug($"SafeGetString 失败: {path}", ex);
                return fallback;
            }
        }

        public static object? TryGetObject(string path)
        {
            try
            {
                return SimServer.GetExtIfVariableVal(path);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>读取 EMU Modbus 控制线圈当前值（如 yk3 启停），失败时回退仿真 DTO。</summary>
        public static bool GetEmuPcsStartStopCoil(int unitIndex0, int pcsSlotInUnit0)
        {
            const string paramName = "yk3";
            int global0 = ResolveGlobalPcsIndex(unitIndex0, pcsSlotInUnit0);
            try
            {
                var server = SimulatorHost.Instance.Get<IModbusRegisterServer>($"simEmu{global0 + 1}");
                if (server != null)
                {
                    var raw = server.GetDataObjectByMesurePointName(paramName);
                    if (raw != null)
                        return raw switch
                        {
                            bool b => b,
                            string s when bool.TryParse(s, out var bv) => bv,
                            _ => Convert.ToDouble(raw) != 0
                        };
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"GetEmuPcsStartStopCoil 读 Modbus 失败: simEmu{global0 + 1}.{paramName}", ex);
            }

            return SafeGetBool($"emu{unitIndex0 + 1}.PcsList[{pcsSlotInUnit0}].pcsOnOffSwitch");
        }

        private static int ResolveGlobalPcsIndex(int unitIndex0, int pcsSlotInUnit0)
        {
            var layout = GetPcsPerUnit();
            if (layout.Count > 0)
                return PcsUnitLayout.BaseIndexOfUnit(layout, unitIndex0) + pcsSlotInUnit0;
            return unitIndex0 * 2 + pcsSlotInUnit0;
        }
    }
}
