using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.Protocol.Modbus;
using log4net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EssSimulator.SiteControl;

internal sealed class SelHostedService(IOptions<SimulatorConfig> options) : BackgroundService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(SelHostedService));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value;
        if (!config.Protocol.EnableSel)
            return;

        var names = SelPcsProtocolWriter.GetTargetNames(config);
        var host = SimulatorHost.Instance;
        ModbusSimServer?[] targets;
        while (true)
        {
            stoppingToken.ThrowIfCancellationRequested();
            targets = names.Select(host.Get<ModbusSimServer>).ToArray();
            if (targets.All(s => s is { IsDataPathReady: true }))
                break;
            await Task.Delay(500, stoppingToken);
        }

        var writer = new SelPcsProtocolWriter(targets.Select(s => s!));
        var server = new SelModbusServer(writer, config.Protocol.SelModbusPort);
        host.Register(server.ServerName, server);
        try
        {
            var report = ProtocolLayerManager.Instance.RegisterAndStart(server, ProtocolDeviceType.Sel, "sel.csv");
            if (report.Errors.Count > 0)
                Log.Error($"SEL 启动失败：{string.Join("；", report.Errors)}");
            else
                Log.Info($"SEL 已注册：{names.Count} 台储能 PCS，端口 {server.Port}，从站号 {server.SlaveId}");
            if (!writer.CanWrite)
                Log.Warn("SEL 控制不可用：需要至少一台在线且从站号为 1 的储能 PCS Modbus 协议，且外部控制权未被占用");

            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            while (await timer.WaitForNextTickAsync(stoppingToken))
                server.RunCycle();
        }
        finally
        {
            server.Stop();
        }
    }
}
