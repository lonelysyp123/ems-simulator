using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssSimModelApi.Bms;
using EssSimulator.EssSimModelApi.Mappers;
using EssSimulator.Protocol;
using System;
using System.Collections.Generic;

namespace EssSimulator.Display
{
    public class EssCommand : ICommand
    {
        public string Name => "esscmd";
        public string Description => "Ess 操控命令（负载 / 协议链路 / BMS 并离网）";

        public CommandResult Execute(string[] args)
        {
            if (args.Length == 0 || (args.Length == 1 && args[0].Equals("help", StringComparison.OrdinalIgnoreCase)))
                return CommandResult.Ok(PrintHelp());

            var verb = args[0];
            if (verb.Equals("setLoad", StringComparison.OrdinalIgnoreCase))
                return ExecuteSetLoad(args);

            if (verb.Equals("link", StringComparison.OrdinalIgnoreCase))
                return ExecuteLink(args);

            if (verb.Equals("setGrid", StringComparison.OrdinalIgnoreCase))
                return ExecuteSetGrid(args);

            if (verb.StartsWith("setbms", StringComparison.OrdinalIgnoreCase))
                return ExecuteSetBms(args);

            if (verb.StartsWith("bms", StringComparison.OrdinalIgnoreCase) &&
                args.Length == 3 &&
                args[1].Equals("fault", StringComparison.OrdinalIgnoreCase) &&
                args[2].Equals("clear", StringComparison.OrdinalIgnoreCase))
                return ExecuteBmsFaultClear(args);

            if (verb.StartsWith("pcs", StringComparison.OrdinalIgnoreCase))
                return ExecutePcsCommand(args);

            if (verb.StartsWith("setpv", StringComparison.OrdinalIgnoreCase))
                return ExecuteSetPv(args);

            return CommandResult.Fail("未知子命令，请使用 esscmd help 查看用法");
        }

        private static string PrintHelp()
        {
            return new[]
            {
                "esscmd 子命令:",
                "  setLoad activePower <kW>       // 手动设定负载有功（仅允许 ≤0：负=消耗，正值拒绝）",
                "  setLoad reactivePower <kvar>   // 手动设定负载无功（可正可负）",
                "  link pcsN on|off               // 开启/关闭第 N 路 PCS 所属 EMU 单元的 Modbus 对外服务",
                "  link bmsN on|off               // 开启/关闭第 N 路 BMS 的 Modbus 对外服务",
                "  link em on|off                 // 开启/关闭并网点电表 simEm 的 Modbus 对外服务",
                "  link status [pcsN|bmsN|em]     // 查看协议链路状态（省略目标则列出全部）",
                "  setGrid frequency <Hz>         // 设定仿真电网额定频率（并网时 PCS 跟网、电表 yc19）",
                "  setGrid voltage <V>            // 设定仿真电网额定线电压（如 220000）",
                "  pcsN start|stop                // PCS 启停（直控仿真设备，不依赖点表点位）",
                "  pcsN power <kW>                // PCS 有功设定（工程值 kW；无功保留现值）",
                "  pcsN reactive <kvar>           // PCS 无功设定（工程值 kvar；有功保留现值）",
                "  setbmsN power on|off           // BMS 物理并网/离网（PCS↔BMS 直流链路）",
                "  setbmsN soc <0~1|%>            // 热设 BMS 整堆 SOC（须待机；写透电芯，立即生效）",
                "  bmsN fault clear               // 待机时清除充放电方向内部故障，恢复可并网",
                "  setpvN run on|off              // 光伏单元启停（直控仿真设备）",
                "  setpvN power <kW>              // 光伏有功设定（限发 kW，≥0）",
                "  setpvN reactive <kvar>         // 光伏无功设定（kvar，可正可负）",
                "  setpvN array A|B temperature <℃> // 设定光伏方阵环境温度（电池温度由 NOCT 估算）",
                "  setpvN array A|B angle <度>    // 设定光伏方阵光照入射角（90=正对 1000 W/㎡，0/180=0）",
                "  setpvN array A|B albedo <0~1>  // 设定方阵地面反照率（G_rear = 反照率 × 平面辐照；0 关闭双面）",
                "",
                "说明:",
                "  - link off：关闭 TCP 监听，模拟通信中断；与 setbms power 无关",
                "  - setGrid：调整外部电网源；主断闭合后生效于 PCC/跟网 PCS",
                "  - setbmsN power on：触发并网，GridConnectStatus→2，IsPcsLinked=true",
                "  - setbmsN power off：断开 PCS↔BMS 链路，GridConnectStatus→0",
                "  - setbmsN soc：须堆电流为 0（待机）；0~1 为标幺，>1 且≤100 按百分比",
                "  - bmsN fault clear：待机时清除充放电方向内部故障（一次性）；再次超限会重新触发，三级故障会自动下电",
                "  - setpvN：方阵环境温度/入射角/反照率替代按时刻正弦的辐照，A/B 可分别设定",
                "  - 每台 PCS 对应独立 simEmu{N}（与 pcsN 1:1），关闭链路只影响该路 PCS"
            }.JoinLines();
        }

