using EssSimulator.DataExchange.Adapters;
using EssSimulator.DataExchange.Catalog;
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

            _shadow.CommitControl(paramName, applied);
            return true;
        }

        private string FeedbackLogPrefix =>
            _serverName.StartsWith("simPvInv", StringComparison.OrdinalIgnoreCase) ? "PV-Inverter" :
            _serverName.StartsWith("simBms", StringComparison.OrdinalIgnoreCase) ? "BMS" : "EMU";

        private static string FormatValue(object? value) =>
            value == null ? "<null>" : value.ToString() ?? "<null>";
    }
}
