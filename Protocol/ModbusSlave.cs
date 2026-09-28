using NModbus;
using NModbus.Device;
using NModbus.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;
using NModbus.Data;
using System.Threading;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator
{
    /// <summary>
    /// Modbus Slave
    /// </summary>
    public class ModbusSlave
    {
        #region Const
        private const int ADDRESS_LENGTH = 16;
        private const int BYTE_LENGTH = 8;
        private const int COILWRITEFUNCTIONCODE = 5;
        private const int CTRLFUNCTIONCODE = 6;
        private const int CTRLBATCHFUNCTIONCODE = 16;
        #endregion

        #region ReadOnly
        protected readonly MapEntry[]? rackPointMap;
        protected readonly MapEntry[] pointMap;
        protected readonly CommunicatorBase? communicator;
        protected readonly DeviceInfoDto deviceInfoDto;
        #endregion

        protected IModbusSlaveNetwork? modbusSlaveNetwork;
        private Dictionary<int, int> CtrlContinuerAddressGroup; // function code -> 6
        private Dictionary<int, int>? CtrlContinuerAddressGroupForRack; // function code -> 6
        private readonly HashSet<int> _bankTelemetryOwnedHolding;
        private readonly HashSet<int> _rackTelemetryOwnedHolding;
        private readonly Dictionary<(byte SlaveId, int Address), ushort> _telemetryHolding = new();
        private readonly Dictionary<(byte SlaveId, string Param), byte[]> _controlLatch = new();
        private readonly object _splitGate = new();
        private int _telemetryGeneration;
        private int _suppressWriteNotifyDepth;
        ILog log = LogManager.GetLogger(typeof(ModbusSlave));

        /// <summary>外部 Modbus 主站写入控制区（FC5/FC6/FC16）后触发，slaveId 为 NModbus 从站号。</summary>
        public event Action<byte>? ExternalControlWrite;
        public ModbusSlave(DeviceInfoDto deviceInfoDto, List<MapEntry[]> pointMaps, CommunicatorBase? communicator = null, int rackCount = 0)
        {
            this.communicator = communicator;
            this.deviceInfoDto = deviceInfoDto;

            pointMap = pointMaps[0];
            CtrlContinuerAddressGroup = CalcContinuerAddress(pointMap);
            _bankTelemetryOwnedHolding = TelemetryOwnedHolding(pointMap);
            if (rackCount > 0)
            {
                rackPointMap = pointMaps[1];
                CtrlContinuerAddressGroupForRack = CalcContinuerAddress(rackPointMap);
                _rackTelemetryOwnedHolding = TelemetryOwnedHolding(rackPointMap);
            }
            else
            {
                _rackTelemetryOwnedHolding = new HashSet<int>();
            }
        }

        private Dictionary<int, int> CalcContinuerAddress(MapEntry[] pointMap)
        {
            Dictionary<int, int> addressLengthGroup = new Dictionary<int, int>();
            var filtered = pointMap
                .Where(p => p.FunctionCode == CTRLFUNCTIONCODE || p.FunctionCode == CTRLBATCHFUNCTIONCODE)
                .ToArray();
            var continuerAddressGroup = filtered.OrderBy(p => p.Address).ToArray();
            int currentSpanLength = 0; // 当前连续段累计的寄存器长度
            int currentSpanItemCount = 0; // 当前连续段包含的点表条目数，因为有可能存在地址重复的点表条目或者是Size>ADDRESS_LENGTH的点表条目
            for (int i = 0; i < continuerAddressGroup.Length; i++)
            {
                currentSpanItemCount++;
                int addressNum = continuerAddressGroup[i].Size / ADDRESS_LENGTH;
                if (addressNum == 0) addressNum = 1;
                if (i <= 0 || continuerAddressGroup[i].Address != continuerAddressGroup[i - 1].Address)
                {
                    currentSpanLength += addressNum;
                }

                if (currentSpanLength < 120)
                {
                    if (i + 1 < continuerAddressGroup.Length)
                    {
                        if (continuerAddressGroup[i].Address + addressNum == continuerAddressGroup[i + 1].Address
                        || continuerAddressGroup[i].Address == continuerAddressGroup[i + 1].Address)
                        {
                            continue;
                        }
                    }
                }

                var groupStartAddr = continuerAddressGroup[i + 1 - currentSpanItemCount].Address;
                addressLengthGroup.Add(groupStartAddr, currentSpanLength);
                currentSpanLength = 0;
                currentSpanItemCount = 0;
            }
            return addressLengthGroup;
        }

        public virtual void DeviceConnect()
        {
            if (communicator is not null)
                communicator.Connect();

        }

        public virtual void DeviceDisconnect()
        {
            if (communicator is not null)
                communicator.Disconnect();
        }

        public virtual bool GetCommunicatorState()
        {
            if (communicator == null)
                return false;
            return communicator.GetCommunicatorStatus() != 0;
        }

        /// <summary>内部回写 Modbus 时抑制 ExternalControlWrite（避免反馈/遥测写触发控制管道）。</summary>
        public IDisposable SuppressWriteNotifications()
        {
            Interlocked.Increment(ref _suppressWriteNotifyDepth);
            return new WriteNotificationSuppressScope(this);
        }

        internal bool ShouldNotifyExternalControlWrite =>
            Volatile.Read(ref _suppressWriteNotifyDepth) == 0;

        protected void NotifyExternalControlWrite(byte slaveId)
        {
            if (!ShouldNotifyExternalControlWrite)
                return;

            ExternalControlWrite?.Invoke(slaveId);
        }

        private sealed class WriteNotificationSuppressScope : IDisposable
        {
            private ModbusSlave? _owner;

            public WriteNotificationSuppressScope(ModbusSlave owner) => _owner = owner;

            public void Dispose()
            {
                if (_owner == null)
                    return;

                Interlocked.Decrement(ref _owner._suppressWriteNotifyDepth);
                _owner = null;
            }
        }

        // 优化读取方法，只读取外部能够修改的部分，也就是function code为3的
        public Dictionary<string, object>? Read(byte slaveId = 1)
        {
            // 传入的从站号与 bank 从站号一致时读 pointMap，否则读 rackPointMap
            var pointMapToUse = slaveId == deviceInfoDto.slaveId ? pointMap : rackPointMap;
            var ctrlContinuerAddressGroup = slaveId == deviceInfoDto.slaveId ? CtrlContinuerAddressGroup : CtrlContinuerAddressGroupForRack;

            if (pointMapToUse == null || ctrlContinuerAddressGroup == null)
            {
                log.Error("Modbus Slave 读取数据失败，点表或连续地址段为空");
                return null;
            }

            Dictionary<string, object> propertyDataGroup = new Dictionary<string, object>();
            foreach (var continuerAddress in ctrlContinuerAddressGroup)
            {
                // 预筛功能码，避免内层重复 LINQ 分配
                var candidates = pointMapToUse
                    .Where(p => p.FunctionCode == CTRLFUNCTIONCODE || p.FunctionCode == CTRLBATCHFUNCTIONCODE)
                    .ToArray();
                List<byte[]> data = ReadFunc((ushort)continuerAddress.Key, (ushort)continuerAddress.Value, CTRLFUNCTIONCODE, slaveId);
                for (int i = 0; i < continuerAddress.Value; i++)
                {
                    byte[] bytes = data[i];
                    var absoluteAddr = i + continuerAddress.Key;
                    foreach (var entry in candidates)
                    {
                        if (!(entry.Address <= absoluteAddr && absoluteAddr < entry.Address + (entry.Size / ADDRESS_LENGTH == 0 ? 1 : entry.Size / ADDRESS_LENGTH))) continue;

                        string? paramName = entry.ParamName;
                        if (paramName == null) continue;
                        if (TryCopyControlLatch(slaveId, paramName, out var latched))
                        {
                            propertyDataGroup[paramName] = latched;
                            continue;
                        }
                        if (propertyDataGroup.TryGetValue(paramName, out var existing))
                        {
                            var propertyData = (existing as byte[])!.ToList();
                            propertyData.AddRange(bytes);
                            propertyDataGroup[paramName] = propertyData.ToArray();
                        }
                        else
                        {
                            propertyDataGroup.Add(paramName, bytes);
                        }
                    }
                }
            }

            // FC05 线圈控制点按点位逐个读取（线圈区与寄存器区分离，不参与 06 连续段优化）。
            ReadCoilPoints(pointMapToUse, propertyDataGroup, COILWRITEFUNCTIONCODE, slaveId);

            // FC01 线圈读取点按点位逐个读取（用于内部轮询/调试读取）
            ReadCoilPoints(pointMapToUse, propertyDataGroup, 1, slaveId);
            return propertyDataGroup;
        }

        private void ReadCoilPoints(
            MapEntry[] pointMapToUse,
            Dictionary<string, object> propertyDataGroup,
            int functionCode,
            byte slaveId)
        {
            var coilCandidates = pointMapToUse.Where(p => p.FunctionCode == functionCode).ToArray();
            foreach (var entry in coilCandidates)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.ParamName)) continue;
                var coilRaw = ReadFunc((ushort)entry.Address, 1, functionCode, slaveId);
                if (coilRaw == null || coilRaw.Count == 0) continue;
                propertyDataGroup[entry.ParamName] = coilRaw[0];
            }
        }

        public byte[] Read(string paramName)
        {
            // 如果slaveid为1，则读取的是pointMap，否则读取rackPointMap
            var pointMapToUse = pointMap;
            if (pointMapToUse == null || paramName == null)
            {
                log.Error("Modbus Slave 读取数据失败，点表或连续地址段为空");
                return null;
            }
            var entry = pointMapToUse.Where(p => p.ParamName == paramName).FirstOrDefault();
            if (entry == null) return null;
            if (TryCopyControlLatch(deviceInfoDto.slaveId, paramName, out var latched))
                return latched;
            // 预筛功能码，避免内层重复 LINQ 分配
            ushort num = (ushort)(entry.Size / ADDRESS_LENGTH);
            if (num == 0) num = 1;
            List<byte[]> data = ReadFunc((ushort)entry.Address, num, entry.FunctionCode, deviceInfoDto.slaveId);
            if (data == null || data.Count == 0) return Array.Empty<byte>();
            return data.SelectMany(x => x).ToArray();
        }

        public bool Write(Dictionary<string, object> data, byte slaveId = 1, bool applyScale = true)
        {
            // 传入的从站号与 bank 从站号一致时写 pointMap，否则写 rackPointMap
            var pointMapToUse = slaveId == deviceInfoDto.slaveId ? pointMap : rackPointMap;
            if (pointMapToUse == null)
            {
                log.Error("Modbus Slave 写入数据失败，点表为空");
                return false;
            }
            // 完成slave的自我数据写入
            byte[] actualValArrary;
            foreach (var item in data)
            {
                var pm = pointMapToUse.Where(p => p.ParamName == item.Key);
                if (pm == null || pm.Count() == 0) continue;
                var functionCode = pm.First().FunctionCode;
                var currentAddress = pm.First().Address;
                if (functionCode == COILWRITEFUNCTIONCODE)
                {
                    // FC05: 写单线圈，初始化默认值只需要把 0/1 或 bool 写入 Coil 区。
                    bool coilValue = item.Value switch
                    {
                        bool b => b,
                        string s when bool.TryParse(s, out var bv) => bv,
                        string s when int.TryParse(s, out var iv) => iv != 0,
                        _ => Convert.ToDouble(item.Value) != 0
                    };
                    var coilBytes = new byte[] { (byte)(coilValue ? 1 : 0) };
                    WriteFunc(slaveId, (ushort)currentAddress, coilBytes, coilBytes.Length, functionCode);
                    continue;
                }

                try
                {
                    actualValArrary = ModbusPointCodec.Encode(item.Value, pm.First(), applyScale);
                }
                catch (Exception ex)
                {
                    log.Warn($"Encode failed for {pm.First().ParamName}: {ex}");
                    continue;
                }
                WriteFunc(slaveId, (ushort)currentAddress, actualValArrary, actualValArrary.Length, functionCode);
                PreserveRegisterSplit(slaveId, pm.First(), actualValArrary);
            }
            return true;
        }

        /// <summary>
        /// 外部主站写入保持寄存器后调用：重叠地址上的控制值进入锁存，遥测字立刻写回，
        /// 使功能码 03 继续返回遥测，控制管道读到的仍是本次设定。
        /// </summary>
        internal void CaptureExternalHoldingWrite(byte slaveId, ushort startAddress, int pointCount)
        {
            var map = slaveId == deviceInfoDto.slaveId ? pointMap : rackPointMap;
            if (map == null || modbusSlaveNetwork == null || pointCount <= 0)
                return;
            if (!SpanOverlapsOwned(slaveId, startAddress, pointCount))
                return;

            var nmodbus = modbusSlaveNetwork.GetSlave(slaveId);
            if (nmodbus == null)
                return;

            int writeStart = startAddress;
            int writeEnd = startAddress + pointCount;
            foreach (var entry in map)
            {
                if (entry.FunctionCode is not (CTRLFUNCTIONCODE or CTRLBATCHFUNCTIONCODE)
                    || string.IsNullOrWhiteSpace(entry.ParamName))
                    continue;

                int length = Math.Max(1, entry.Size / ADDRESS_LENGTH);
                int entryStart = entry.Address;
                int entryEnd = entryStart + length;
                int overlapStart = Math.Max(entryStart, writeStart);
                int overlapEnd = Math.Min(entryEnd, writeEnd);
                if (overlapStart >= overlapEnd || !SpanOverlapsOwned(slaveId, entryStart, length))
                    continue;

                byte[] image;
                lock (_splitGate)
                {
                    image = _controlLatch.TryGetValue((slaveId, entry.ParamName), out var existing)
                        ? (byte[])existing.Clone()
                        : new byte[length * 2];
                }

                var regs = nmodbus.DataStore.HoldingRegisters.ReadPoints(
                    (ushort)overlapStart, (ushort)(overlapEnd - overlapStart));
                int offset = (overlapStart - entryStart) * 2;
                for (int i = 0; i < regs.Length && offset + (i + 1) * 2 <= image.Length; i++)
                {
                    var raw = BitConverter.GetBytes(regs[i]);
                    Buffer.BlockCopy(raw, 0, image, offset + i * 2, 2);
                }

                lock (_splitGate)
                    _controlLatch[(slaveId, entry.ParamName)] = image;
            }

            using (SuppressWriteNotifications())
                RestoreOwned(slaveId, writeStart, pointCount);
        }

        private static HashSet<int> TelemetryOwnedHolding(MapEntry[]? map)
        {
            var owned = new HashSet<int>();
            if (map == null)
                return owned;

            var control = new List<(int Start, int End)>();
            var telemetry = new List<(int Start, int End)>();
            foreach (var entry in map)
            {
                int length = Math.Max(1, entry.Size / 16);
                var span = (entry.Address, entry.Address + length);
                if (entry.FunctionCode is 6 or 16)
                    control.Add(span);
                else if (entry.FunctionCode == 3)
                    telemetry.Add(span);
            }

            foreach (var data in telemetry)
            {
                foreach (var command in control)
                {
                    int start = Math.Max(data.Start, command.Start);
                    int end = Math.Min(data.End, command.End);
                    for (int address = start; address < end; address++)
                        owned.Add(address);
                }
            }

            return owned;
        }

        private HashSet<int> OwnedHolding(byte slaveId) =>
            slaveId == deviceInfoDto.slaveId ? _bankTelemetryOwnedHolding : _rackTelemetryOwnedHolding;

        private bool SpanOverlapsOwned(byte slaveId, int start, int length)
        {
            var owned = OwnedHolding(slaveId);
            if (owned.Count == 0 || length <= 0)
                return false;
            for (int address = start; address < start + length; address++)
            {
                if (owned.Contains(address))
                    return true;
            }

            return false;
        }

        private bool TryCopyControlLatch(byte slaveId, string paramName, out byte[] raw)
        {
            lock (_splitGate)
            {
                if (_controlLatch.TryGetValue((slaveId, paramName), out var latched))
                {
                    raw = (byte[])latched.Clone();
                    return true;
                }
            }

            raw = Array.Empty<byte>();
            return false;
        }

        private void PreserveRegisterSplit(byte slaveId, MapEntry entry, byte[] encoded)
        {
            int length = Math.Max(1, entry.Size / ADDRESS_LENGTH);
            if (entry.FunctionCode == 3)
            {
                RememberTelemetry(slaveId, entry.Address, encoded);
                return;
            }

            if (entry.FunctionCode is not (CTRLFUNCTIONCODE or CTRLBATCHFUNCTIONCODE)
                || string.IsNullOrWhiteSpace(entry.ParamName)
                || !SpanOverlapsOwned(slaveId, entry.Address, length))
                return;

            lock (_splitGate)
                _controlLatch[(slaveId, entry.ParamName)] = (byte[])encoded.Clone();

            using (SuppressWriteNotifications())
                RestoreOwned(slaveId, entry.Address, length);
        }

        private void RememberTelemetry(byte slaveId, int start, byte[] encoded)
        {
            if (encoded.Length < 2 || !SpanOverlapsOwned(slaveId, start, encoded.Length / 2))
                return;

            var words = Common.ConvertBytesToUShorts(encoded);
            lock (_splitGate)
            {
                var owned = OwnedHolding(slaveId);
                _telemetryGeneration++;
                for (int i = 0; i < words.Length; i++)
                {
                    int address = start + i;
                    if (owned.Contains(address))
                        _telemetryHolding[(slaveId, address)] = words[i];
                }
            }
        }

        private void RestoreOwned(byte slaveId, int start, int length)
        {
            var owned = OwnedHolding(slaveId);
            for (int i = 0; i < length; i++)
            {
                int address = start + i;
                if (!owned.Contains(address))
                    continue;

                for (int attempt = 0; attempt < 4; attempt++)
                {
                    int generation;
                    ushort word;
                    lock (_splitGate)
                    {
                        generation = _telemetryGeneration;
                        _telemetryHolding.TryGetValue((slaveId, address), out word);
                    }

                    var bytes = BitConverter.GetBytes(word);
                    WriteFunc(slaveId, (ushort)address, bytes, bytes.Length, 3);
                    lock (_splitGate)
                    {
                        if (_telemetryGeneration == generation)
                            break;
                    }
                }
            }
        }

        private void WriteFunc(byte slaveId, ushort address, byte[] data, int num, int functionCode)
        {
            using (SuppressWriteNotifications())
            {
                WriteFuncCore(slaveId, address, data, num, functionCode);
            }
        }

        private void WriteFuncCore(byte slaveId, ushort address, byte[] data, int num, int functionCode)
        {
            try
            {
                var modbusSlave = modbusSlaveNetwork?.GetSlave(slaveId);
                if (functionCode == 1)
                {
                    // FC01: 写多线圈（用于内部写入或扩展场景）
                    bool[] values = data.Select(item => item == 1).ToArray();
                    modbusSlave?.DataStore.CoilDiscretes.WritePoints(address, values);
                }
                else if (functionCode == 2)
                {
                    // 暂不支持 FC02（离散输入）外部读写（避免语义混乱）
                    log.Warn($"FC02 write ignored. Device={deviceInfoDto.name}, address={address}, len={data?.Length ?? 0}");
                }
                else if (functionCode == COILWRITEFUNCTIONCODE)
                {
                    bool[] values = data.Select(item => item == 1).ToArray();
                    modbusSlave?.DataStore.CoilDiscretes.WritePoints(address, values);
                }
                else if (functionCode == 3)
                {
                    var usdata = Common.ConvertBytesToUShorts(data);
                    modbusSlave?.DataStore.HoldingRegisters.WritePoints(address, usdata);
                }
                else if (functionCode == CTRLFUNCTIONCODE)
                {
                    var usdata = Common.ConvertBytesToUShorts(data);
                    modbusSlave?.DataStore.HoldingRegisters.WritePoints(address, usdata);
                }
                else if (functionCode == CTRLBATCHFUNCTIONCODE)
                {
                    var usdata = Common.ConvertBytesToUShorts(data);
                    modbusSlave?.DataStore.HoldingRegisters.WritePoints(address, usdata);
                }
                else if (functionCode == 4)
                {
                    var usdata = Common.ConvertBytesToUShorts(data);
                    modbusSlave?.DataStore.InputRegisters.WritePoints(address, usdata);
                }
            }
            catch (Exception ex)
            {
                log.Error("往设备" + deviceInfoDto.name + "写入数据失败,地址为" + address + "数据为" + data + "错误原因：" + ex.ToString());
            }
        }

        private List<byte[]> ReadFunc(ushort address, ushort num, int functionCode, byte slaveId)
        {
            // if (functionCode != CTRLFUNCTIONCODE)
            // {
            //     throw new Exception("只支持读取功能码为6的寄存器");
            // }
            if (modbusSlaveNetwork == null) throw new Exception("modbusMaster is null");
            var modbusSlave = modbusSlaveNetwork.GetSlave(slaveId);

            if (functionCode == 1 || functionCode == COILWRITEFUNCTIONCODE)
            {
                var coilRet = new List<byte[]>();
                ushort currentCoilAddress = address;
                while (num > 0)
                {
                    ushort groupCount = Math.Min(num, (ushort)120);
                    bool[] coilData = modbusSlave.DataStore.CoilDiscretes.ReadPoints(currentCoilAddress, groupCount);
                    foreach (var bit in coilData)
                    {
                        coilRet.Add(new byte[] { (byte)(bit ? 1 : 0) });
                    }
                    currentCoilAddress += groupCount;
                    num -= groupCount;
                }
                return coilRet;
            }
            if (functionCode == 2)
            {
                // 暂不支持 FC02（离散输入）读取
                return new List<byte[]>();
            }

            List<ushort> ushorts = new List<ushort>();
            ushort currentAddress = address;

            while (num > 0)
            {
                ushort[] data;
                ushort groupCount = Math.Min(num, (ushort)120);
                if (functionCode == 3 || functionCode == CTRLFUNCTIONCODE || functionCode == CTRLBATCHFUNCTIONCODE)
                {
                    data = modbusSlave.DataStore.HoldingRegisters.ReadPoints(currentAddress, groupCount);
                }
                else if (functionCode == 4)
                {
                    data = modbusSlave.DataStore.InputRegisters.ReadPoints(currentAddress, groupCount);
                }
                else
                {
                    data = new ushort[0];
                }
                ushorts.AddRange(data);
                currentAddress += groupCount;
                num -= groupCount;
            }

            List<byte[]> ret = new List<byte[]>(ushorts.Count);
            for (int i = 0; i < ushorts.Count; i++)
            {
                var objs = BitConverter.GetBytes(ushorts[i]);
                ret.Add(objs);
            }
            return ret;
        }
    }
}