        private static CommandResult ExecuteBmsFaultClear(string[] args)
        {
            if (!TryParseBmsIndex(args[0], out int bms1Based, out var parseMessage))
                return CommandResult.Fail(parseMessage);

            if (!SimulatorHost.Instance.Contains($"bms{bms1Based}"))
                return CommandResult.Fail($"找不到 bms{bms1Based}（超出当前配置范围）");

            if (!BmsFaultClearEngine.TryClearFaults(bms1Based - 1, out var result))
                return CommandResult.Fail($"操作失败: {result}");

            // 强制刷新遥测 shadow（点名随点表版本而异，不存在的点名安全忽略）
            InvalidateBmsTelemetryShadow(bms1Based, "yc0", "param4", "param19");
            return CommandResult.Ok($"执行成功: bms{bms1Based} — {result}");
        }

        private static bool TryParseBmsIndex(string target, out int bms1Based, out string message)
        {
            bms1Based = 0;
            message = string.Empty;
            if (!target.StartsWith("bms", StringComparison.OrdinalIgnoreCase))
            {
                message = "子命令格式应为 bmsN，例如 bms1";
                return false;
            }

            if (!int.TryParse(target.AsSpan(3), out bms1Based) || bms1Based < 1)
            {
                message = "子命令格式应为 bmsN，例如 bms1";
                return false;
            }

            return true;
        }

        private static CommandResult ExecuteSetLoad(string[] args)
        {
            if (args.Length != 3)
                return CommandResult.Fail("用法: esscmd setLoad activePower|reactivePower <数值>");

            if (args[1] != "activePower" && args[1] != "reactivePower")
                return CommandResult.Fail("setLoad 仅支持 activePower 或 reactivePower");

            if (!double.TryParse(args[2], out var num))
                return CommandResult.Fail("请输入有效的数字");

            if (args[1] == "activePower" && num > 0)
                return CommandResult.Fail("负载有功只能消耗不能释放：请输入 ≤0 的值（负值=从电网取电）");

            var ess = SimulatorHost.Instance.Get<EnergyStorageSystem>("ess");
            if (ess == null)
                return CommandResult.Fail("找不到 ess 模型，请确认仿真已启动");

            ess.SetLoadCharacteristic(args[1], num);
            return CommandResult.Ok($"执行成功: 负载 {args[1]} = {num}");
        }

        private static CommandResult ExecuteSetGrid(string[] args)
        {
            if (args.Length != 3)
                return CommandResult.Fail("用法: esscmd setGrid frequency|voltage <数值>\n示例: esscmd setGrid frequency 50\n      esscmd setGrid voltage 220000");

            var ess = SimulatorHost.Instance.Get<EnergyStorageSystem>("ess");
            if (ess == null)
                return CommandResult.Fail("找不到 ess 模型，请确认仿真已启动");

            if (args[1].Equals("frequency", StringComparison.OrdinalIgnoreCase))
            {
                if (!double.TryParse(args[2], out var hz))
                    return CommandResult.Fail("请输入有效的频率数值（Hz）");

                if (!ess.TrySetGridFrequency(hz, out var message))
                    return CommandResult.Fail($"操作失败: {message}");

                UiSnapshotNotifier.RequestImmediatePush();
                return CommandResult.Ok($"执行成功: {message}");
            }

            if (args[1].Equals("voltage", StringComparison.OrdinalIgnoreCase))
            {
                if (!double.TryParse(args[2], out var volts))
                    return CommandResult.Fail("请输入有效的电压数值（V）");

                if (!ess.TrySetGridVoltage(volts, out var message))
                    return CommandResult.Fail($"操作失败: {message}");

                UiSnapshotNotifier.RequestImmediatePush();
                return CommandResult.Ok($"执行成功: {message}");
            }

            return CommandResult.Fail("setGrid 仅支持 frequency 或 voltage");
        }

