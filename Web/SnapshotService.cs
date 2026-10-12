using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.Display;
using EssSimulator.Web.Topology;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace EssSimulator.Web
{
    /// <summary>周期采样仿真状态并通过 SignalR 推送（主接线/BMS/连接）。</summary>
    public sealed class SnapshotService : BackgroundService, IUiSnapshotNotifier
    {
        private readonly IHubContext<RealtimeHub> _hub;
        private readonly SimulatorConfig _simCfg;
        private readonly WebConfig _webCfg;
        private readonly TopologyStore _topologyStore;
        private readonly object _kickLock = new();
        private TaskCompletionSource _kick =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SnapshotService(
            IHubContext<RealtimeHub> hub,
            IOptions<SimulatorConfig> simCfg,
            IOptions<WebConfig> webCfg,
            TopologyStore topologyStore)
        {
            _hub = hub;
            _simCfg = simCfg.Value;
            _webCfg = webCfg.Value;
            _topologyStore = topologyStore;
            UiSnapshotNotifier.Current = this;
        }

        public void RequestImmediatePush() => Kick();

        private void Kick()
        {
            lock (_kickLock)
                _kick.TrySetResult();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 等待仿真基本就绪
            while (!stoppingToken.IsCancellationRequested && !IsSimulatorReady())
            {
                try { await Task.Delay(200, stoppingToken); }
                catch (TaskCanceledException) { return; }
            }

            // 与主循环/控制轮询同量级；默认 200ms，下限 50ms
            int intervalMs = Math.Clamp(_webCfg.SnapshotIntervalMs, 50, 5000);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try { await PushAll(stoppingToken); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"SnapshotService 采样失败: {ex.Message}");
                    }

                    try { await WaitNextAsync(intervalMs, stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
            }
            finally
            {
                if (ReferenceEquals(UiSnapshotNotifier.Current, this))
                    UiSnapshotNotifier.Reset();
            }
        }

        private async Task WaitNextAsync(int intervalMs, CancellationToken ct)
        {
            TaskCompletionSource kickTcs;
            lock (_kickLock)
                kickTcs = _kick;

            var winner = await Task.WhenAny(Task.Delay(intervalMs, ct), kickTcs.Task);
            if (winner == kickTcs.Task)
            {
                lock (_kickLock)
                {
                    if (ReferenceEquals(_kick, kickTcs))
                        _kick = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
        }

        private bool IsSimulatorReady()
        {
            var store = EssSimulator.Core.SimulatorHost.Instance;
            return store.Contains("ess") && store.Contains("simEm")
                && (store.Contains("simBms1") || store.Contains("simPv1"));
        }

        private async Task PushAll(CancellationToken ct)
        {
            var mainLine = MainLineEnricher.Build(_topologyStore);
            await _hub.Clients.Group(RealtimeChannels.MainLine)
                .SendAsync(RealtimeMethods.ReceiveMainLine, mainLine, ct);

            var conn = ConnectionSnapshotReader.Read();
            await _hub.Clients.Group(RealtimeChannels.Connections)
                .SendAsync(RealtimeMethods.ReceiveConnections, conn, ct);

            int unitCount = Math.Max(1, GuiSimDataAccess.GetEssUnitCount());
            for (int i = 0; i < unitCount; i++)
            {
                try
                {
                    var overview = BatterySnapshotReader.ReadOverview(i);
                    await _hub.Clients.Group($"{RealtimeChannels.Battery}.{i + 1}")
                        .SendAsync(RealtimeMethods.ReceiveBattery, overview, ct);
                }
                catch { /* 单个舱失败不影响其他 */ }
            }

            var alert = FatalSystemAlert.GetSnapshot();
            if (alert.IsActive)
            {
                await _hub.Clients.All.SendAsync(RealtimeMethods.ReceiveAlert, alert, ct);
            }
        }
    }
}
