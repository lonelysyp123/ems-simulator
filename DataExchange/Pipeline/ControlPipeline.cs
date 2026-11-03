using EssSimulator.Core;
using EssSimulator.DataExchange.Adapters;
using EssSimulator.EssSimModelApi.Mappers;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Effects;
using EssSimulator.EssDeviceSimModel;
using log4net;

namespace EssSimulator.DataExchange.Pipeline
{
    /// <summary>FC 5/6/16：Modbus → 仿真，变更才写并触发副作用。</summary>
    public sealed class ControlPipeline
    {
        private readonly PointCatalog _catalog;
        private readonly ISimulationDataAdapter _simulation;
        private readonly IModbusRegisterAdapter _modbus;
        private readonly ModbusParser _parser;
        private readonly ShadowStore _shadow;
        private readonly ControlEffectRegistry _effects;
        private readonly string _serverName;
        private readonly bool _logControlChanges;
        private readonly ILog _log = LogManager.GetLogger(typeof(ControlPipeline));

        public ControlPipeline(
            PointCatalog catalog,
            ISimulationDataAdapter simulation,
            IModbusRegisterAdapter modbus,
            ModbusParser parser,
            ShadowStore shadow,
            ControlEffectRegistry effects,
            string serverName,
            bool logControlChanges)
        {
            _catalog = catalog;
            _simulation = simulation;
            _modbus = modbus;
            _parser = parser;
            _shadow = shadow;
            _effects = effects;
            _serverName = serverName;
            _logControlChanges = logControlChanges;
        }

        public void RunOnce()
        {
            if (ExternalControlGate.IsBlocked)
                return;
            if (_catalog.ControlPoints.Count == 0)
                return;

            var paramNames = _catalog.ControlPoints.Select(p => p.ParamName).ToList();
            var selectedRaw = _modbus.ReadAllControlRaw(paramNames);
            if (selectedRaw.Count == 0)
                return;

            var parsed = _parser.DataParse(selectedRaw);
            foreach (var binding in _catalog.ControlPoints)
            {
                if (!parsed.TryGetValue(binding.ParamName, out var newValue))
                    continue;

                if (!_shadow.TryDetectControlChange(binding.ParamName, newValue, out var previous))
                    continue;

                object applied;
                if (binding.Semantics == ControlSemantics.Edge)
                {
                    if (!TryResolveEdgeTransition(previous, newValue, out var edgeApplied) || edgeApplied == null)
                        continue;
                    applied = edgeApplied;
                }
                else
                {
                    applied = CoerceControlValue(binding, newValue);
                }

                Accept(binding, applied, previous);
            }
        }

        /// <summary>
        /// 先收下设定（shadow），模型写入放到电站步进开头。
        /// 两套 EMS 在同一拍里改同一点时，只保留最后一次。
        /// </summary>
        private void Accept(PointBinding binding, object applied, object? previous)
        {
            string path = binding.Target.FullPath;
            string name = binding.ParamName;
            _shadow.CommitControl(name, applied);
            PlantControlMailbox.Submit(path, () => ApplyToModel(binding, path, name, applied, previous));
        }

        private void ApplyToModel(PointBinding binding, string path, string name, object applied, object? previous)
        {
            if (PlantControlMailbox.IsSuperseded(path))
                return;

            if (!_simulation.Write(path, applied))
            {
                if (!PlantControlMailbox.IsSuperseded(path))
                    _shadow.CommitControl(name, previous);
                _log.Warn($"Control write failed: {path} <= {applied}");
                return;
            }

            ControlPointCapture.Current.OnControlApplied(_serverName, binding, applied, previous);
            DeviceControlFacade.OnPvModelControlWritten(path);

            if (_logControlChanges)
            {
                _log.Info(
                    $"[{ControlLogPrefix}-Control:change] {_serverName}.{name}: -> {FormatValue(applied)}");
            }

            if (binding.Effect != ControlEffectId.None)
            {
                _effects.Dispatch(binding.Effect, new ControlEffectContext
                {
                    ServerName = _serverName,
                    Binding = binding,
                    AppliedValue = applied,
                    PreviousValue = previous
                });
            }
        }

        /// <summary>
        /// 边沿型控制点（如黑启动）：仅 0→1 / 1→0 变化时写入仿真。
        /// </summary>
        private bool TryResolveEdgeTransition(object? previous, object incoming, out object? applied)
        {
            applied = null;
            bool prevOn = CoerceToBool(previous);
            bool nextOn = CoerceToBool(incoming);

            if (nextOn && !prevOn)
            {
                applied = true;
                return true;
            }

            if (!nextOn && prevOn)
            {
                applied = false;
                return true;
            }

            return false;
        }

        private static bool CoerceToBool(object? value) =>
            value switch
            {
                null => false,
                bool b => b,
                string s when bool.TryParse(s, out var bv) => bv,
                _ => Convert.ToDouble(value) != 0
            };

        private object CoerceControlValue(PointBinding binding, object valToSet)
        {
            if (valToSet is string s)
            {
                if (double.TryParse(s, out var dv))
                    valToSet = dv;
                else if (bool.TryParse(s, out var bv))
                    valToSet = bv ? 1 : 0;
            }

            if (binding.Entry.FunctionCode != 5)
                return valToSet;

            bool coilBool = valToSet switch
            {
                bool b => b,
                string str when bool.TryParse(str, out var bv) => bv,
                _ => Convert.ToDouble(valToSet) != 0
            };

            try
            {
                var current = _simulation.Read(binding.Target.FullPath);
                if (current is bool)
                    return coilBool;
            }
            catch (Exception ex)
            {
                _log.Debug($"CoerceControlValue read failed: {binding.Target.FullPath}", ex);
            }

            return coilBool ? 1 : 0;
        }

        private string ControlLogPrefix =>
            _serverName.StartsWith("simPvInv", StringComparison.OrdinalIgnoreCase) ? "PV-Inverter" :
            _serverName.StartsWith("simBms", StringComparison.OrdinalIgnoreCase) ? "BMS" : "EMU";

        private static string FormatValue(object? value) =>
            value == null ? "<null>" : value.ToString() ?? "<null>";
    }
}
