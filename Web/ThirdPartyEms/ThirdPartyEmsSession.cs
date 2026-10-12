using EssSimulator.Core;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;
using EssSimulator.EssSimModelApi.Mappers;

namespace EssSimulator.Web.ThirdPartyEms
{
    /// <summary>
    /// 第三方 EMS 会话：直写 emuN.Emu 数据模型；占用期间通过
    /// <see cref="ExternalControlGate"/> 拒绝 Modbus/dpc 等外部指令。
    /// </summary>
    public sealed class ThirdPartyEmsSession : IDisposable
    {
        public const string NoUnitReason = "仿真未就绪，找不到储能单元数据模型（emu）。";
        public const int OpStart = 3;
        public const int OpStop = 4;
        public const int OpStandby = 5;
        public const int OpReset = 6;

        private readonly Func<IReadOnlyList<ThirdPartyEmsTarget>> _targets;
        private readonly object _gate = new();
        private readonly Dictionary<string, UnitSession> _units = new(StringComparer.OrdinalIgnoreCase);

        public ThirdPartyEmsSession() : this(FromModel) { }

        public ThirdPartyEmsSession(Func<IReadOnlyList<ThirdPartyEmsTarget>> targets)
        {
            _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        }

        public ThirdPartyEmsDashboard GetDashboard()
        {
            lock (_gate)
            {
                var targets = _targets() ?? Array.Empty<ThirdPartyEmsTarget>();
                SyncUnits(targets);

                var dash = new ThirdPartyEmsDashboard
                {
                    Available = targets.Count > 0,
                    Exclusive = ExternalControlGate.Owner == ExternalControlOwner.ThirdPartyEms,
                    GateOwner = ExternalControlGate.Owner.ToString(),
                    Reason = DashboardReason(targets.Count)
                };

                double sumP = 0, sumQ = 0;
                int live = 0, connected = 0;
                foreach (var unit in _units.Values.OrderBy(u => u.Target.UnitIndex))
                {
                    var snap = ReadUnitLocked(unit);
                    dash.Units.Add(snap);
                    if (snap.Connected)
                        connected++;
                    if (snap.Live)
                    {
                        live++;
                        sumP += snap.ActivePowerKw ?? 0;
                        sumQ += snap.ReactivePowerKvar ?? 0;
                    }
                }

                dash.Station.UnitCount = dash.Units.Count;
                dash.Station.ConnectedCount = connected;
                if (live > 0)
                {
                    dash.Station.ActivePowerKw = sumP;
                    dash.Station.ReactivePowerKvar = sumQ;
                }

                return dash;
            }
        }

        private static string? DashboardReason(int targetCount)
        {
            if (targetCount == 0)
                return NoUnitReason;
            if (ExternalControlGate.Owner == ExternalControlOwner.EmsStrategy)
                return ExternalControlGate.StrategyBlockedMessage;
            return null;
        }

        public bool TryConnect(string? name, out string error)
        {
            lock (_gate)
            {
                var targets = _targets() ?? Array.Empty<ThirdPartyEmsTarget>();
                if (ExternalControlGate.Owner == ExternalControlOwner.EmsStrategy)
                {
                    error = ExternalControlGate.StrategyBlockedMessage;
                    return false;
                }

                SyncUnits(targets);
                if (_units.Count == 0)
                {
                    error = NoUnitReason;
                    return false;
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    foreach (var unit in _units.Values)
                        ConnectUnitLocked(unit);
                    RefreshExclusiveLocked();
                    error = "";
                    return true;
                }

                if (!TryGetUnitLocked(name, out var selected, out error))
                    return false;
                ConnectUnitLocked(selected);
                RefreshExclusiveLocked();
                error = "";
                return true;
            }
        }

        public bool TryDisconnect(string? name, out string error)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    foreach (var unit in _units.Values)
                        unit.Connected = false;
                    RefreshExclusiveLocked();
                    error = "";
                    return true;
                }

