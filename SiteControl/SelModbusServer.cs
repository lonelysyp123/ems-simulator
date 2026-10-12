using EssSimulator.Protocol.Modbus;
using log4net;
using NModbus;
using NModbus.Data;
using NModbus.Device;

namespace EssSimulator.SiteControl;

internal sealed class SelModbusServer : IProtocolLayerServer
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(SelModbusServer));
    private readonly object _gate = new();
    private readonly SelPcsProtocolWriter _writer;
    private readonly ModbusPortHub _hub;
    private readonly TimeProvider _clock;
    private readonly long _startedAt;
    private SelController _controller = new();
    private SlaveDataStore? _store;
    private IModbusSlaveNetwork? _network;
    private SerializedSlave? _slave;
    private byte _functionCode;

    public string ServerName => "simSel";
    public bool IsOnline { get; private set; }
    public int Port { get; private set; }
    public byte SlaveId { get; private set; }
    public int RackCount => 0;
    public ModbusPointMap PointMap { get; } = new("sel.csv", "simSel");

    public SelModbusServer(SelPcsProtocolWriter writer, int port, byte slaveId = 1,
        ModbusPortHub? hub = null, TimeProvider? clock = null)
    {
        _writer = writer;
        Port = port;
        SlaveId = slaveId;
        _hub = hub ?? ModbusPortHub.Instance;
        _clock = clock ?? TimeProvider.System;
        _startedAt = _clock.GetTimestamp();
    }

    public bool Start(int maxRetries = 30)
    {
        lock (_gate)
        {
            if (IsOnline)
                return true;
            var attached = _hub.AttachDevice(Port, SlaveId, ServerName, PointMap.RawMaps[0]);
            if (!attached.Ok)
            {
                Log.Error($"SEL 启动失败：{string.Join("；", attached.Errors)}");
                return false;
            }

            _controller = new SelController();
            _store = attached.DataStore!;
            _store.HoldingRegisters.WritePoints(0, _controller.ReadRegisters(0, SelController.RegisterCount));
            _network = _hub.GetNetwork(Port)!;
            _slave = new SerializedSlave(this, _network.GetSlave(SlaveId));
            _store.HoldingRegisters.BeforeRead += BeforeRead;
            _store.HoldingRegisters.BeforeWrite += BeforeWrite;
            _store.HoldingRegisters.AfterWrite += AfterWrite;
            _network.RemoveSlave(SlaveId);
            _network.AddSlave(_slave);
            IsOnline = true;
            return true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!IsOnline)
                return;
            IsOnline = false;
            _controller.StopRamp();
            _store!.HoldingRegisters.BeforeRead -= BeforeRead;
            _store.HoldingRegisters.BeforeWrite -= BeforeWrite;
            _store.HoldingRegisters.AfterWrite -= AfterWrite;
            if (ReferenceEquals(_network!.GetSlave(SlaveId), _slave))
            {
                _network.RemoveSlave(SlaveId);
                _network.AddSlave(_slave!.Inner);
            }
            _hub.DetachDevice(Port, SlaveId, ServerName);
            _store = null;
            _network = null;
            _slave = null;
        }
    }

    public void Reconfigure(int port, byte slaveId)
    {
        lock (_gate)
        {
            if (IsOnline)
                throw new InvalidOperationException("SEL 必须先停止再调整端口或从站号");
            Port = port;
            SlaveId = slaveId;
        }
    }

    public void RunCycle()
    {
        lock (_gate)
        {
            if (!IsOnline || !_controller.IsRamping)
                return;
            if (!_writer.CanWrite)
            {
                _controller.StopRamp();
                Log.Warn("SEL 升压已停止：PCS 协议不可写或外部控制权被占用");
                return;
            }
            var rollback = _controller.Capture();
            try
            {
                if (_controller.Tick(Now) is { } command)
                    _writer.Write(command);
            }
            catch (Exception ex)
            {
                _controller.Restore(rollback);
                _controller.StopRamp();
                Log.Error("SEL 升压下发失败，已停止斜坡", ex);
            }
        }
    }

    private TimeSpan Now => _clock.GetElapsedTime(_startedAt);

    private bool HasOtherDevices => _hub.GetAttachedDevices(Port)
        .TryGetValue(SlaveId, out var names) && names.Any(n => n != ServerName);

    private bool IsSelRange(ushort address, int count)
    {
        if (address >= SelController.RegisterCount && HasOtherDevices)
            return false;
        if (count == 0 || address + count > SelController.RegisterCount)
            throw new InvalidModbusRequestException(2);
        return true;
    }

    private void BeforeRead(object? sender, PointEventArgs args)
    {
        if (IsSelRange(args.StartAddress, args.NumberOfPoints) && _functionCode != 3)
            throw new InvalidModbusRequestException(1);
    }

    private void BeforeWrite(object? sender, PointEventArgs<ushort> args)
    {
        if (!IsSelRange(args.StartAddress, args.Points.Length))
            return;
        if (_functionCode is not (6 or 16))
            throw new InvalidModbusRequestException(1);
        if (args.StartAddress <= 5 && args.StartAddress + args.Points.Length > 2 && !_writer.CanWrite)
            throw new InvalidModbusRequestException(6);

        var rollback = _controller.Capture();
        var error = _controller.TryWriteRegisters(args.StartAddress, args.Points, Now, out var command);
        if (error != SelWriteError.None)
            throw new InvalidModbusRequestException((byte)error);
        if (command is { } reference)
        {
            try
            {
                _writer.Write(reference);
            }
            catch (Exception ex)
            {
                // 回滚后同值重写仍会被判为变化，否则 EMS 重试将静默丢失。
                _controller.Restore(rollback);
                Log.Error("SEL 下发 PCS 失败，已回滚本次设定", ex);
                throw new InvalidModbusRequestException(6);
            }
        }
    }

    private void AfterWrite(object? sender, PointEventArgs args)
    {
        if (args.StartAddress < SelController.RegisterCount)
            _store!.HoldingRegisters.WritePoints(0, _controller.ReadRegisters(0, SelController.RegisterCount));
    }

    private sealed class SerializedSlave(SelModbusServer owner, NModbus.IModbusSlave inner) : NModbus.IModbusSlave
    {
        public NModbus.IModbusSlave Inner => inner;
        public byte UnitId => inner.UnitId;
        public ISlaveDataStore DataStore => inner.DataStore;

        public IModbusMessage ApplyRequest(IModbusMessage request)
        {
            // BeforeWrite 位于 NModbus 数据锁之外，必须连同提交和定时下发一起串行化。
            lock (owner._gate)
            {
                if (!owner.IsOnline)
                    return inner.ApplyRequest(request);
                if (request.FunctionCode is not (3 or 6 or 16) && !owner.HasOtherDevices)
                    return new ExceptionResponse(request, 1);
                owner._functionCode = request.FunctionCode;
                return inner.ApplyRequest(request);
            }
        }
    }

    private sealed class ExceptionResponse(IModbusMessage request, byte exceptionCode) : IModbusMessage
    {
        public byte FunctionCode { get; set; } = (byte)(request.FunctionCode | 0x80);
        public byte SlaveAddress { get; set; } = request.SlaveAddress;
        public ushort TransactionId { get; set; } = request.TransactionId;
        public byte[] ProtocolDataUnit => new[] { FunctionCode, exceptionCode };
        public byte[] MessageFrame => new[] { SlaveAddress, FunctionCode, exceptionCode };
        public void Initialize(byte[] frame) => throw new NotSupportedException();
    }
}
