namespace EssSimulator.LocalControl
{
    /// <summary>emu/standard 单台 PCS 点表槽位（yk2/yk3、yc20–yc46、yt0/yt1/yt3/yt4）。</summary>
    internal sealed class EmuPcsChannel
    {
        public string Fault { get; init; } = "";
        public string Alarm { get; init; } = "";
        public string Status { get; init; } = "";
        public string Freq { get; init; } = "";
        public string Vab { get; init; } = "";
        public string Vbc { get; init; } = "";
        public string Vca { get; init; } = "";
        public string BlackStart { get; init; } = "";
        public string StartStop { get; init; } = "";
        public string ActivePowerSet { get; init; } = "";
        public string ReactivePowerSet { get; init; } = "";
        public string IslandV { get; init; } = "";
        public string IslandF { get; init; } = "";
        public string MeasActivePower { get; init; } = "";
        public string MeasReactivePower { get; init; } = "";

        public static readonly EmuPcsChannel Slot0 = new()
        {
            Fault = "yc46",
            Alarm = "yc45",
            Status = "yc44",
            Freq = "yc23",
            Vab = "yc20",
            Vbc = "yc21",
            Vca = "yc22",
            BlackStart = "yk2",
            StartStop = "yk3",
            ActivePowerSet = "yt0",
            ReactivePowerSet = "yt1",
            IslandV = "yt3",
            IslandF = "yt4",
            MeasActivePower = "yc27",
            MeasReactivePower = "yc28"
        };

        public static EmuPcsChannel? TryProtocol(int flatPcsIndex) =>
            flatPcsIndex >= 0 ? Slot0 : null;
    }
}
