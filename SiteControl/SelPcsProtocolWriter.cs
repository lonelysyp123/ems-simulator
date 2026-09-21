using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.LocalControl;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.SiteControl;

internal class SelPcsProtocolWriter
{
    private readonly Target[] _targets;

    public SelPcsProtocolWriter(IEnumerable<ModbusSimServer> servers)
    {
        _targets = servers.Select(server => new Target(
            server, FindPoint(server, "IslandVoltageSetting"),
            FindPoint(server, "IslandFrequencySetting"))).ToArray();
    }

    public bool CanWrite => _targets.Length > 0 && !ExternalControlGate.IsBlocked
        && _targets.All(t => t.Server.IsOnline && t.Server.IsDataPathReady && t.Server.SlaveId == 1);

    public static IReadOnlyList<string> GetTargetNames(SimulatorConfig config) =>
        config.EffectiveEssUnitCount == 0
            ? Array.Empty<string>()
            : EmuProtocolLayout.Enumerate(config).Select(e => e.ServerName).ToArray();

    public virtual void Write(SelReferenceCommand command)
    {
        if (!CanWrite)
            throw new InvalidOperationException("SEL 下发被阻止：PCS Modbus 未就绪、从站号不是 1 或外部控制权被占用");

        foreach (var target in _targets)
        {
            if (command.VoltageV is double voltage)
                Send(target.Server, target.Voltage, voltage);
            if (command.FrequencyHz is double frequency)
                Send(target.Server, target.Frequency, frequency);
        }
    }

    private static MapEntry FindPoint(ModbusSimServer server, string property)
    {
        var point = server.ControlMaps.SingleOrDefault(p => p.FunctionCode == 6
            && p.ParamName != null
            && server.PointMap.ParamModelLookup.TryGetValue(p.ParamName, out var model)
            && model.Arg1?.EndsWith("." + property, StringComparison.Ordinal) == true);
        return point ?? throw new InvalidOperationException(
            $"{server.ServerName} 点表缺少可写的 {property} 寄存器");
    }

    private static void Send(ModbusSimServer server, MapEntry point, double engineeringValue)
    {
        var raw = ModbusPointCodec.ScaleToRaw(
            engineeringValue, ModbusPointCodec.ToClrType(point), point.Scale);
        server.SetDataObjectByMesurePointName(point.ParamName!, raw);
    }

    private sealed record Target(ModbusSimServer Server, MapEntry Voltage, MapEntry Frequency);
}
