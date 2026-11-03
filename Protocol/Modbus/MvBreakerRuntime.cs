using System.Collections.Concurrent;
using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Solver;
using EssSimulator.EssSimModelApi.Mappers;
using EssSimulator.LocalControl;
using log4net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EssSimulator.Protocol.Modbus
{
    /// <summary>未绑定储能单元的断路器合闸标志，供一次图读取。</summary>
    public static class MvBreakerProtocolState
    {
        private static readonly ConcurrentDictionary<string, bool> ClosedByNode = new(StringComparer.Ordinal);

        public static void Set(string nodeId, bool closed)
        {
            if (!string.IsNullOrEmpty(nodeId))
                ClosedByNode[nodeId] = closed;
        }

        public static Dictionary<string, bool> Copy() => new(ClosedByNode);

        public static void Clear() => ClosedByNode.Clear();
    }

    /// <summary>一台 simMv 上的点读写。在线从站与测试用的内存点表都走这个口。</summary>
    public interface IMvPointPort
    {
        object? Read(string name);
        void Write(string name, object value);
    }

    public sealed class MvModbusPointPort : IMvPointPort
    {
        private readonly ModbusSimServer _server;

        public MvModbusPointPort(ModbusSimServer server) => _server = server;

        public object? Read(string name) => _server.ReadLivePoint(name);

        public void Write(string name, object value) => _server.SetDataStoreByMesurePointName(name, value);
    }

    /// <summary>
    /// 每台 simMv 的遥测与 AA/EE。主断抄并网点，单元断抄该单元电表，
    /// 其余只抄相邻母线线电压。合闸编码只写在对应从站上。
    /// </summary>
    public sealed class MvBreakerRuntime
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(MvBreakerRuntime));
        private readonly Dictionary<string, double> _commandShadow = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _otherClosed = new(StringComparer.Ordinal);

        internal static LcMvMeterSnap FromBusLineVoltage(double lineVoltageV, double frequencyHz)
        {
            double voltage = Math.Max(0, lineVoltageV);
            return new LcMvMeterSnap(
                voltage, voltage, voltage,
                PhaseACurrent: 0, PhaseBCurrent: 0, PhaseCCurrent: 0, NeutralCurrent: 0,
                TotalActivePower: 0, TotalReactivePower: 0, TotalApparentPower: 0,
                PowerFactor: 0, Frequency: frequencyHz,
                ForwardActiveEnergySecondary: 0, ReverseActiveEnergySecondary: 0,
                ForwardReactiveEnergySecondary: 0, ReverseReactiveEnergySecondary: 0);
        }

        public void RunCycle(IReadOnlyList<MvBreakerEndpoint> breakers, Func<string, IMvPointPort?> resolve)
        {
            if (breakers == null || breakers.Count == 0)
                return;

            var ess = SimulatorHost.Instance.TryGetEss();
            for (int i = 0; i < breakers.Count; i++)
            {
                var server = resolve($"simMv{i + 1}");
                if (server == null)
                    continue;
                ApplyCommand(breakers[i], server, ess);
                WriteTelemetry(breakers[i], server, ess);
            }
        }

        private void WriteTelemetry(MvBreakerEndpoint ep, IMvPointPort server, EnergyStorageSystem? ess)
        {
            foreach (var (param, value) in LcMvTelemetry.Collect(ResolveSnap(ep, ess)))
                server.Write(param, value);
        }

        private LcMvMeterSnap ResolveSnap(MvBreakerEndpoint ep, EnergyStorageSystem? ess)
        {
            if (ep.Role == MvBreakerRole.Main && ess != null)
            {
                return LcMvTelemetry.FromPcc(
                    ess.ElectricalNetwork.PccMeter,
                    ess.ElectricalNetwork.SystemFrequencyHz);
            }

            if (ep.Role == MvBreakerRole.Unit && ep.UnitId >= 1)
            {
                var emu = SimulatorHost.Instance.TryGetEmu(ep.UnitId);
                if (emu != null)
                    return LcMvTelemetry.FromUnitMeter(emu.ElectricityMeter);
            }

            if (ep.Role == MvBreakerRole.Other && ess != null && !string.IsNullOrWhiteSpace(ep.SourceBusId))
            {
                var sample = MeterBusSampler.Sample(
                    ess.ElectricalNetwork,
                    ess.RadialGraph,
                    ep.SourceBusId,
                    ess.ElectricalNetwork.SystemFrequencyHz);
                return FromBusLineVoltage(sample.LineVoltageV, sample.FrequencyHz);
            }

            return FromBusLineVoltage(0, 0);
        }

        private void ApplyCommand(MvBreakerEndpoint ep, IMvPointPort server, EnergyStorageSystem? ess)
        {
            string key = ep.NodeId;
            double current = ReadCommand(server);
            if (!_commandShadow.TryGetValue(key, out double previous))
            {
                if (!LcMvControl.TryInterpret(current, out _, out _))
                {
                    double primed = LcMvControl.Encode(ReadClosed(ep, ess));
                    server.Write(LcMvMap.HvBreakerCommand, primed);
                    _commandShadow[key] = primed;
                    return;
                }

                previous = LcMvControl.Encode(ReadClosed(ep, ess));
                _commandShadow[key] = previous;
            }

            if (Math.Abs(current - previous) < 0.5)
                return;

            if (!LcMvControl.TryInterpret(current, out double normalized, out bool closed))
            {
                Log.Warn($"中压断路器 {ep.NodeId} 角色 {ep.Role} 读到非法命令 {current}，回退 {previous}");
                server.Write(LcMvMap.HvBreakerCommand, previous);
                return;
            }

            if (!TryDrive(ep, closed, out var driveMessage))
            {
                Log.Warn($"中压断路器 {ep.NodeId} 角色 {ep.Role} 命令 {current} 未执行：{driveMessage}，回退 {previous}");
                server.Write(LcMvMap.HvBreakerCommand, previous);
                return;
            }

            if (ep.Role == MvBreakerRole.Other)
            {
                _otherClosed[ep.NodeId] = closed;
                MvBreakerProtocolState.Set(ep.NodeId, closed);
                UiSnapshotNotifier.RequestImmediatePush();
            }
            server.Write(LcMvMap.HvBreakerCommand, normalized);
            _commandShadow[key] = normalized;
        }

        private bool ReadClosed(MvBreakerEndpoint ep, EnergyStorageSystem? ess)
        {
            if (ep.Role == MvBreakerRole.Main && ess != null)
                return ess.IsMainBreakerClosed;
            if (ep.Role == MvBreakerRole.Unit && ess != null && ep.UnitId >= 1)
                return ess.IsUnitBreakerClosed(ep.UnitId - 1);
            return _otherClosed.TryGetValue(ep.NodeId, out bool closed) ? closed : true;
        }

        private static bool TryDrive(MvBreakerEndpoint ep, bool closed, out string message)
        {
            if (ep.Role == MvBreakerRole.Main)
                return DeviceControlFacade.TrySetMainBreaker(closed, out message);
            if (ep.Role == MvBreakerRole.Unit)
                return DeviceControlFacade.TrySetUnitBreaker(ep.UnitId, closed, out message);

            message = "";
            return true;
        }

        private static double ReadCommand(IMvPointPort server)
        {
            var raw = server.Read(LcMvMap.HvBreakerCommand);
            return raw == null ? 0 : ModbusValueConverter.ToDouble(raw);
        }
    }

    /// <summary>仿真刷新时把每台三相断路器的遥测和遥控同步到对应 simMv。</summary>
    public sealed class MvBreakerHostedService : BackgroundService
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(MvBreakerHostedService));
        private readonly SimulatorConfig _cfg;
        private readonly MvBreakerRuntime _runtime = new();

        public MvBreakerHostedService(IOptions<SimulatorConfig> options)
        {
            _cfg = options.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_cfg.MvBreakers == null || _cfg.MvBreakers.Count == 0)
                return;

            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    _runtime.RunCycle(_cfg.MvBreakers, name =>
                    {
                        var server = SimulatorHost.Instance.Get<ModbusSimServer>(name);
                        return server == null ? null : new MvModbusPointPort(server);
                    });
                }
                catch (Exception ex)
                {
                    Log.Debug("中压断路器桥接周期异常", ex);
                }
            }
        }
    }
}
