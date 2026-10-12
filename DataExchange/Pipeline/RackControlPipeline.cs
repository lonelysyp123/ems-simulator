using EssSimulator.Core;
using EssSimulator.DataExchange.Adapters;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.EssDeviceSimModel;
using log4net;

namespace EssSimulator.DataExchange.Pipeline
{
    /// <summary>
    /// BMS 簇级 FC5/6/16：按 rack 从站读 Holding/Coil，写回 <c>Cluseter[rackId].*</c> 路径。
    /// </summary>
    public sealed class RackControlPipeline
    {
        private readonly IReadOnlyList<RackPointBinding> _points;
        private readonly ISimulationDataAdapter _simulation;
        private readonly IModbusRegisterAdapter _modbus;
        private readonly ModbusParser _parser;
        private readonly ShadowStore _shadow;
        private readonly int _clusterCount;
        private readonly byte _baseSlaveId;
        private readonly string _serverName;
        private readonly bool _logControlChanges;
        private readonly ILog _log = LogManager.GetLogger(typeof(RackControlPipeline));

        public RackControlPipeline(
            IReadOnlyList<RackPointBinding> points,
            ISimulationDataAdapter simulation,
            IModbusRegisterAdapter modbus,
            ModbusParser parser,
            ShadowStore shadow,
            int clusterCount,
            byte baseSlaveId,
            string serverName,
            bool logControlChanges)
        {
            _points = points;
            _simulation = simulation;
            _modbus = modbus;
            _parser = parser;
            _shadow = shadow;
            _clusterCount = clusterCount;
            _baseSlaveId = baseSlaveId;
            _serverName = serverName;
            _logControlChanges = logControlChanges;
        }

        public IReadOnlyList<RackPointBinding> Points => _points;
        public int ClusterCount => _clusterCount;
        public byte BaseSlaveId => _baseSlaveId;

        public RackPointBinding? Find(string paramName) =>
            _points.FirstOrDefault(p =>
                string.Equals(p.ParamName, paramName, StringComparison.OrdinalIgnoreCase));

        public void RunOnce()
        {
            if (ExternalControlGate.IsBlocked)
                return;
            if (_points.Count == 0 || _clusterCount <= 0)
                return;

            var paramNames = _points.Select(p => p.ParamName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            for (int rackId = 0; rackId < _clusterCount; rackId++)
                RunForRack(rackId, paramNames);
        }

        public void RunForRack(int rackId)
        {
            if (ExternalControlGate.IsBlocked)
                return;
            if (_points.Count == 0 || rackId < 0 || rackId >= _clusterCount)
                return;

            var paramNames = _points.Select(p => p.ParamName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            RunForRack(rackId, paramNames);
        }

        private void RunForRack(int rackId, IReadOnlyList<string> paramNames)
        {
            byte slaveId = (byte)(_baseSlaveId + rackId + 1);
            var selectedRaw = _modbus.ReadAllControlRaw(paramNames, slaveId);
            if (selectedRaw.Count == 0)
                return;

            var parsed = _parser.DataParse(selectedRaw);
            foreach (var binding in _points)
            {
                if (!parsed.TryGetValue(binding.ParamName, out var newValue))
                    continue;

                string shadowKey = $"{rackId}:{binding.ParamName}";
                if (!_shadow.TryDetectControlChange(shadowKey, newValue, out var previous))
                    continue;

                object applied = CoerceValue(binding, newValue);

                // 未 seed 的首次观测：若寄存器仍为 0，只对齐 shadow，避免冲掉模型默认门限。
                // （正常路径由 DataExchangeSession.InitializeRackControlRegistersFromSimulation seed）
                if (previous == null && IsNumericZero(applied))
                {
                    _shadow.CommitControl(shadowKey, applied);
                    continue;
                }

                string path = binding.ResolvePath(rackId);
                string paramName = binding.ParamName;
                int rack = rackId;
                _shadow.CommitControl(shadowKey, applied);
                PlantControlMailbox.Submit(path, () =>
                {
                    if (PlantControlMailbox.IsSuperseded(path))
                        return;

                    if (!_simulation.Write(path, applied))
                    {
                        if (!PlantControlMailbox.IsSuperseded(path))
                            _shadow.CommitControl(shadowKey, previous);
                        _log.Warn($"Rack control write failed: {path} <= {applied}");
                        return;
                    }

                    if (_logControlChanges)
                    {
                        _log.Info(
                            $"[BMS-RackControl:change] {_serverName}.r{rack}.{paramName}: -> {applied}");
                    }
                });
            }
        }

        private static object CoerceValue(RackPointBinding binding, object valToSet)
        {
            if (valToSet is string s)
            {
                if (double.TryParse(s, out var dv))
                    valToSet = dv;
                else if (bool.TryParse(s, out var bv))
                    valToSet = bv ? 1 : 0;
            }

            if (binding.Entry.FunctionCode == 5)
            {
                return valToSet switch
                {
                    bool b => b,
                    _ => Convert.ToDouble(valToSet) != 0
                };
            }

            return valToSet switch
            {
                double d => (float)d,
                float f => f,
                int i => (float)i,
                long l => (float)l,
                _ => valToSet
            };
        }

        private static bool IsNumericZero(object value) =>
            value switch
            {
                bool b => !b,
                float f => Math.Abs(f) < 1e-12f,
                double d => Math.Abs(d) < 1e-12,
                int i => i == 0,
                long l => l == 0,
                _ => Math.Abs(Convert.ToDouble(value)) < 1e-12
            };
    }
}