        private static CommandResult ExecuteSetBms(string[] args)
        {
            if (args.Length < 2)
                return CommandResult.Fail("用法: esscmd setbmsN power on|off\n      esscmd setbmsN soc <0~1|%>");

            if (args[1].Equals("power", StringComparison.OrdinalIgnoreCase))
                return ExecuteSetBmsPower(args);

            if (args[1].Equals("soc", StringComparison.OrdinalIgnoreCase))
                return ExecuteSetBmsSoc(args);

            return CommandResult.Fail("用法: esscmd setbmsN power on|off\n      esscmd setbmsN soc <0~1|%>");
        }

        private static CommandResult ExecuteSetBmsSoc(string[] args)
        {
            if (args.Length != 3)
                return CommandResult.Fail("用法: esscmd setbmsN soc <0~1|%>\n示例: esscmd setbms1 soc 0.55\n      esscmd setbms1 soc 55");

            if (!TryParseSetBmsIndex(args[0], out int bms1Based, out var parseMessage))
                return CommandResult.Fail(parseMessage);

            if (!TryParseSocValue(args[2], out double soc, out var socMessage))
                return CommandResult.Fail(socMessage);

            if (!SimulatorHost.Instance.Contains($"bms{bms1Based}"))
                return CommandResult.Fail($"找不到 bms{bms1Based}（超出当前配置范围）");

            var ess = SimulatorHost.Instance.Get<EnergyStorageSystem>("ess");
            if (ess == null)
                return CommandResult.Fail("找不到 ess 仿真对象");

            int idx = bms1Based - 1;
            if (idx < 0 || idx >= ess._bmsRackDevices.Count)
                return CommandResult.Fail($"bms{bms1Based} 设备索引越界");

            if (!ess._bmsRackDevices[idx].TrySetSoc(soc, out var result))
                return CommandResult.Fail($"操作失败: {result}");

            InvalidateBmsTelemetryShadow(bms1Based, "yc11", "param47");
            UiSnapshotNotifier.RequestImmediatePush();
            return CommandResult.Ok($"执行成功: bms{bms1Based} — {result}");
        }

        /// <summary>解析 SOC：0~1 为标幺；>1 且 ≤100 按百分比。</summary>
        private static bool TryParseSocValue(string raw, out double soc, out string message)
        {
            soc = 0;
            message = string.Empty;
            if (!double.TryParse(raw, out var value) || double.IsNaN(value) || double.IsInfinity(value))
            {
                message = "请输入有效的 SOC 数值";
                return false;
            }

            if (value > 1.0 && value <= 100.0)
                value /= 100.0;

            if (value < 0.0 || value > 1.0)
            {
                message = "SOC 须在 0~1，或 0~100（百分比）";
                return false;
            }

            soc = value;
            return true;
        }

        private static CommandResult ExecuteSetBmsPower(string[] args)
        {
            if (args.Length != 3 || !args[1].Equals("power", StringComparison.OrdinalIgnoreCase))
                return CommandResult.Fail("用法: esscmd setbmsN power on|off\n示例: esscmd setbms1 power on");

            if (!TryParseSetBmsIndex(args[0], out int bms1Based, out var parseMessage))
                return CommandResult.Fail(parseMessage);

            if (!TryParseLinkState(args[2], out bool connect, out var stateMessage))
                return CommandResult.Fail(stateMessage);

            if (!SimulatorHost.Instance.Contains($"bms{bms1Based}"))
                return CommandResult.Fail($"找不到 bms{bms1Based}（超出当前配置范围）");

            if (!BmsLinkEngine.TrySetGridPower(bms1Based - 1, connect, out var result))
                return CommandResult.Fail($"操作失败: {result}");

            InvalidateBmsTelemetryShadow(bms1Based, "yc0", "yc45", "param4", "param43");
            return CommandResult.Ok($"执行成功: bms{bms1Based} {(connect ? "并网" : "离网")} — {result}");
        }

