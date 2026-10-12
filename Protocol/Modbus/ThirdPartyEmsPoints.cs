namespace EssSimulator.Protocol.Modbus
{
    /// <summary>
    /// 第三方 EMS 使用的 LC SYSTEM 点（与 <c>pointmaps/models/lc/system/lc.csv</c> 对齐）。
    /// </summary>
    public static class ThirdPartyEmsPoints
    {
        public static readonly MapEntry RemoteEnable = Holding("syst4", 4, "u16");
        public static readonly MapEntry RemoteMode = Holding("syst5", 5, "u16");
        public static readonly MapEntry SystemOp = Holding("syst6", 6, "u16");
        public static readonly MapEntry TargetP = Holding("syst1010", 1010, "int16");
        public static readonly MapEntry TargetQ = Holding("syst1011", 1011, "int16");

        public static readonly MapEntry Soc = Input("sysyc104", 104, "u16", scale: 1000);
        public static readonly MapEntry DetailedStatus = Input("sysyc106", 106, "u16");
        public static readonly MapEntry FaultSummary = Input("sysyc107", 107, "u16");
        public static readonly MapEntry MaxChargePower = Input("sysyc113", 113, "u32", scale: 10);
        public static readonly MapEntry MaxDischargePower = Input("sysyc115", 115, "u32", scale: 10);
        public static readonly MapEntry ActivePower = Input("sysyc125", 125, "int32", scale: 10);
        public static readonly MapEntry ReactivePower = Input("sysyc127", 127, "int32", scale: 10);

        public const int OpStart = 3;
        public const int OpStop = 4;
        public const int OpStandby = 5;
        public const int OpReset = 6;

        public static MapEntry[] HoldingPoints { get; } =
        {
            RemoteEnable, RemoteMode, SystemOp, TargetP, TargetQ
        };

        public static MapEntry[] InputPoints { get; } =
        {
            Soc, DetailedStatus, FaultSummary, MaxChargePower, MaxDischargePower, ActivePower, ReactivePower
        };

        private static MapEntry Holding(string name, int address, string type, int scale = 1) =>
            new()
            {
                ParamName = name,
                Address = address,
                FunctionCode = 6,
                Type = type,
                Size = type is "int32" or "u32" ? 32 : 16,
                Scale = scale
            };

        private static MapEntry Input(string name, int address, string type, int scale = 1) =>
            new()
            {
                ParamName = name,
                Address = address,
                FunctionCode = 4,
                Type = type,
                Size = type is "int32" or "u32" ? 32 : 16,
                Scale = scale
            };
    }
}
