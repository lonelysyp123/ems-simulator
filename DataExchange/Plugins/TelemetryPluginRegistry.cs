namespace EssSimulator.DataExchange.Plugins
{
    /// <summary>遥测插件注册表：按注册顺序解析字键对应的插件。</summary>
    public sealed class TelemetryPluginRegistry
    {
        private readonly List<ITelemetryPlugin> _plugins = new();

        public TelemetryPluginRegistry Register(ITelemetryPlugin plugin)
        {
            _plugins.Add(plugin);
            return this;
        }

        public ITelemetryPlugin? Resolve(string wordKey)
        {
            if (string.IsNullOrWhiteSpace(wordKey))
                return null;

            foreach (var plugin in _plugins)
            {
                if (plugin.CanHandle(wordKey))
                    return plugin;
            }

            return null;
        }
    }
}