        /// <summary>
        /// 使 BMS 遥测 shadow 失效，强制下一轮数据 worker 回写实时值。
        /// 点名随点表版本而异（common 版 yc/yt、LC 版 param），不存在的点名安全忽略。
        /// </summary>
        private static void InvalidateBmsTelemetryShadow(int bms1Based, params string[] pointNames)
        {
            var server = SimulatorHost.Instance.Get<ModbusSimServer>($"simBms{bms1Based}");
            if (server == null)
                return;

            foreach (var name in pointNames)
                server.InvalidateDataShadow(name);
        }

        private static CommandResult ExecutePcsCommand(string[] args)
        {
            if (!TryParsePcsIndex(args[0], out int pcs1Based, out var parseMessage))
                return CommandResult.Fail(parseMessage);

            if (args.Length == 2)
            {
                string state = args[1].ToLowerInvariant();
                bool? start = state switch
                {
                    "start" or "on" or "1" or "true" => true,
                    "stop" or "off" or "0" or "false" => false,
                    _ => null
                };
                if (start == null)
                    return CommandResult.Fail("请使用 start|stop");

                if (!DeviceControlFacade.TrySetPcsRun(pcs1Based, start.Value, out var runMessage))
                    return CommandResult.Fail($"操作失败: {runMessage}");

                return CommandResult.Ok($"执行成功: PCS{pcs1Based} {(start.Value ? "启动" : "停机")} — {runMessage}");
            }

            if (args.Length == 3 &&
                (args[1].Equals("power", StringComparison.OrdinalIgnoreCase) ||
                 args[1].Equals("reactive", StringComparison.OrdinalIgnoreCase)))
            {
                if (!double.TryParse(args[2], out var value))
                    return CommandResult.Fail("请输入有效的数字");

                bool active = args[1].Equals("power", StringComparison.OrdinalIgnoreCase);
                if (!DeviceControlFacade.TrySetPcsPower(
                    pcs1Based,
                    active ? value : null,
                    active ? null : value,
                    out var powerMessage))
                    return CommandResult.Fail($"操作失败: {powerMessage}");

                return CommandResult.Ok($"执行成功: {powerMessage}");
            }

            return CommandResult.Fail(
                "用法: esscmd pcsN start|stop\n      esscmd pcsN power <kW>\n      esscmd pcsN reactive <kvar>\n示例: esscmd pcs1 start\n      esscmd pcs1 power 500");
        }

