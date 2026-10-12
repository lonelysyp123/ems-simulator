namespace EssSimulator.EssDeviceSimModel.Pv
{
    /// <summary>PID100（转发 ID 9/10）。健康默认绝缘正常、抑制模式。</summary>
    public sealed class PvPidDevice
    {
        public const ushort ModeOff = 0x55;
        public const ushort ModeSuppress = 0xAA;
        public const ushort ModeRecover = 0xBB;
        public const ushort ModeIso = 0xCC;
        public const ushort ModeDebug = 0xDD;

        public string DeviceId { get; }
        public uint InsulationKohm { get; set; } = 20000;
        public ushort OutputVoltageV { get; private set; }
        public ushort OutputCurrentMa { get; private set; }
        public double InteriorTemperatureC { get; private set; }
        public ushort FaultStatus { get; set; }
        public ushort AlarmStatus { get; set; }
        public ushort WorkingMode { get; set; } = ModeSuppress;

        public PvPidDevice(string deviceId) => DeviceId = deviceId;

        public void Update(double ambientC)
        {
            InteriorTemperatureC = ambientC + 8;
            bool on = WorkingMode is ModeSuppress or ModeRecover or ModeIso or ModeDebug;
            OutputVoltageV = (ushort)(on ? 400 : 0);
            OutputCurrentMa = (ushort)(on ? 20 : 0);
        }
    }
}
