using EssSimulator.Protocol.Modbus;
using NModbus;
using NModbus.Data;
using log4net;

namespace EssSimulator
{
    /// <summary>
    /// Modbus Slave TCP：传输层（监听/从站网络/寄存器镜像）由 <see cref="ModbusPortHub"/> 统一提供，
    /// 同端口多设备共享监听；同端口同从站号共享寄存器镜像（挂载前已完成地址查重）。
    /// </summary>
    public class ModbusTCPSlave : ModbusSlave, IModbusSlave
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ModbusTCPSlave));

        private readonly int rackCount;
        private readonly ModbusPortHub _hub;
        private int _attachGeneration;
        private bool _attached;

        public ModbusTCPSlave(
            DeviceInfoDto deviceInfoDto,
            List<MapEntry[]> pointMaps,
            int rackCount = 0,
            ModbusPortHub? hub = null)
            : base(deviceInfoDto, pointMaps, communicator: null, rackCount)
        {
            this.rackCount = rackCount;
            _hub = hub ?? ModbusPortHub.Instance;
        }

        public override void DeviceConnect()
        {
            _attachGeneration++;
            int generation = _attachGeneration;

            int port = deviceInfoDto.port;
            byte bankSlaveId = deviceInfoDto.slaveId;
            string name = deviceInfoDto.name ?? string.Empty;

            var bankResult = _hub.AttachDevice(port, bankSlaveId, name, pointMap);
            if (!bankResult.Ok)
            {
                foreach (var error in bankResult.Errors)
                    Log.Error($"{name} 挂载失败：{error}");
                return;
            }

            modbusSlaveNetwork = _hub.GetNetwork(port);
            var bankIndex = new ModbusControlAddressIndex(pointMap);
            AttachControlWriteHooks(bankResult.DataStore!, bankSlaveId, bankIndex, generation);

            if (rackCount > 0 && rackPointMap != null)
            {
                var rackIndex = new ModbusControlAddressIndex(rackPointMap);
                for (int r = 0; r < rackCount; r++)
                {
                    byte sid = (byte)(bankSlaveId + r + 1);
                    var rackResult = _hub.AttachDevice(port, sid, $"{name}#rack{r + 1}", rackPointMap);
                    if (!rackResult.Ok)
                    {
                        foreach (var error in rackResult.Errors)
                            Log.Error($"{name} rack{r + 1} 挂载失败：{error}");
                        // 簇从站挂载冲突时整体回退，避免半挂载状态
                        _hub.DetachDevice(port, bankSlaveId, name);
                        modbusSlaveNetwork = null;
                        return;
                    }
                    AttachControlWriteHooks(rackResult.DataStore!, sid, rackIndex, generation);
                }
            }

            _attached = true;
        }

        /// <summary>
        /// 挂载控制写钩子到共享寄存器镜像。钩子闭包捕获挂载代数（generation），
        /// 设备重挂载后旧钩子自动失效，避免共享槽位上残留钩子重复触发控制管道。
        /// </summary>
        private void AttachControlWriteHooks(
            SlaveDataStore dataStore, byte slaveId, ModbusControlAddressIndex index, int generation)
        {
            if (dataStore.CoilDiscretes is PointSource<bool> coils)
            {
                coils.AfterWrite += (_, e) =>
                {
                    if (generation != _attachGeneration || !_attached)
                        return;
                    if (!ShouldNotifyExternalControlWrite)
                        return;
                    if (index.TouchesCoilWrite(e.StartAddress, e.NumberOfPoints))
                        NotifyExternalControlWrite(slaveId);
                };
            }

            if (dataStore.HoldingRegisters is PointSource<ushort> holding)
            {
                holding.AfterWrite += (_, e) =>
                {
                    if (generation != _attachGeneration || !_attached)
                        return;
                    if (!ShouldNotifyExternalControlWrite)
                        return;
                    if (!index.TouchesHoldingWrite(e.StartAddress, e.NumberOfPoints))
                        return;
                    CaptureExternalHoldingWrite(slaveId, e.StartAddress, e.NumberOfPoints);
                    NotifyExternalControlWrite(slaveId);
                };
            }
        }

        public override void DeviceDisconnect()
        {
            base.DeviceDisconnect();

            _attached = false;
            int port = deviceInfoDto.port;
            byte bankSlaveId = deviceInfoDto.slaveId;
            string name = deviceInfoDto.name ?? string.Empty;

            _hub.DetachDevice(port, bankSlaveId, name);
            if (rackCount > 0)
            {
                for (int r = 0; r < rackCount; r++)
                    _hub.DetachDevice(port, (byte)(bankSlaveId + r + 1), $"{name}#rack{r + 1}");
            }

            modbusSlaveNetwork = null;
        }

        public override bool GetCommunicatorState() => _attached && _hub.IsPortListening(deviceInfoDto.port);
    }
}