        private static bool TryParsePcsIndex(string target, out int pcs1Based, out string message)
        {
            pcs1Based = 0;
            message = string.Empty;
            if (!target.StartsWith("pcs", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(target.AsSpan(3), out pcs1Based) ||
                pcs1Based < 1)
            {
                message = "子命令格式应为 pcsN，例如 pcs1";
                return false;
            }

            return true;
        }

        private static CommandResult ExecuteSetPv(string[] args)
        {
            if (!TryParseSetPvIndex(args[0], out int pv1Based, out var parseMessage))
                return CommandResult.Fail(parseMessage);

            if (args.Length == 3 && args[1].Equals("run", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseLinkState(args[2], out bool run, out var stateMessage))
                    return CommandResult.Fail(stateMessage);

                if (!DeviceControlFacade.TrySetPvRun(pv1Based, run, out var runMessage))
                    return CommandResult.Fail($"操作失败: {runMessage}");

                return CommandResult.Ok($"执行成功: {runMessage}");
            }

            if (args.Length == 3 &&
                (args[1].Equals("power", StringComparison.OrdinalIgnoreCase) ||
                 args[1].Equals("reactive", StringComparison.OrdinalIgnoreCase)))
            {
                if (!double.TryParse(args[2], out var pvValue))
                    return CommandResult.Fail("请输入有效的数字");

                bool active = args[1].Equals("power", StringComparison.OrdinalIgnoreCase);
                if (!DeviceControlFacade.TrySetPvPower(
                    pv1Based,
                    active ? pvValue : null,
                    active ? null : pvValue,
                    out var powerMessage))
                    return CommandResult.Fail($"操作失败: {powerMessage}");

                return CommandResult.Ok($"执行成功: {powerMessage}");
            }

            if (args.Length != 5
                || !args[1].Equals("array", StringComparison.OrdinalIgnoreCase))
            {
                return CommandResult.Fail(
                    "用法: esscmd setpvN run on|off\n      esscmd setpvN power <kW>\n      esscmd setpvN reactive <kvar>\n      esscmd setpvN array A|B temperature|angle|albedo <数值>\n示例: esscmd setpv1 run on\n      esscmd setpv1 array A temperature 35");
            }

            string side = args[2].Trim().ToUpperInvariant();
            if (side != "A" && side != "B")
                return CommandResult.Fail("方阵侧应为 A 或 B");

            if (!double.TryParse(args[4], out var value))
                return CommandResult.Fail("请输入有效的数字");

            var ess = SimulatorHost.Instance.Get<EnergyStorageSystem>("ess");
            if (ess == null)
                return CommandResult.Fail("找不到 ess 模型，请确认仿真已启动");

            if (!ess.TrySetPvArrayClimate(pv1Based, side, args[3], value, out var message))
                return CommandResult.Fail($"操作失败: {message}");

            UiSnapshotNotifier.RequestImmediatePush();
            return CommandResult.Ok($"执行成功: {message}");
        }

        private static bool TryParseSetPvIndex(string verb, out int pv1Based, out string message)
        {
            pv1Based = 0;
            message = string.Empty;
            if (!verb.StartsWith("setpv", StringComparison.OrdinalIgnoreCase))
            {
                message = "子命令格式应为 setpvN，例如 setpv1";
                return false;
            }

            if (!int.TryParse(verb.AsSpan(5), out pv1Based) || pv1Based < 1)
            {
                message = "子命令格式应为 setpvN，例如 setpv1";
                return false;
            }

            return true;
        }

        private static bool TryParseSetBmsIndex(string verb, out int bms1Based, out string message)
        {
            bms1Based = 0;
            message = string.Empty;
            if (!verb.StartsWith("setbms", StringComparison.OrdinalIgnoreCase))
            {
                message = "子命令格式应为 setbmsN，例如 setbms1";
                return false;
            }

            if (!int.TryParse(verb.AsSpan(6), out bms1Based) || bms1Based < 1)
            {
                message = "子命令格式应为 setbmsN，例如 setbms1";
                return false;
            }

            return true;
        }

        private static CommandResult ExecuteLink(string[] args)
        {
            if (args.Length == 2 && args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
                return CommandResult.Ok("协议链路状态:", PrintAllLinkStatus());

            if (args.Length == 3 && args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryResolveProtocolServer(args[2], out var server, out var serverName, out var detail))
                    return CommandResult.Fail(detail);
                return CommandResult.Ok(FormatLinkStatus(args[2], serverName, server!, detail));
            }

            if (args.Length != 3)
                return CommandResult.Fail("用法: esscmd link pcsN|bmsN|em on|off\n      esscmd link status [pcsN|bmsN|em]");

            if (!TryResolveProtocolServer(args[1], out var simServer, out var resolvedName, out var resolveMessage))
                return CommandResult.Fail(resolveMessage);

            if (!TryParseLinkState(args[2], out var enable, out var stateMessage))
                return CommandResult.Fail(stateMessage);

            bool ok = simServer!.SetOnline(enable);
            if (!ok)
                return CommandResult.Fail($"操作失败: {resolvedName} 未能{(enable ? "恢复" : "关闭")} Modbus 服务");

            var listenInfo = SimServer.serverListenInfo.TryGetValue(resolvedName, out var info) ? info : resolvedName;
            return CommandResult.Ok(enable
                ? $"执行成功: {args[1]} -> {resolvedName} 已上线（{listenInfo}）{resolveMessage}"
                : $"执行成功: {args[1]} -> {resolvedName} 已离线，外部无法连接{resolveMessage}");
        }

        public static bool TryParseLinkState(string raw, out bool enable, out string message)
        {
            enable = false;
            message = string.Empty;
            switch (raw.ToLowerInvariant())
            {
                case "on":
                case "online":
                case "connect":
                    enable = true;
                    return true;
                case "off":
                case "offline":
                case "disconnect":
                    enable = false;
                    return true;
                default:
                    message = "链路状态仅支持 on/off（或 online/offline、connect/disconnect）";
                    return false;
            }
        }

        public static bool TryResolveProtocolServer(
            string target,
            out ModbusSimServer? server,
            out string serverName,
            out string detail)
        {
            server = null;
            serverName = string.Empty;
            detail = string.Empty;
            if (string.IsNullOrWhiteSpace(target))
            {
                detail = "请指定 pcsN、bmsN、pvN、pvMeterN 或 em";
                return false;
            }

            var store = SimulatorHost.Instance;
            if (target.Equals("em", StringComparison.OrdinalIgnoreCase))
            {
                serverName = "simEm";
                server = store.Get<ModbusSimServer>(serverName);
                if (server == null)
                {
                    detail = "找不到 simEm（电表 Modbus 未注册，请确认仿真已启动）";
                    return false;
                }
                return true;
            }

            if (target.StartsWith("pcs", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(target.AsSpan(3), out int pcsIdx) &&
                pcsIdx >= 1)
            {
                serverName = $"simEmu{pcsIdx}";
                server = store.Get<ModbusSimServer>(serverName);
                if (server == null)
                {
                    detail = $"找不到 {serverName}（pcs{pcsIdx} 超出当前配置范围）";
                    return false;
                }

                detail = $"（pcs{pcsIdx}）";
                return true;
            }

            if (target.StartsWith("bms", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(target.AsSpan(3), out int bmsIdx) &&
                bmsIdx >= 1)
            {
                serverName = $"simBms{bmsIdx}";
                server = store.Get<ModbusSimServer>(serverName);
                if (server == null)
                {
                    detail = $"找不到 {serverName}（bms{bmsIdx} 超出当前配置范围）";
                    return false;
                }
                return true;
            }

            if (target.StartsWith("pvMeter", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(target.AsSpan(7), out int pvMeterIdx) &&
                pvMeterIdx >= 1)
            {
                serverName = $"simPvMeter{pvMeterIdx}";
                server = store.Get<ModbusSimServer>(serverName);
                if (server == null)
                {
                    detail = $"找不到 {serverName}（pvMeter{pvMeterIdx} 超出当前配置范围）";
                    return false;
                }
                return true;
            }

            if (target.StartsWith("pv", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(target.AsSpan(2), out int pvIdx) &&
                pvIdx >= 1)
            {
                serverName = $"simPv{pvIdx}";
                server = store.Get<ModbusSimServer>(serverName);
                if (server == null)
                {
                    detail = $"找不到 {serverName}（pv{pvIdx} 超出当前配置范围）";
                    return false;
                }
                return true;
            }

            detail = "目标格式应为 pcsN、bmsN、pvN、pvMeterN 或 em，例如 pcs1、bms3、pv1、em";
            return false;
        }

        public static List<LinkStatusDto> BuildAllLinkStatus()
        {
            var list = new List<LinkStatusDto>();
            var store = SimulatorHost.Instance;

            if (store.Contains("simEm"))
            {
                var emServer = store.Get<ModbusSimServer>("simEm");
                list.Add(BuildLinkStatusDto("em", "simEm", emServer!, "", "em"));
            }

            int bms = 1;
            while (store.Contains($"simBms{bms}"))
            {
                var server = store.Get<ModbusSimServer>($"simBms{bms}");
                list.Add(BuildLinkStatusDto($"bms{bms}", $"simBms{bms}", server!, "", $"bms{bms}"));
                bms++;
            }

            int emu = 1;
            while (store.Contains($"simEmu{emu}"))
            {
                var server = store.Get<ModbusSimServer>($"simEmu{emu}");
                list.Add(BuildLinkStatusDto($"pcs{emu}", $"simEmu{emu}", server!, "", $"pcs{emu}"));
                var ied = EssSimulator.Protocol.Iec61850.Iec61850LayerManager.Instance.GetSnapshot()
                    .FirstOrDefault(s => string.Equals(s.ServerName, $"simEmu{emu}", StringComparison.OrdinalIgnoreCase));
                if (ied != null)
                {
                    list.Add(new LinkStatusDto
                    {
                        Label = $"pcs{emu}-61850",
                        ServerName = $"{ied.ServerName}.iec61850",
                        Target = $"iec61850-pcs{emu}",
                        Online = ied.Online,
                        ListenInfo = $"IEC 61850 MMS {ied.IedName} 端口 {ied.Port}",
                        Extra = Extra61850(ied)
                    });
                }
                emu++;
            }

            int pv = 1;
            while (store.Contains($"simPv{pv}"))
            {
                var server = store.Get<ModbusSimServer>($"simPv{pv}");
                list.Add(BuildLinkStatusDto($"pv{pv}", $"simPv{pv}", server!, "", $"pv{pv}"));
                pv++;
            }

            int pvMeter = 1;
            while (store.Contains($"simPvMeter{pvMeter}"))
            {
                var server = store.Get<ModbusSimServer>($"simPvMeter{pvMeter}");
                list.Add(BuildLinkStatusDto($"pvMeter{pvMeter}", $"simPvMeter{pvMeter}", server!, "", $"pvMeter{pvMeter}"));
                pvMeter++;
            }

            return list;
        }

        private static string Extra61850(EssSimulator.Protocol.Iec61850.Iec61850DeviceSnapshot ied)
        {
            var parts = new List<string> { $"客户端 {ied.AssociatedClients}" };
            if (!string.IsNullOrWhiteSpace(ied.GooseInterface))
                parts.Add($"网卡 {ied.GooseInterface}");
            if (ied.GooseSubscribing)
                parts.Add(ied.GooseSubscribeAppId.HasValue
                    ? $"GOOSE订 0x{ied.GooseSubscribeAppId.Value:X4}"
                    : "GOOSE订");
            else if (!string.IsNullOrWhiteSpace(ied.GooseSubscribeSkip))
                parts.Add($"GOOSE订关 {ied.GooseSubscribeSkip}");
            return string.Join(" ", parts);
        }

        private static LinkStatusDto BuildLinkStatusDto(string label, string serverName, ModbusSimServer server, string extra, string target)
        {
            var listenInfo = SimServer.serverListenInfo.TryGetValue(serverName, out var info) ? info : serverName;
            return new LinkStatusDto
            {
                Label = label,
                ServerName = serverName,
                Target = target,
                Online = server.IsOnline,
                ListenInfo = listenInfo,
                Extra = extra
            };
        }

        private static string PrintAllLinkStatus()
        {
            var list = BuildAllLinkStatus();
            var lines = new List<string>();
            foreach (var s in list)
            {
                lines.Add(string.IsNullOrWhiteSpace(s.Extra)
                    ? $"  {s.Label,-12} {s.ServerName,-10} {(s.Online ? "在线" : "离线"),-4} {s.ListenInfo}"
                    : $"  {s.Label,-14} {s.ServerName,-10} {(s.Online ? "在线" : "离线"),-4} {s.ListenInfo}  ({s.Extra})");
            }
            return lines.JoinLines();
        }

        private static string FormatLinkStatus(string label, string serverName, ModbusSimServer server, string extra)
        {
            var listenInfo = SimServer.serverListenInfo.TryGetValue(serverName, out var info) ? info : serverName;
            var state = server.IsOnline ? "在线" : "离线";
            return string.IsNullOrWhiteSpace(extra)
                ? $"  {label,-12} {serverName,-10} {state,-4} {listenInfo}"
                : $"  {label,-14} {serverName,-10} {state,-4} {listenInfo}  ({extra})";
        }
    }

    public sealed class LinkStatusDto
    {
        public string Label { get; set; } = "";
        public string ServerName { get; set; } = "";
        public string Target { get; set; } = "";
        public bool Online { get; set; }
        public string ListenInfo { get; set; } = "";
        public string Extra { get; set; } = "";
    }

    internal static class StringJoinExtensions
    {
        public static string JoinLines(this IEnumerable<string> lines) =>
            string.Join(Environment.NewLine, lines);
    }
}
