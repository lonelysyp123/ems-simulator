using EssSimulator.DataExchange.Adapters;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.EssDeviceSimModel;
using log4net;

namespace EssSimulator.DataExchange.Pipeline
{
    /// <summary>FC 5/6/16：仿真 → Modbus 控制反馈，按 shadow 去重，不触发副作用。</summary>
    public sealed class ControlFeedbackPipeline
    {
        private readonly PointCatalog _catalog;
        private readonly ISimulationDataAdapter _simulation;
        private readonly IModbusRegisterAdapter _modbus;
        private readonly ShadowStore _shadow;
        private readonly string _serverName;
        private readonly bool _logFeedback;
        private readonly ILog _log = LogManager.GetLogger(typeof(ControlFeedbackPipeline));

        public ControlFeedbackPipeline(
            PointCatalog catalog,
            ISimulationDataAdapter simulation,
            IModbusRegisterAdapter modbus,
            ShadowStore shadow,
            string serverName,
            bool logFeedback)
        {
            _catalog = catalog;
            _simulation = simulation;
            _modbus = modbus;
            _shadow = shadow;
            _serverName = serverName;
            _logFeedback = logFeedback;
        }

        public void RunOnce()
        {
            if (_catalog.ControlPoints.Count == 0)
                return;

            var writeBuffer = new Dictionary<string, object>();
            foreach (var binding in _catalog.ControlPoints)
            {
                try
                {
                    // 设定已收下、模型还没在本拍步进里更新时，不要用旧模型值覆盖 EMS 刚写的寄存器。
                    if (PlantControlMailbox.IsPending(binding.Target.FullPath))
                        continue;

                    var raw = _simulation.Read(binding.Target.FullPath);
                    if (raw == null)
                        continue;

                    var applied = ControlValueCoercion.CoerceForModbusRegister(binding, raw);
                    if (!_shadow.TryDetectControlChange(binding.ParamName, applied, out _))
                        continue;

                    writeBuffer[binding.ParamName] = applied;
                }
                catch (Exception ex)
                {
                    _log.Debug($"Feedback read failed: {binding.ParamName}", ex);
                }
            }

            foreach (var pair in writeBuffer)
                WriteWithReadback(pair.Key, pair.Value);
        }

        /// <summary> imperative 回写（LC / 启动 bootstrap），写 Modbus 并推进 shadow。</summary>
        public bool PublishImmediate(string paramName, object value, out object applied)
        {
            applied = value;
            var binding = _catalog.FindControl(paramName);
            if (binding == null)
                return false;

            applied = ControlValueCoercion.CoerceForModbusRegister(binding, value);
            return WriteWithReadback(paramName, applied);
        }

        private bool WriteWithReadback(string paramName, object applied)
        {
            try
            {
                _modbus.WritePoints(new Dictionary<string, object> { { paramName, applied } });
            }
            catch (Exception ex)
            {
                _log.Warn($"Feedback write failed for {paramName}: {ex.Message}");
                return false;
            }

            object? readback = null;
            bool readbackOk = false;
            try
            {
                readback = _modbus.ReadParsedPoint(paramName);
                readbackOk = ShadowStore.ValuesEqual(applied, readback);
            }
            catch (Exception ex)
            {
                _log.Warn($"Feedback readback failed for {paramName}: {ex.Message}");
            }

            // 整数寄存器按系数四舍五入后，回读是本次写入的量化结果。
            // shadow 必须记回读值，否则下一次控制扫描会把它当成新命令。
            object? shadowValue = applied;
            if (!readbackOk && TryAcceptQuantizedReadback(paramName, applied, readback))
            {
                readbackOk = true;
                shadowValue = readback;
            }

            if (_logFeedback)
            {
                _log.Debug(
                    $"[{FeedbackLogPrefix}-Feedback] {_serverName}.{paramName} write={FormatValue(applied)} readback={FormatValue(readback)} ok={readbackOk}");
            }

            if (!readbackOk)
            {
                _log.Warn(
                    $"Feedback shadow not updated: {_serverName}.{paramName}, write={FormatValue(applied)}, readback={FormatValue(readback)}");
                return false;
            }

            _shadow.CommitControl(paramName, shadowValue);
            return true;
        }

        /// <summary>
        /// 回读等于 round(applied × scale) / scale，且偏差不超过半个量化单位时，视为本次反馈写成功。
        /// 遥测覆盖等偏离量化结果的回读不接受。
        /// </summary>
        private bool TryAcceptQuantizedReadback(string paramName, object applied, object? readback)
        {
            if (readback == null || applied is bool || readback is bool)
                return false;

            var binding = _catalog.FindControl(paramName);
            int scale = binding?.Entry.Scale ?? 0;
            if (binding == null || scale <= 1 || !IsIntegerRegister(binding.Entry))
                return false;

            if (!TryToDouble(applied, out double written) || !TryToDouble(readback, out double echoed))
                return false;
            if (!double.IsFinite(written) || !double.IsFinite(echoed))
                return false;

            double expected = Math.Round(written * scale, MidpointRounding.ToEven) / scale;
            if (Math.Abs(echoed - expected) > 1e-6)
                return false;

            double halfQuantum = 0.5 / scale;
            return Math.Abs(written - echoed) <= halfQuantum + 1e-9;
        }

        private static bool IsIntegerRegister(MapEntry entry)
        {
            switch (entry.Type?.Trim().ToLowerInvariant())
            {
                case "u16":
                case "int16":
                case "s16":
                case "u32":
                case "int32":
                case "s32":
                case "u64":
                case "int64":
                case "s64":
                    return true;
                case "float":
                case "single":
                case "double":
                case "bool":
                    return false;
                default:
                    return entry.Size is 16 or 32 or 64;
            }
        }

        private static bool TryToDouble(object value, out double number)
        {
            switch (value)
            {
                case double d:
                    number = d;
                    return true;
                case float f:
                    number = f;
                    return true;
                case int or long or short or byte or uint or ulong or ushort:
                    number = Convert.ToDouble(value);
                    return true;
                default:
                    return double.TryParse(value.ToString(), out number);
            }
        }

        private string FeedbackLogPrefix =>
            _serverName.StartsWith("simPvInv", StringComparison.OrdinalIgnoreCase) ? "PV-Inverter" :
            _serverName.StartsWith("simBms", StringComparison.OrdinalIgnoreCase) ? "BMS" : "EMU";

        private static string FormatValue(object? value) =>
            value == null ? "<null>" : value.ToString() ?? "<null>";
    }
}
