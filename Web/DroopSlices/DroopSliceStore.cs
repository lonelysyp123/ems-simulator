using EssSimulator.Configuration;
using EssSimulator.DataExchange;
using EssSimulator.DataExchange.Catalog;
using Microsoft.Extensions.Options;

namespace EssSimulator.Web.DroopSlices
{
    /// <summary>
    /// 白盒切片内存环缓冲。经 <see cref="IControlPointCapture"/> 在控制点写入后采集。
    /// </summary>
    public sealed class DroopSliceStore : IControlPointCapture
    {
        private static DroopSliceStore? _current;
        private readonly object _gate = new();
        private readonly LinkedList<DroopSlice> _items = new();
        private long _sequence;
        private int _maxCount;
        private bool _enabled;
        private readonly bool _allowFeature;

        public DroopSliceStore(IOptions<WebConfig> webCfg, IOptions<EditionConfig> editionCfg)
        {
            var cfg = webCfg.Value;
            var edition = editionCfg.Value;
            edition.ApplyPresets();
            _allowFeature = edition.AllowDroopSlices;
            _enabled = _allowFeature && cfg.DroopSliceCaptureEnabled;
            _maxCount = Math.Clamp(cfg.DroopSliceMaxCount, 10, 5000);
            _current = this;
            ControlPointCapture.Current = this;
        }

        public static DroopSliceStore? Current => _current;

        /// <summary>当前档位是否允许白盒切片功能。</summary>
        public bool FeatureAllowed => _allowFeature;

        public bool Enabled
        {
            get { lock (_gate) return _enabled; }
            set
            {
                lock (_gate)
                {
                    if (!_allowFeature)
                    {
                        _enabled = false;
                        return;
                    }
                    _enabled = value;
                }
            }
        }

        public int MaxCount
        {
            get { lock (_gate) return _maxCount; }
            set { lock (_gate) _maxCount = Math.Clamp(value, 10, 5000); }
        }

        public int Count
        {
            get { lock (_gate) return _items.Count; }
        }

        /// <summary>
        /// 若启用且绑定为 PCS 有功/无功设定，则采集一切片。
        /// </summary>
        public void OnControlApplied(
            string serverName,
            PointBinding binding,
            object appliedValue,
            object? previousValue)
        {
            TryCapture(serverName, binding, appliedValue, previousValue);
        }

        /// <summary>
        /// 若启用且绑定为 PCS 有功/无功设定，则采集一切片；否则返回 false。
        /// </summary>
        public static bool TryCapture(
            string serverName,
            PointBinding binding,
            object appliedValue,
            object? previousValue)
        {
            var store = _current;
            if (store == null || !store.Enabled)
                return false;

            if (!IsPcsPowerSetting(binding.Target.PropertyPath))
                return false;

            var slice = DroopSliceBuilder.Build(serverName, binding, appliedValue, previousValue, store.NextSequence());
            store.Add(slice);
            return true;
        }

        public static bool IsPcsPowerSetting(string? propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath))
                return false;
            return propertyPath.Contains("PCSActivePowerSetting", StringComparison.Ordinal)
                || propertyPath.Contains("PCSReactivePowerSetting", StringComparison.Ordinal);
        }

        private long NextSequence()
        {
            lock (_gate)
                return ++_sequence;
        }

        private void Add(DroopSlice slice)
        {
            lock (_gate)
            {
                _items.AddFirst(slice);
                while (_items.Count > _maxCount)
                    _items.RemoveLast();
            }
        }

        public IReadOnlyList<DroopSliceSummary> List(int limit = 100, int offset = 0)
        {
            limit = Math.Clamp(limit, 1, 1000);
            offset = Math.Max(0, offset);
            lock (_gate)
            {
                return _items
                    .Skip(offset)
                    .Take(limit)
                    .Select(ToSummary)
                    .ToList();
            }
        }

        public DroopSlice? Get(Guid id)
        {
            lock (_gate)
                return _items.FirstOrDefault(s => s.Id == id);
        }

        public void Clear()
        {
            lock (_gate)
                _items.Clear();
        }

        public object GetStatus()
        {
            lock (_gate)
            {
                return new
                {
                    enabled = _enabled,
                    featureAllowed = _allowFeature,
                    count = _items.Count,
                    maxCount = _maxCount,
                    latestSequence = _sequence
                };
            }
        }

        private static DroopSliceSummary ToSummary(DroopSlice s) => new()
        {
            Id = s.Id,
            Sequence = s.Sequence,
            TimestampUtc = s.TimestampUtc,
            ServerName = s.Trigger.ServerName,
            ParamName = s.Trigger.ParamName,
            Kind = s.Trigger.Kind,
            EngineeringValue = s.Trigger.EngineeringValue,
            PreviousEngineeringValue = s.Trigger.PreviousEngineeringValue,
            Unit = s.Trigger.Unit,
            ChannelIndex = s.Pcs.ChannelIndex,
            PccLineVoltageV = s.Grid.PccLineVoltageV,
            GridNominalLineVoltageV = s.Grid.NominalLineVoltageV,
            MeterActivePowerKw = s.Meter.TotalActivePowerKw,
            MeterReactivePowerKvar = s.Meter.TotalReactivePowerKvar,
            PcsActiveSettingKw = s.Pcs.PcsActivePowerSettingKw,
            PcsReactiveSettingKvar = s.Pcs.PcsReactivePowerSettingKvar,
            PcsActiveKw = s.Pcs.ActivePowerKw,
            PcsReactiveKvar = s.Pcs.ReactivePowerKvar
        };
    }
}
