using EssSimulator.EssDeviceSimModel;
using log4net;

namespace EssSimulator.Display
{
    /// <summary>主接线等视图的状态文本格式化（Web/TUI 共用）。</summary>
    public static class GuiStatusFormatters
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(GuiStatusFormatters));

        public static string FormatVoltage(double lineVoltageV) =>
            lineVoltageV >= 1000 ? $"{lineVoltageV / 1000:0.0} kV" : $"{lineVoltageV:0.0} V";

        public static string FormatAcPhasor(AcPhasorSnapshot phasor) =>
            $"{FormatVoltage(phasor.LineVoltageV)} / {phasor.LineCurrentA:0.0} A / φ{phasor.PhaseAngleDeg:0.0}° / {phasor.FrequencyHz:0.0} Hz";

        /// <summary>变压器等无独立频率源的交流相量（仅 V/I/φ；工频由系统统一频率源决定，变压器不改变频率）。</summary>
        public static string FormatAcPhasorViPhi(AcPhasorSnapshot phasor) =>
            $"{FormatVoltage(phasor.LineVoltageV)} / {phasor.LineCurrentA:0.0} A / φ{phasor.PhaseAngleDeg:0.0}°";

        public static string FormatAcPhasorWithPower(AcPhasorSnapshot phasor) =>
            $"{FormatAcPhasor(phasor)}  → P {phasor.ActivePowerKw:0.0} kW  Q {phasor.ReactivePowerKvar:0.0} kvar  PF {phasor.PowerFactor:0.000}";

        public static string FormatBusNode(BusNodeSnapshot? bus)
        {
            if (bus == null)
                return "—";
            var p = new AcPhasorSnapshot(bus.Value.LineVoltageV, bus.Value.LineCurrentA, bus.Value.PhaseAngleDeg, bus.Value.FrequencyHz);
            return $"{bus.Value.BusId}: {FormatAcPhasorWithPower(p)}";
        }

        public static string FormatBreakerState(bool closed, bool tripped) =>
            tripped ? "跳闸" : closed ? "合" : "分";

        public static string FormatGridModeLabel(string? gMode) => gMode switch
        {
            "GridConnected" => "并网",
            "Islanded" => "离网",
            _ => string.IsNullOrWhiteSpace(gMode) ? "?" : gMode
        };

        public static string FormatBlackStartPhaseLabel(string? phase) => phase switch
        {
            "Inactive" => "未激活",
            "Preparing" => "准备",
            "SoftStarting" => "软启动",
            "VoltageRegulating" => "调压",
            "Synchronized" => "已同步",
            "Following" => "预同步",
            _ => string.IsNullOrWhiteSpace(phase) ? "—" : phase
        };

        public static string FormatPcsAcLine(PcsChannelSnapshot pcs) =>
            $"AC {FormatAcPhasor(pcs.AcOutput)} | {FormatGridModeLabel(pcs.GridMode)}" +
            (pcs.BlackStartEnabled ? $" | 黑启动:{FormatBlackStartPhaseLabel(pcs.BlackStartPhase)}" : "") +
            $" | P/Q {pcs.ActivePowerKw:0.0}/{pcs.ReactivePowerKw:0.0}";

        public static string FormatBlackStartSwitchStatus(int unitIndex0, int pcsSlotInUnit0, int essPcsListIndex)
        {
            bool swOn = GuiSimDataAccess.SafeGetBool($"emu{unitIndex0 + 1}.PcsList[{pcsSlotInUnit0}].BlackStartEnabled");
            if (!swOn)
                return "关";

            bool simOn = GuiSimDataAccess.SafeGetBool($"ess._pcsList[{essPcsListIndex}]._currentState.BlackStartEnabled");
            if (!simOn)
                return "开(未生效)";

            try
            {
                var m = SimServer.GetExtIfVariableVal($"ess._pcsList[{essPcsListIndex}]._currentState.Mode");
                var g = SimServer.GetExtIfVariableVal($"ess._pcsList[{essPcsListIndex}]._currentState.GMode");
                if (m != null && g != null &&
                    Enum.TryParse<OperationMode>(m.ToString(), out var mode) &&
                    Enum.TryParse<GridMode>(g.ToString(), out var gMode) &&
                    mode == OperationMode.Normal &&
                    gMode == GridMode.Islanded)
                    return "开(运行中)";
            }
            catch (Exception ex)
            {
                Log.Debug($"FormatBlackStartSwitchStatus 读取 PCS{essPcsListIndex + 1} 模式失败", ex);
            }

            return "开";
        }

        public static string BuildBlackStartSwitchSummary(int channelStart, int channelEndExclusive)
        {
            var layout = GuiSimDataAccess.GetPcsPerUnit();
            var parts = new List<string>();
            for (int i = channelStart; i < channelEndExclusive; i++)
            {
                int u = PcsUnitLayout.UnitIndexOf(layout, i);
                int slot = PcsUnitLayout.SlotOfChannel(layout, i);
                string st = FormatBlackStartSwitchStatus(u, slot, i);
                parts.Add($"PCS{i + 1}:{st}");
            }
            return string.Join("  ", parts);
        }

        public static string FormatPcsDeviceStateLabel(int unitIndex0, int pcsSlotInUnit0, int essPcsListIndex)
        {
            try
            {
                var m = SimServer.GetExtIfVariableVal($"ess._pcsList[{essPcsListIndex}]._currentState.Mode");
                double pAct = GuiSimDataAccess.SafeGetDouble($"ess._pcsList[{essPcsListIndex}]._currentState.ActivePower");
                bool blackStart = GuiSimDataAccess.SafeGetBool($"ess._pcsList[{essPcsListIndex}]._currentState.BlackStartEnabled");
                ushort fault = (ushort)GuiSimDataAccess.SafeGetDouble($"ess._pcsList[{essPcsListIndex}]._currentState.FaultType");
                if (m != null && Enum.TryParse<OperationMode>(m.ToString(), out var mode))
                    return PcsDisplayLabels.GetRunPhaseLabel(mode, pAct, blackStart, fault);
            }
            catch (Exception ex)
            {
                Log.Debug($"FormatPcsDeviceStateLabel 读取 PCS{essPcsListIndex + 1} 失败", ex);
            }

            return "?";
        }

        public static string FormatPcsDeviceStateLine(int unitIndex0, int pcsSlotInUnit0, int essPcsListIndex)
        {
            string phase = FormatPcsDeviceStateLabel(unitIndex0, pcsSlotInUnit0, essPcsListIndex);
            return $"设备状态:{phase}";
        }

        public static string FormatPcsStartStopPointLine(int unitIndex0, int pcsSlotInUnit0)
        {
            bool coilOn = GuiSimDataAccess.GetEmuPcsStartStopCoil(unitIndex0, pcsSlotInUnit0);
            return $"启停控制:{(coilOn ? "1" : "0")}";
        }

        public static string FormatPcsTargetPowerLine(int unitIndex0, int pcsSlotInUnit0)
        {
            double pSet = GuiSimDataAccess.SafeGetDouble(
                $"emu{unitIndex0 + 1}.PcsList[{pcsSlotInUnit0}].PCSActivePowerSetting");
            return $"目标有功:{pSet:0.0}kW";
        }

        public static string FormatPcsActualPowerLine(int essPcsListIndex, double activePowerKwFallback = 0)
        {
            double pAct = GuiSimDataAccess.SafeGetDouble(
                $"ess._pcsList[{essPcsListIndex}]._currentState.ActivePower",
                activePowerKwFallback);
            return $"实时有功:{pAct:0.0}kW";
        }

        /// <summary>主接线 PCS 框紧凑文案（缩短标签，语义与完整版一致）。</summary>
        public static string FormatPcsMainLineDeviceState(int unitIndex0, int pcsSlotInUnit0, int essPcsListIndex)
        {
            string phase = FormatPcsDeviceStateLabel(unitIndex0, pcsSlotInUnit0, essPcsListIndex);
            return $"状态:{phase}";
        }

        public static string FormatPcsMainLineStartStop(int unitIndex0, int pcsSlotInUnit0)
        {
            bool coilOn = GuiSimDataAccess.GetEmuPcsStartStopCoil(unitIndex0, pcsSlotInUnit0);
            return $"启停:{(coilOn ? "1" : "0")}";
        }

        public static string FormatPcsMainLineTargetPower(int unitIndex0, int pcsSlotInUnit0)
        {
            double pSet = GuiSimDataAccess.SafeGetDouble(
                $"emu{unitIndex0 + 1}.PcsList[{pcsSlotInUnit0}].PCSActivePowerSetting");
            return $"P设:{pSet:0.0}kW";
        }

        public static string FormatPcsMainLineActualPower(int essPcsListIndex, double activePowerKwFallback = 0)
        {
            double pAct = GuiSimDataAccess.SafeGetDouble(
                $"ess._pcsList[{essPcsListIndex}]._currentState.ActivePower",
                activePowerKwFallback);
            return $"P实:{pAct:0.0}kW";
        }

        public static string FormatPcsMainLineTargetReactive(int unitIndex0, int pcsSlotInUnit0)
        {
            double qSet = GuiSimDataAccess.SafeGetDouble(
                $"emu{unitIndex0 + 1}.PcsList[{pcsSlotInUnit0}].PCSReactivePowerSetting");
            return $"Q设:{qSet:0.0}kvar";
        }

        public static string FormatPcsMainLineActualReactive(int essPcsListIndex, double reactivePowerKvarFallback = 0)
        {
            double qAct = GuiSimDataAccess.SafeGetDouble(
                $"ess._pcsList[{essPcsListIndex}]._currentState.ReactivePower",
                reactivePowerKvarFallback);
            return $"Q实:{qAct:0.0}kvar";
        }

        public static string FormatPcsMainLineBlackStart(int unitIndex0, int pcsSlotInUnit0, int essPcsListIndex)
        {
            string st = FormatBlackStartSwitchStatus(unitIndex0, pcsSlotInUnit0, essPcsListIndex);
            st = st switch
            {
                "开(运行中)" => "运",
                "开(未生效)" => "未效",
                _ => st
            };
            return $"黑:{st}";
        }

        /// <summary>主接线 BMS 框紧凑文案。</summary>
        public static string FormatBmsMainLineGridConnect(int bmsIndex0)
        {
            int status = (int)GuiSimDataAccess.SafeGetDouble($"bms{bmsIndex0 + 1}.BatteryStacks[0].GridConnectStatus");
            bool linked = GuiSimDataAccess.SafeGetBool($"bms{bmsIndex0 + 1}.BatteryStacks[0].IsPcsLinked");
            string tag = status switch
            {
                0 => "未始",
                1 => "进行",
                2 => linked ? "已联" : "成功",
                3 => "失败",
                _ => $"S{status}"
            };
            return linked ? $"并网:{tag}" : $"并网:{tag}/离";
        }

        public static string FormatBmsMainLineBlackStart(int bmsIndex0)
        {
            int status = (int)GuiSimDataAccess.SafeGetDouble($"bms{bmsIndex0 + 1}.BatteryStacks[0].BlackStartStatus");
            string tag = status switch
            {
                0 => "空闲",
                3 => "进入",
                4 => "失败",
                5 => "退出",
                _ => $"S{status}"
            };
            return $"黑启:{tag}";
        }

        /// <summary>主接线 BMS 框空调紧凑文案：运行状态 + 柜内温度 + 制冷设定温。</summary>
        public static string FormatBmsMainLineAirConditioner(int bmsIndex0)
        {
            string prefix = $"bms{bmsIndex0 + 1}.AirConditioners[0]";
            if (GuiSimDataAccess.TryGetObject($"{prefix}.CabinetTemp") == null)
                return string.Empty;

            double temp = GuiSimDataAccess.SafeGetDouble($"{prefix}.CabinetTemp");
            double set = GuiSimDataAccess.SafeGetDouble($"{prefix}.CoolingSetTemp");
            bool on = GuiSimDataAccess.SafeGetBool($"{prefix}.DeviceOperationStatus");
            bool cooling = GuiSimDataAccess.SafeGetBool($"{prefix}.CompressorStatus");
            bool overheat = GuiSimDataAccess.SafeGetBool($"{prefix}.CabinetOverheat");

            string mode = !on ? "停机" : cooling ? "制冷" : "运行";
            string text = $"空调:{mode} {temp:0.0}℃ 设{set:0.0}℃";
            return overheat ? $"{text} 过热" : text;
        }

        public static string FormatPcsControlStatus(int unitIndex0, int pcsSlotInUnit0, int essPcsListIndex)
        {
            string blackStartSw = FormatBlackStartSwitchStatus(unitIndex0, pcsSlotInUnit0, essPcsListIndex);
            string deviceState = FormatPcsDeviceStateLabel(unitIndex0, pcsSlotInUnit0, essPcsListIndex);
            bool coilOn = GuiSimDataAccess.GetEmuPcsStartStopCoil(unitIndex0, pcsSlotInUnit0);
            double pSet = GuiSimDataAccess.SafeGetDouble($"emu{unitIndex0 + 1}.PcsList[{pcsSlotInUnit0}].PCSActivePowerSetting");
            double pAct = GuiSimDataAccess.SafeGetDouble($"ess._pcsList[{essPcsListIndex}]._currentState.ActivePower");

            return
                $"设备状态:{deviceState} 启停控制:{(coilOn ? "1" : "0")} 黑启动:{blackStartSw} " +
                $"目标有功:{pSet:0.0}kW 实时有功:{pAct:0.0}kW " +
                $"模式:{FormatGridModeLabel(GuiSimDataAccess.SafeGetString($"ess._pcsList[{essPcsListIndex}]._currentState.GMode"))}";
        }

        public static string FormatGridConnectStatus(int bmsIndex0)
        {
            int status = (int)GuiSimDataAccess.SafeGetDouble($"bms{bmsIndex0 + 1}.BatteryStacks[0].GridConnectStatus");
            string label = status switch
            {
                0 => "未开始",
                1 => "进行中",
                2 => "成功",
                3 => "失败",
                _ => $"未知({status})"
            };
            bool linked = GuiSimDataAccess.SafeGetBool($"bms{bmsIndex0 + 1}.BatteryStacks[0].IsPcsLinked");
            return $"{label}({(linked ? "已关联" : "未关联")})";
        }

        public static string FormatBlackStartModeStatus(int bmsIndex0)
        {
            int status = (int)GuiSimDataAccess.SafeGetDouble($"bms{bmsIndex0 + 1}.BatteryStacks[0].BlackStartStatus");
            int success = (int)GuiSimDataAccess.SafeGetDouble($"bms{bmsIndex0 + 1}.BatteryStacks[0].BlackStartEnterSuccess");
            string statusLabel = status switch
            {
                0 => "空闲",
                3 => "已进入",
                4 => "进入失败",
                5 => "已退出",
                _ => $"状态{status}"
            };
            return $"{statusLabel} 成功:{(success == 1 ? "是" : "否")}";
        }

        public static string FormatBatteryCompartmentLine(int bmsIndex0, double soc, double vdc, double idc)
        {
            return
                $"舱{bmsIndex0 + 1}:  SOC {soc:0.0}%  Vdc {vdc:0.0} V  Idc {idc:0.0} A  " +
                $"并离网:{FormatGridConnectStatus(bmsIndex0)}  黑启动:{FormatBlackStartModeStatus(bmsIndex0)}";
        }
    }
}
