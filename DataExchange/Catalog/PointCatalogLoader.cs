using EssSimulator.Configuration;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Config;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.DataExchange.Catalog
{
    /// <summary>
    /// 从 Modbus 点表编译数据交互层目录（Phase A：复用 ModbusPointMap 解析结果）。
    /// </summary>
    public static class PointCatalogLoader
    {
        private static readonly Dictionary<string, ControlSemantics> EmuDefaultSemantics = new(StringComparer.OrdinalIgnoreCase)
        {
            ["pcs1_startstop"] = ControlSemantics.Hold,
            ["pcs2_startstop"] = ControlSemantics.Hold,
            ["pcs1_blackstart_enable"] = ControlSemantics.Edge,
            ["pcs2_blackstart_enable"] = ControlSemantics.Edge,
            ["highvoltagebreakeronoff"] = ControlSemantics.Hold
        };

        private static readonly Dictionary<string, ControlSemantics> BmsDefaultSemantics = new(StringComparer.OrdinalIgnoreCase)
        {
            ["param11"] = ControlSemantics.Pulse,
            ["param12"] = ControlSemantics.Hold,
            ["param131"] = ControlSemantics.Pulse,
            ["param170"] = ControlSemantics.Pulse,
            ["yc133"] = ControlSemantics.Pulse,
            ["yt0"] = ControlSemantics.Pulse
        };

        private static readonly Dictionary<string, ControlEffectId> BmsDefaultEffects = new(StringComparer.OrdinalIgnoreCase)
        {
            ["param11"] = ControlEffectId.BmsApplyLinkCommands,
            ["param12"] = ControlEffectId.BmsApplyLinkCommands,
            ["yc133"] = ControlEffectId.BmsApplyLinkCommands,
            ["yt0"] = ControlEffectId.BmsApplyLinkCommands
        };

        private static readonly Dictionary<string, ControlEffectId> EmuDefaultEffects = new(StringComparer.OrdinalIgnoreCase)
        {
            ["pcs1_startstop"] = ControlEffectId.PcsApplyCommands,
            ["pcs2_startstop"] = ControlEffectId.PcsApplyCommands,
            ["yx3"] = ControlEffectId.PcsApplyCommands,
            ["yk3"] = ControlEffectId.PcsApplyCommands,
            ["yx5"] = ControlEffectId.PcsApplyCommands,
            ["pcs1_blackstart_enable"] = ControlEffectId.PcsApplyCommands,
            ["pcs2_blackstart_enable"] = ControlEffectId.PcsApplyCommands,
            ["yx2"] = ControlEffectId.PcsApplyCommands,
            ["yk2"] = ControlEffectId.PcsApplyCommands,
            ["yx4"] = ControlEffectId.PcsApplyCommands,
            ["param55"] = ControlEffectId.PcsApplyCommands,
            ["param56"] = ControlEffectId.PcsApplyCommands,
            ["param59"] = ControlEffectId.PcsApplyCommands,
            ["param60"] = ControlEffectId.PcsApplyCommands,
            ["param64"] = ControlEffectId.PcsApplyCommands,
            ["param65"] = ControlEffectId.PcsApplyCommands,
            ["yt3"] = ControlEffectId.PcsApplyCommands,
            ["yt4"] = ControlEffectId.PcsApplyCommands,
            ["yt7"] = ControlEffectId.PcsApplyCommands,
            ["highvoltagebreakeronoff"] = ControlEffectId.UnitHighVoltageBreaker
        };

        public static PointCatalog FromPointMap(
            ModbusPointMap pointMap,
            string serverName,
            DataExchangeOptions? options = null,
            IReadOnlyList<EssUnitConfig>? essUnits = null)
        {
            options ??= new DataExchangeOptions();
            // simLc 系统级点表与 simEmu 同构：控制点绑定 emu{n}.Emu.*，复用 EMU 默认效果解析
            bool isEmu = DataExchangeSession.IsEmuLikeDevice(serverName);
            bool isBms = serverName.StartsWith("simBms", StringComparison.OrdinalIgnoreCase);
            // EMU 点表绑定门控：只能指向机组内实际存在的设备（PCS/电表/断路器），缺失即按未绑定处理
            var emuFilter = EmuDeviceCatalogFilter.Create(serverName, essUnits);

            var telemetry = new List<PointBinding>();
            var pluginPoints = new List<PluginPointBinding>();
            var sumPoints = new List<SumPointBinding>();
            var maxPoints = new List<MaxPointBinding>();
            foreach (var entry in pointMap.DataMaps)
            {
                if (string.IsNullOrWhiteSpace(entry.ParamName))
                    continue;
                if (!pointMap.ParamModelLookup.TryGetValue(entry.ParamName, out var model))
                    continue;
                if (string.IsNullOrWhiteSpace(model.Arg1))
                    continue;

                // model=sum：遥测值 = 各路径之和（arg1 支持分号分隔多路径，arg2 为经典第二路径），不走单路径反射绑定
                if (string.Equals(model.ModelType, SumPointBinding.ModelType, StringComparison.OrdinalIgnoreCase))
                {
                    var paths = SumPointBinding.ParsePaths(model.Arg1, model.Arg2);
                    if (paths == null)
                        continue;

                    if (emuFilter != null)
                    {
                        paths = emuFilter.FilterPaths(paths);
                        if (paths == null)
                            continue;
                    }

                    sumPoints.Add(new SumPointBinding
                    {
                        Entry = entry,
                        ParamName = entry.ParamName,
                        Paths = paths
                    });
                    continue;
                }

                // model=max：遥测值 = 各路径最大值（路径格式同 sum），用于单元级取最严重模块口径，不走单路径反射绑定
                if (string.Equals(model.ModelType, MaxPointBinding.ModelType, StringComparison.OrdinalIgnoreCase))
                {
                    var paths = SumPointBinding.ParsePaths(model.Arg1, model.Arg2);
                    if (paths == null)
                        continue;

                    if (emuFilter != null)
                    {
                        paths = emuFilter.FilterPaths(paths);
                        if (paths == null)
                            continue;
                    }

                    maxPoints.Add(new MaxPointBinding
                    {
                        Entry = entry,
                        ParamName = entry.ParamName,
                        Paths = paths
                    });
                    continue;
                }

                // model=plugin|arg1=<字键>|arg2=<设备根路径>：交给遥测插件组字，不走反射绑定
                if (string.Equals(model.ModelType, "plugin", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(model.Arg2))
                        continue;

                    pluginPoints.Add(new PluginPointBinding
                    {
                        Entry = entry,
                        ParamName = entry.ParamName,
                        WordKey = model.Arg1,
                        DeviceRoot = model.Arg2
                    });
                    continue;
                }

                var target = DataTarget.ParseBindingPath(model.Arg1);
                if (target == null)
                    continue;

                if (emuFilter != null && !emuFilter.Allows(target.FullPath))
                    continue;

                telemetry.Add(new PointBinding
                {
                    Entry = entry,
                    ParamName = entry.ParamName,
                    Target = target
                });
            }

            var control = new List<PointBinding>();
            foreach (var entry in pointMap.ControlMaps)
            {
                if (string.IsNullOrWhiteSpace(entry.ParamName))
                    continue;
                if (!pointMap.ParamModelLookup.TryGetValue(entry.ParamName, out var model))
                    continue;
                if (string.IsNullOrWhiteSpace(model.Arg1))
                    continue;

                // sum/max 点位仅供遥测，不作为控制目标
                if (string.Equals(model.ModelType, SumPointBinding.ModelType, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(model.ModelType, MaxPointBinding.ModelType, StringComparison.OrdinalIgnoreCase))
                    continue;

                var target = DataTarget.ParseBindingPath(model.Arg1);
                if (target == null)
                    continue;

                if (emuFilter != null && !emuFilter.Allows(target.FullPath))
                    continue;

                control.Add(new PointBinding
                {
                    Entry = entry,
                    ParamName = entry.ParamName,
                    Target = target,
                    Semantics = ResolveSemantics(entry.ParamName, target, isEmu, isBms, options),
                    Effect = ResolveEffect(entry.ParamName, target, isEmu, isBms, options)
                });
            }

            var rackTelemetry = new List<RackPointBinding>();
            var rackControl = new List<RackPointBinding>();
            if (isBms)
            {
                foreach (var entry in pointMap.RackDataMaps)
                {
                    if (string.IsNullOrWhiteSpace(entry.ParamName))
                        continue;
                    if (!pointMap.RackParamModelLookup.TryGetValue(entry.ParamName, out var model))
                        continue;
                    if (string.IsNullOrWhiteSpace(model.Arg1))
                        continue;

                    // sum/max 仅支持 bank 级点位，rack 级跳过
                    if (string.Equals(model.ModelType, SumPointBinding.ModelType, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(model.ModelType, MaxPointBinding.ModelType, StringComparison.OrdinalIgnoreCase))
                        continue;

                    rackTelemetry.Add(new RackPointBinding
                    {
                        Entry = entry,
                        ParamName = entry.ParamName,
                        BindingPathTemplate = model.Arg1
                    });
                }

                foreach (var entry in pointMap.RackControlMaps)
                {
                    if (string.IsNullOrWhiteSpace(entry.ParamName))
                        continue;
                    if (!pointMap.RackParamModelLookup.TryGetValue(entry.ParamName, out var model))
                        continue;
                    if (string.IsNullOrWhiteSpace(model.Arg1))
                        continue;

                    if (string.Equals(model.ModelType, SumPointBinding.ModelType, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(model.ModelType, MaxPointBinding.ModelType, StringComparison.OrdinalIgnoreCase))
                        continue;

                    rackControl.Add(new RackPointBinding
                    {
                        Entry = entry,
                        ParamName = entry.ParamName,
                        BindingPathTemplate = model.Arg1
                    });
                }
            }

            return new PointCatalog
            {
                ServerName = serverName,
                TelemetryPoints = telemetry,
                ControlPoints = control,
                RackTelemetryPoints = rackTelemetry,
                RackControlPoints = rackControl,
                PluginPoints = pluginPoints,
                SumPoints = sumPoints,
                MaxPoints = maxPoints,
                DefaultValues = new Dictionary<string, object>(pointMap.DefaultBuffer)
            };
        }

        private static ControlSemantics ResolveSemantics(
            string paramName,
            DataTarget target,
            bool isEmu,
            bool isBms,
            DataExchangeOptions options)
        {
            if (options.ControlSemantics.TryGetValue(paramName, out var configured))
                return DataExchangeOptionParser.ParseSemantics(configured, ControlSemantics.Hold);

            if (isEmu && EmuDefaultSemantics.TryGetValue(paramName, out var emuDefault))
                return emuDefault;

            if (isBms && BmsDefaultSemantics.TryGetValue(paramName, out var bmsDefault))
                return bmsDefault;

            if (isBms && (target.PropertyPath.Contains("GridConnectCommand", StringComparison.Ordinal) ||
                          target.PropertyPath.Contains("FaultClearCommand", StringComparison.Ordinal)))
                return ControlSemantics.Pulse;

            return ControlSemantics.Hold;
        }

        private static ControlEffectId ResolveEffect(
            string paramName,
            DataTarget target,
            bool isEmu,
            bool isBms,
            DataExchangeOptions options)
        {
            if (options.ControlEffects.TryGetValue(paramName, out var configured))
                return DataExchangeOptionParser.ParseEffect(configured, ControlEffectId.None);

            if (isEmu && EmuDefaultEffects.TryGetValue(paramName, out var emuDefault))
                return emuDefault;

            if (isBms && BmsDefaultEffects.TryGetValue(paramName, out var bmsDefault))
                return bmsDefault;

            if (isBms && target.PropertyPath.Contains("GridConnectCommand", StringComparison.Ordinal))
                return ControlEffectId.BmsApplyLinkCommands;

            if (isBms && target.PropertyPath.Contains("BlackStartCommand", StringComparison.Ordinal))
                return ControlEffectId.BmsApplyLinkCommands;

            if (isBms && target.PropertyPath.Contains("FaultClearCommand", StringComparison.Ordinal))
                return ControlEffectId.BmsApplyLinkCommands;

            if (isEmu)
            {
                if (target.PropertyPath.Contains("pcsOnOffSwitch", StringComparison.Ordinal) ||
                    target.PropertyPath.Contains("BlackStartEnabled", StringComparison.Ordinal) ||
                    target.PropertyPath.Contains("PCSActivePowerSetting", StringComparison.Ordinal) ||
                    target.PropertyPath.Contains("PCSReactivePowerSetting", StringComparison.Ordinal) ||
                    target.PropertyPath.Contains("IslandVoltageSetting", StringComparison.Ordinal) ||
                    target.PropertyPath.Contains("IslandFrequencySetting", StringComparison.Ordinal))
                    return ControlEffectId.PcsApplyCommands;

                // EMU 级（虚拟模型）系统控制：目标 P/Q 均分、系统操作、黑启动写入、远程使能/模式
                if (target.PropertyPath.Contains("Emu.TargetActivePower", StringComparison.Ordinal) ||
                    target.PropertyPath.Contains("Emu.TargetReactivePower", StringComparison.Ordinal) ||
                    target.PropertyPath.Contains("Emu.SystemOperation", StringComparison.Ordinal) ||
                    target.PropertyPath.Contains("Emu.BlackStartModeWrite", StringComparison.Ordinal) ||
                    target.PropertyPath.Contains("Emu.RemoteControlEnable", StringComparison.Ordinal) ||
                    target.PropertyPath.Contains("Emu.RemoteControlMode", StringComparison.Ordinal))
                    return ControlEffectId.PcsApplyCommands;

                if (target.PropertyPath.Equals("Breaker.Closed", StringComparison.OrdinalIgnoreCase) ||
                    target.PropertyPath.EndsWith(".PowerOnOff", StringComparison.Ordinal))
                    return ControlEffectId.UnitHighVoltageBreaker;
            }

            return ControlEffectId.None;
        }
    }
}
