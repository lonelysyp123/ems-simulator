namespace EssSimulator.Web.ThirdPartyEms
{
    public sealed class ThirdPartyEmsTarget
    {
        public string Name { get; init; } = "";
        public int UnitIndex { get; init; }
    }

    public sealed class ThirdPartyEmsDashboard
    {
        public bool Available { get; set; }
        /// <summary>仅当闸门所有者为第三方 EMS 时为 true。</summary>
        public bool Exclusive { get; set; }
        /// <summary><c>None</c> / <c>EmsStrategy</c> / <c>ThirdPartyEms</c>。</summary>
        public string GateOwner { get; set; } = "None";
        public string? Reason { get; set; }
        public ThirdPartyEmsStationTotals Station { get; set; } = new();
        public List<ThirdPartyEmsUnitSnapshot> Units { get; set; } = new();
    }

    public sealed class ThirdPartyEmsStationTotals
    {
        public double? ActivePowerKw { get; set; }
        public double? ReactivePowerKvar { get; set; }
        public int ConnectedCount { get; set; }
        public int UnitCount { get; set; }
    }

    public sealed class ThirdPartyEmsUnitSnapshot
    {
        public string Name { get; set; } = "";
        public int UnitIndex { get; set; }
        public string ModelPath { get; set; } = "";
        public bool Connected { get; set; }
        public bool Live { get; set; }
        public string? LastError { get; set; }
        public string? LastWrite { get; set; }
        public bool? LastWriteOk { get; set; }
        public DateTime? LastWriteUtc { get; set; }

        public double? RemoteEnable { get; set; }
        public double? RemoteMode { get; set; }
        public double? TargetActivePowerKw { get; set; }
        public double? TargetReactivePowerKvar { get; set; }
        public double? ActivePowerKw { get; set; }
        public double? ReactivePowerKvar { get; set; }
        public double? Soc { get; set; }
        public double? DetailedStatus { get; set; }
        public double? FaultSummary { get; set; }
        public double? MaxChargePowerKw { get; set; }
        public double? MaxDischargePowerKw { get; set; }
    }

    public sealed class ThirdPartyEmsPowerRequest
    {
        public string? Name { get; set; }
        public double ActivePowerKw { get; set; }
        public double ReactivePowerKvar { get; set; }
    }

    public sealed class ThirdPartyEmsConnectRequest
    {
        public string? Name { get; set; }
    }

    public sealed class ThirdPartyEmsRemoteRequest
    {
        public string? Name { get; set; }
        public int Enable { get; set; } = 1;
        public int Mode { get; set; } = 1;
    }

    public sealed class ThirdPartyEmsOperationRequest
    {
        public string? Name { get; set; }
        public int Operation { get; set; }
    }
}
