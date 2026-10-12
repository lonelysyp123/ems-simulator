using EssSimulator.DataExchange.Adapters;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.DataExchange;

public class ModbusRegisterAdapterTests
{
    private static readonly MapEntry Point = new()
    {
        FunctionCode = 6,
        Address = 5039,
        Type = "u16",
        Size = 16,
        ParamName = "yt0",
        Scale = 10
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultReadsAndWrites_FollowCurrentBankAfterReconfigure(bool usePointStore)
    {
        var (device, slave, adapter) = Create(usePointStore);
        foreach (byte bankId in new byte[] { 1, 7, 247, 1 })
        {
            device.slaveId = bankId;
            adapter.WriteDefaults(new Dictionary<string, object> { ["yt0"] = 12.3 });
            Assert.Equal(bankId, slave.LastWriteSlaveId);
            Assert.Equal(12.3, Convert.ToDouble(ModbusPointCodec.Decode(slave.Read("yt0"), Point)), 6);

            adapter.WritePoints(new Dictionary<string, object> { ["yt0"] = 45.6 });
            Assert.Equal(bankId, slave.LastWriteSlaveId);
            var raw = adapter.ReadAllControlRaw(new[] { "yt0" });
            Assert.Equal(bankId, slave.LastReadSlaveId);
            Assert.Equal(45.6, Convert.ToDouble(ModbusPointCodec.Decode((byte[])raw["yt0"], Point)), 6);
            Assert.Equal(45.6, Convert.ToDouble(adapter.ReadParsedPoint("yt0")), 6);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitSlaveIds_RemainAbsoluteAndReadTheirOwnRegisters(bool usePointStore)
    {
        var (device, slave, adapter) = Create(usePointStore);
        foreach (byte bankId in new byte[] { 7, 9 })
        {
            device.slaveId = bankId;
            foreach (byte slaveId in new byte[] { 1, 2, 8 })
            {
                adapter.WritePoints(new Dictionary<string, object> { ["yt0"] = slaveId * 10d }, slaveId);
                Assert.Equal(slaveId, slave.LastWriteSlaveId);
            }

            foreach (byte slaveId in new byte[] { 1, 2, 8 })
            {
                var raw = adapter.ReadAllControlRaw(new[] { "yt0" }, slaveId);
                Assert.Equal(slaveId, slave.LastReadSlaveId);
                Assert.Equal(slaveId * 10d, Convert.ToDouble(ModbusPointCodec.Decode((byte[])raw["yt0"], Point)), 6);
                Assert.Equal(slaveId * 10d, Convert.ToDouble(adapter.ReadParsedPoint("yt0", slaveId)), 6);
                Assert.Equal(slaveId, slave.LastReadSlaveId);
            }
        }
    }

    private static (DeviceInfoDto Device, RecordingSlave Slave, IModbusRegisterAdapter Adapter) Create(bool usePointStore)
    {
        var device = new DeviceInfoDto { slaveId = 1 };
        var slave = new RecordingSlave(device);
        var parser = new ModbusParser(new List<MapEntry[]> { new[] { Point } });
        IModbusRegisterAdapter adapter = new ModbusRegisterAdapter(slave, parser, device);
        if (usePointStore)
            adapter = new ProtocolPointStore(adapter);
        return (device, slave, adapter);
    }

    private sealed class RecordingSlave(DeviceInfoDto device) : IModbusSlave
    {
        private readonly Dictionary<byte, Dictionary<string, object>> _registers = new();
        public byte LastWriteSlaveId { get; private set; }
        public byte LastReadSlaveId { get; private set; }

        public void DeviceConnect() { }
        public void DeviceDisconnect() { }
        public bool GetCommunicatorState() => true;

        public Dictionary<string, object>? Read(byte slaveId = 1)
        {
            LastReadSlaveId = slaveId;
            return _registers.GetValueOrDefault(slaveId);
        }

        public byte[] Read(string paramName) => (byte[])Read(device.slaveId)![paramName];

        public bool Write(Dictionary<string, object> data, byte slaveId = 1, bool applyScale = true)
        {
            LastWriteSlaveId = slaveId;
            if (!_registers.TryGetValue(slaveId, out var registers))
                _registers[slaveId] = registers = new Dictionary<string, object>();
            foreach (var pair in data)
                registers[pair.Key] = ModbusPointCodec.Encode(pair.Value, Point, applyScale);
            return true;
        }
    }
}
