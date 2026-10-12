namespace EssSimulator.EssDeviceSimModel.Pv
{
    /// <summary>中压扩展 I/O（12DI+4DO）。两台接线不同，用命名属性区分。</summary>
    public sealed class PvMvIoModule
    {
        public string DeviceId { get; }
        public ushort[] Inputs { get; } = new ushort[12];
        public ushort[] Outputs { get; } = new ushort[4];

        public PvMvIoModule(string deviceId) => DeviceId = deviceId;

        public static PvMvIoModule CreateModule1(string deviceId)
        {
            var m = new PvMvIoModule(deviceId);
            m.Inputs[0] = 1; // 负荷开关1 合
            m.Inputs[1] = 1;
            m.Inputs[2] = 1; // VCB 合
            m.Inputs[3] = 1; // 隔离 合
            m.Inputs[6] = 0; // 接地刀分
            m.Inputs[7] = 0;
            m.Inputs[8] = 0;
            m.Inputs[9] = 0; // UPS 正常
            m.Inputs[10] = 0;
            m.Inputs[11] = 1; // UPS 开
            return m;
        }

        public static PvMvIoModule CreateModule2(string deviceId)
        {
            var m = new PvMvIoModule(deviceId);
            m.Inputs[0] = 1; // 所用变温 正常
            m.Inputs[1] = 1; // SPD3 正常
            m.Inputs[2] = 1; // QFB 合
            m.Inputs[3] = 1; // SPD2 正常
            m.Inputs[5] = 1; // 辅房烟感 正常
            m.Inputs[10] = 0; // 急停 正常
            return m;
        }
    }
}