                if (!TryGetUnitLocked(name, out var selected, out error))
                    return false;
                selected.Connected = false;
                RefreshExclusiveLocked();
                error = "";
                return true;
            }
        }

        public bool TrySetPower(string? name, double activeKw, double reactiveKvar, out string error)
        {
            return TryMutate(name, "目标有功/无功", out error, emu =>
            {
                emu.Emu.RemoteControlEnable = 1;
                emu.Emu.RemoteControlMode = 1;
                emu.Emu.TargetActivePower = (float)activeKw;
                emu.Emu.TargetReactivePower = (float)reactiveKvar;
            }, $"P={activeKw:0.#} kW Q={reactiveKvar:0.#} kvar");
        }

        public bool TrySetRemote(string? name, int enable, int mode, out string error)
        {
            return TryMutate(name, "远程使能/模式", out error, emu =>
            {
                emu.Emu.RemoteControlEnable = enable;
                emu.Emu.RemoteControlMode = mode;
            }, $"enable={enable} mode={mode}");
        }

        public bool TrySetOperation(string? name, int operation, out string error)
        {
            if (operation is not (OpStart or OpStop or OpStandby or OpReset))
            {
                error = "系统操作仅支持 3 启动 / 4 停止 / 5 待机 / 6 重置";
                return false;
            }

            return TryMutate(name, "系统操作", out error,
                emu => emu.Emu.SystemOperation = operation,
                $"syst6={operation}");
        }

        public void Dispose()
        {
            lock (_gate)
            {
                foreach (var unit in _units.Values)
                    unit.Connected = false;
                _units.Clear();
                ExternalControlGate.Release(ExternalControlOwner.ThirdPartyEms);
            }
        }

        internal static IReadOnlyList<ThirdPartyEmsTarget> FromModel()
        {
            var list = new List<ThirdPartyEmsTarget>();
            var ess = SimulatorHost.Instance.TryGetEss();
            int n = ess != null && ess.PcsPerUnit.Count > 0 ? ess.PcsPerUnit.Count : 0;
            if (n == 0)
            {
                for (int i = 1; i <= 32; i++)
                {
                    if (SimulatorHost.Instance.TryGetEmu(i) == null)
                        break;
                    n = i;
                }
            }

            for (int i = 1; i <= n; i++)
            {
                if (SimulatorHost.Instance.TryGetEmu(i) == null)
                    continue;
                list.Add(new ThirdPartyEmsTarget { Name = $"emu{i}", UnitIndex = i });
            }

            return list;
        }

        private bool TryMutate(
            string? name,
            string action,
            out string error,
            Action<EnergyManagementData> mutate,
            string detail)
        {
            lock (_gate)
            {
                var targets = _targets() ?? Array.Empty<ThirdPartyEmsTarget>();
                SyncUnits(targets);
                if (_units.Count == 0)
                {
                    error = NoUnitReason;
                    return false;
                }

                if (!TryGetUnitLocked(name, out var unit, out error))
                    return false;
                if (!unit.Connected)
                {
                    error = "请先占用该单元（连接）后再下发";
                    return false;
                }

                var emu = SimulatorHost.Instance.TryGetEmu(unit.Target.UnitIndex);
                if (emu == null)
                {
                    error = $"找不到 {unit.Target.Name} 数据模型";
                    unit.LastError = error;
                    return false;
                }

                mutate(emu);
                if (!EmuCommandPipeline.TryApplyUnit(unit.Target.UnitIndex))
                {
                    error = "找不到 ess 模型，请确认仿真已启动";
                    unit.LastWriteOk = false;
                    unit.LastWriteUtc = DateTime.UtcNow;
                    unit.LastWrite = $"{action}失败：{error}";
                    unit.LastError = error;
                    return false;
                }

                error = "";
                unit.LastError = null;
                unit.LastWriteOk = true;
                unit.LastWriteUtc = DateTime.UtcNow;
                unit.LastWrite = $"{action} {detail}";
                return true;
            }
        }

        private void SyncUnits(IReadOnlyList<ThirdPartyEmsTarget> targets)
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in targets)
            {
                if (string.IsNullOrWhiteSpace(target.Name))
                    continue;
                keep.Add(target.Name);
                if (!_units.ContainsKey(target.Name))
                    _units[target.Name] = new UnitSession { Target = target };
                else
                    _units[target.Name].Target = target;
            }

            foreach (var name in _units.Keys.Where(k => !keep.Contains(k)).ToList())
                _units.Remove(name);

            RefreshExclusiveLocked();
        }

        private void ConnectUnitLocked(UnitSession unit)
        {
            unit.Connected = true;
            unit.LastError = null;
        }

        private void RefreshExclusiveLocked()
        {
            if (_units.Values.Any(u => u.Connected))
                ExternalControlGate.TryOccupy(ExternalControlOwner.ThirdPartyEms, out _);
            else
                ExternalControlGate.Release(ExternalControlOwner.ThirdPartyEms);
        }

        private bool TryGetUnitLocked(string? name, out UnitSession unit, out string error)
        {
            unit = null!;
            if (string.IsNullOrWhiteSpace(name))
            {
                if (_units.Count == 1)
                {
                    unit = _units.Values.First();
                    error = "";
                    return true;
                }

                error = "请指定储能单元（emu 名称）";
                return false;
            }

            if (_units.TryGetValue(name.Trim(), out unit!))
            {
                error = "";
                return true;
            }

            error = $"未知储能单元：{name}";
            return false;
        }

        private static ThirdPartyEmsUnitSnapshot ReadUnitLocked(UnitSession unit)
        {
            var t = unit.Target;
            var snap = new ThirdPartyEmsUnitSnapshot
            {
                Name = t.Name,
                UnitIndex = t.UnitIndex,
                ModelPath = $"emu{t.UnitIndex}.Emu",
                Connected = unit.Connected,
                LastError = unit.LastError,
                LastWrite = unit.LastWrite,
                LastWriteOk = unit.LastWriteOk,
                LastWriteUtc = unit.LastWriteUtc
            };

            var emu = SimulatorHost.Instance.TryGetEmu(t.UnitIndex);
            if (emu == null)
            {
                snap.Live = false;
                snap.LastError = unit.LastError ?? $"找不到 {t.Name}";
                return snap;
            }

            var e = emu.Emu;
            snap.Live = true;
            snap.RemoteEnable = e.RemoteControlEnable;
            snap.RemoteMode = e.RemoteControlMode;
            snap.TargetActivePowerKw = e.TargetActivePower;
            snap.TargetReactivePowerKvar = e.TargetReactivePower;
            snap.ActivePowerKw = e.OutputActivePower;
            snap.ReactivePowerKvar = e.OutputReactivePower;
            snap.Soc = e.AverageBatterySoc;
            snap.DetailedStatus = e.OperationStatus;
            snap.FaultSummary = e.AnyPcsFaulted || e.FaultPcsCount > 0 ? 1 : 0;
            snap.MaxChargePowerKw = e.MaxChargePower;
            snap.MaxDischargePowerKw = e.MaxDischargePower;
            return snap;
        }

        private sealed class UnitSession
        {
            public required ThirdPartyEmsTarget Target { get; set; }
            public bool Connected { get; set; }
            public string? LastError { get; set; }
            public string? LastWrite { get; set; }
            public bool? LastWriteOk { get; set; }
            public DateTime? LastWriteUtc { get; set; }
        }
    }
}
