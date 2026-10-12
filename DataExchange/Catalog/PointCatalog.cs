namespace EssSimulator.DataExchange.Catalog
{
    public sealed class PointCatalog
    {
        public required string ServerName { get; init; }
        public required IReadOnlyList<PointBinding> TelemetryPoints { get; init; }
        public required IReadOnlyList<PointBinding> ControlPoints { get; init; }
        public IReadOnlyList<RackPointBinding> RackTelemetryPoints { get; init; } = Array.Empty<RackPointBinding>();
        public IReadOnlyList<RackPointBinding> RackControlPoints { get; init; } = Array.Empty<RackPointBinding>();
        public IReadOnlyList<PluginPointBinding> PluginPoints { get; init; } = Array.Empty<PluginPointBinding>();
        public IReadOnlyList<SumPointBinding> SumPoints { get; init; } = Array.Empty<SumPointBinding>();
        public IReadOnlyList<MaxPointBinding> MaxPoints { get; init; } = Array.Empty<MaxPointBinding>();
        public required IReadOnlyDictionary<string, object> DefaultValues { get; init; }

        public PointBinding? FindControl(string paramName) =>
            ControlPoints.FirstOrDefault(p =>
                string.Equals(p.ParamName, paramName, StringComparison.OrdinalIgnoreCase));

        public PointBinding? FindTelemetry(string paramName) =>
            TelemetryPoints.FirstOrDefault(p =>
                string.Equals(p.ParamName, paramName, StringComparison.OrdinalIgnoreCase));

        public RackPointBinding? FindRackControl(string paramName) =>
            RackControlPoints.FirstOrDefault(p =>
                string.Equals(p.ParamName, paramName, StringComparison.OrdinalIgnoreCase));
    }
}
