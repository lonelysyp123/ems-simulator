using EssSimulator.DataExchange.Adapters;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Plugins;
using log4net;

namespace EssSimulator.DataExchange.Pipeline
{
    /// <summary>FC 3/4：仿真 → Modbus，变更才写；含插件点位（故障字组字等）。</summary>
    public sealed class TelemetryPipeline
    {
        private readonly PointCatalog _catalog;
        private readonly ISimulationDataAdapter _simulation;
        private readonly IModbusRegisterAdapter _modbus;
        private readonly ShadowStore _shadow;
        private readonly List<(PluginPointBinding Binding, ITelemetryPlugin Plugin)> _pluginPoints = new();
        private readonly ILog _log = LogManager.GetLogger(typeof(TelemetryPipeline));

        public TelemetryPipeline(
            PointCatalog catalog,
            ISimulationDataAdapter simulation,
            IModbusRegisterAdapter modbus,
            ShadowStore shadow,
            TelemetryPluginRegistry? plugins = null)
        {
            _catalog = catalog;
            _simulation = simulation;
            _modbus = modbus;
            _shadow = shadow;

            // 启动时预解析插件点位；无插件覆盖的字键保持默认值 0
            if (plugins != null)
            {
                foreach (var binding in catalog.PluginPoints)
                {
                    var plugin = plugins.Resolve(binding.WordKey);
                    if (plugin != null)
                        _pluginPoints.Add((binding, plugin));
                }
            }
        }

        public void RunOnce()
        {
            if (_catalog.TelemetryPoints.Count == 0 && _pluginPoints.Count == 0 && _catalog.SumPoints.Count == 0 && _catalog.MaxPoints.Count == 0)
                return;

            var writeBuffer = new Dictionary<string, object>();
            foreach (var binding in _catalog.TelemetryPoints)
            {
                try
                {
                    var value = _simulation.Read(binding.Target.FullPath) ?? 0;
                    if (_shadow.TelemetryChanged(binding.ParamName, value))
                        writeBuffer[binding.ParamName] = value;
                }
                catch (Exception ex)
                {
                    _log.Debug($"Telemetry read failed: {binding.ParamName}", ex);
                }
            }

            foreach (var (binding, plugin) in _pluginPoints)
            {
                try
                {
                    var value = plugin.Compute(binding.WordKey, binding.DeviceRoot, _simulation);
                    if (value != null && _shadow.TelemetryChanged(binding.ParamName, value))
                        writeBuffer[binding.ParamName] = value;
                }
                catch (Exception ex)
                {
                    _log.Debug($"Telemetry plugin compute failed: {binding.ParamName}", ex);
                }
            }

            // model=sum：读取各仿真路径并求和，缺失操作数按 0 处理
            foreach (var binding in _catalog.SumPoints)
            {
                try
                {
                    var value = SumPointBinding.ComputeSum(
                        binding.Paths.Select(_simulation.Read).ToArray());
                    if (_shadow.TelemetryChanged(binding.ParamName, value))
                        writeBuffer[binding.ParamName] = value;
                }
                catch (Exception ex)
                {
                    _log.Debug($"Telemetry sum compute failed: {binding.ParamName}", ex);
                }
            }

            // model=max：读取各仿真路径并取最大值，缺失操作数忽略
            foreach (var binding in _catalog.MaxPoints)
            {
                try
                {
                    var value = MaxPointBinding.ComputeMax(
                        binding.Paths.Select(_simulation.Read).ToArray());
                    if (_shadow.TelemetryChanged(binding.ParamName, value))
                        writeBuffer[binding.ParamName] = value;
                }
                catch (Exception ex)
                {
                    _log.Debug($"Telemetry max compute failed: {binding.ParamName}", ex);
                }
            }

            _modbus.WritePoints(writeBuffer);
        }
    }
}
