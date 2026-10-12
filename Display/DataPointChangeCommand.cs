using EssSimulator.Core;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace EssSimulator.Display
{
    public class DataPointChangeCommand : ICommand
    {
        private static readonly Regex RackPointPattern = new(
            @"^(?<dev>[^.]+)\.r(?<rack>\d+|\*)\.(?<point>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public string Name => "dpc";
        public string Description => "数据点变位 (set/get)";

        public CommandResult Execute(string[] args)
        {
            if (args.Length == 1 && args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
                return CommandResult.Ok(PrintHelp());

            if (args.Length < 2)
                return CommandResult.Ok(PrintHelp());

            if (!TryExecuteDpcOperation(args, out var message))
                return CommandResult.Fail(message);

            return CommandResult.Ok(message);
        }

        public static bool TryExecuteDpcOperation(string[] args, out string message)
        {
            message = string.Empty;
            if (args.Length < 2)
            {
                message = "dpc 参数不足，请使用 dpc help 查看用法";
                return false;
            }

            var dpcname = args[0];
            var op = args[1].ToLowerInvariant();
            var opdata = args.Length > 2 ? string.Join(' ', args.Skip(2)) : string.Empty;

            // 簇级：simBms1.r0.yc1322 或 simBms1.r*.yc1322
            var rackMatch = RackPointPattern.Match(dpcname);
            if (rackMatch.Success)
                return TryExecuteRackDpc(
                    rackMatch.Groups["dev"].Value,
                    rackMatch.Groups["rack"].Value,
                    rackMatch.Groups["point"].Value,
                    op,
                    opdata,
                    out message);

            var dpcnameParts = dpcname.Split('.');
            if (dpcnameParts.Length != 2)
            {
                message = "dpcname 格式错误，应为 <device>.<datapoint> 或 <device>.r<N>.<datapoint>";
                return false;
            }

            var dpcDeviceName = dpcnameParts[0];
            var dpcDeviceDataPoint = dpcnameParts[1];

            var objectsCollect = SimulatorHost.Instance;
            var obj = objectsCollect.Get<object>(dpcDeviceName);
            IModbusRegisterServer? simServer = obj as IModbusRegisterServer;
            if (simServer == null)
            {
                bool anyEmu = false;
                try { anyEmu = SimulatorHost.Instance.Contains("simEmu1"); } catch { /* ignore */ }
                message = anyEmu
                    ? $"找不到 Modbus 设备 `{dpcDeviceName}`"
                    : $"找不到对应的 Modbus 设备 `{dpcDeviceName}`（仿真尚未就绪：点表未加载或 Modbus 从站启动失败。请确认 `pointmaps/models/` 与 `device-models.json` 选型后重启）";
                return false;
            }

            bool isControlPoint = simServer.ControlMaps.Any(m => m.ParamName == dpcDeviceDataPoint);
            bool isDataPoint    = simServer.DataMaps.Any(m => m.ParamName == dpcDeviceDataPoint);
            if (!isControlPoint && !isDataPoint)
            {
                bool isRackCtl = simServer.RackControlMaps.Any(m =>
                    string.Equals(m.ParamName, dpcDeviceDataPoint, StringComparison.OrdinalIgnoreCase));
                message = isRackCtl
                    ? $"点 `{dpcDeviceDataPoint}` 为簇级控制点，请使用: dpc {dpcDeviceName}.r0.{dpcDeviceDataPoint} set <原始值>"
                    : "指定设备找不到对应数据点";
                return false;
            }

            if (op == "set")
            {
                if (!ExternalControlGate.TryAllow(out message))
                    return false;

                if (string.IsNullOrWhiteSpace(opdata))
                {
                    message = "set 操作缺少参数值";
                    return false;
                }

                if (isControlPoint)
                {
                    object val = opdata;
                    if (bool.TryParse(opdata, out var bv)) val = bv;
                    else if (int.TryParse(opdata, out var iv)) val = iv;
                    simServer.SetDataObjectByMesurePointName(dpcDeviceDataPoint, val);
                    message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {dpcDeviceName}.{dpcDeviceDataPoint} 控制点写入寄存器原始值 {val}（工程值=原始值/Scale，经控制管道解析）";
                }
                else
                {
                    simServer.SetDataStoreByMesurePointName(dpcDeviceDataPoint, opdata);
                    message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {dpcDeviceName}.{dpcDeviceDataPoint} 设置值为 {opdata} (若 ModelSim 不为 0 将在下一个轮询周期被覆盖)";
                }
                return true;
            }

            if (op == "get")
            {
                object? result = simServer.GetDataObjectByMesurePointName(dpcDeviceDataPoint);
                if (result == null)
                {
                    message = "获取为空，可能原因: 1) 点名错误 2) 点不支持读取";
                }
                else
                {
                    message = $"设备:{dpcDeviceName} 数据点:{dpcDeviceDataPoint} val:{result}";
                }

                return true;
            }

            message = "不支持的操作，请使用 set 或 get，或 dpc help 查看用法";
            return false;
        }

        private static bool TryExecuteRackDpc(
            string deviceName,
            string rackToken,
            string pointName,
            string op,
            string opdata,
            out string message)
        {
            message = string.Empty;
            var obj = SimulatorHost.Instance.Get<object>(deviceName);
            if (obj is not IModbusRegisterServer simServer)
            {
                message = $"找不到 Modbus 设备 `{deviceName}`";
                return false;
            }

            if (!simServer.RackControlMaps.Any(m =>
                    string.Equals(m.ParamName, pointName, StringComparison.OrdinalIgnoreCase)))
            {
                message = $"设备 `{deviceName}` 找不到簇级控制点 `{pointName}`";
                return false;
            }

            if (op != "set")
            {
                message = "簇级门限点目前仅支持 set（写原始寄存器值）；读请用 mbpoll 对对应 rack 从站读 Holding";
                return false;
            }

            if (!ExternalControlGate.TryAllow(out message))
                return false;

            if (string.IsNullOrWhiteSpace(opdata))
            {
                message = "set 操作缺少参数值（原始寄存器值，工程值×Scale）";
                return false;
            }

            object val = opdata;
            if (bool.TryParse(opdata, out var bv)) val = bv;
            else if (int.TryParse(opdata, out var iv)) val = iv;
            else if (double.TryParse(opdata, out var dv)) val = dv;

            var targets = new List<int>();
            if (rackToken == "*")
            {
                // 从点表簇数量未知时，按常见上限尝试；后端会拒绝越界
                int maxGuess = 64;
                for (int i = 0; i < maxGuess; i++)
                    targets.Add(i);
            }
            else if (int.TryParse(rackToken, out var rackId))
            {
                targets.Add(rackId);
            }
            else
            {
                message = "簇索引格式错误，应为 r0 / r1 / r*";
                return false;
            }

            var okMessages = new List<string>();
            var errors = new List<string>();
            foreach (var rackId in targets)
            {
                if (!simServer.TrySetRackControl(rackId, pointName, val, out var oneMsg))
                {
                    if (rackToken == "*" && oneMsg.Contains("越界", StringComparison.Ordinal))
                        break;
                    errors.Add(oneMsg);
                    if (rackToken != "*")
                    {
                        message = oneMsg;
                        return false;
                    }
                    continue;
                }

                okMessages.Add(oneMsg);
                if (rackToken == "*" && okMessages.Count >= 1)
                {
                    // 继续直到越界
                }
            }

            if (okMessages.Count == 0)
            {
                message = errors.Count > 0 ? string.Join("; ", errors) : "簇级写入失败";
                return false;
            }

            message = rackToken == "*"
                ? $"已写入 {okMessages.Count} 个簇的 {pointName}\n" + string.Join("\n", okMessages.Take(3)) +
                  (okMessages.Count > 3 ? $"\n... 共 {okMessages.Count} 簇" : "")
                : okMessages[0];
            return true;
        }

        private static string PrintHelp()
        {
            return new[]
            {
                "用法: dpc <dpcname> <operation> <data>",
                "  堆级: dpc <device>.<datapoint> set|get [value]",
                "  簇级门限: dpc <device>.r<N>.<datapoint> set <原始值>",
                "         或 dpc <device>.r*.<datapoint> set <原始值>  （写全部簇）",
                "  示例:",
                "    dpc simBms1.yt0 set 1",
                "    dpc simBms1.r0.yc1322 set 3450   # 簇0 单体过压三级门限 3.45V (Scale1000)",
                "    dpc simBms1.r*.yc1322 set 3450   # 全部簇同门限",
            }.Aggregate((a, b) => a + Environment.NewLine + b);
        }
    }
}
