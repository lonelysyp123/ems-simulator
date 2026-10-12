using EssSimulator.LocalControl;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.LocalControl;

public class LcSystemMapTests
{
    [Fact]
    public void ParamNames_ExistInExpandedCsv()
    {
        var names = LcPointMapExpander.ExpandFile(CsvPath(), 1)
            .Select(e => e.ParamName)
            .ToHashSet();

        Assert.Contains(LcSystemMap.Soc, names);
        Assert.Contains(LcSystemMap.FaultSummary, names);
        Assert.Contains(LcSystemMap.RunStateSummary, names);
        Assert.Contains(LcSystemMap.MaxChargePower, names);
        Assert.Contains(LcSystemMap.ApparentPower, names);
        Assert.Contains(LcSystemMap.BlackStart, names);
        Assert.Contains(LcSystemMap.HvBreaker, names);
        Assert.Contains(LcSystemMap.PcsTotal, names);
        Assert.Contains(LcSystemMap.RemoteEnable, names);
        Assert.Contains(LcSystemMap.BlackStartWrite, names);
        Assert.Contains(LcSystemMap.TargetP, names);
        Assert.Contains(LcSystemMap.TargetQ, names);
        Assert.Contains(LcSystemMap.NominalPower, names);
        Assert.Contains(LcSystemMap.NominalEnergy, names);
        Assert.Contains(LcSystemMap.Soh, names);
        Assert.Contains(LcSystemMap.DetailedStatus, names);
        Assert.Contains(LcSystemMap.ChargeEnergy, names);
        Assert.Contains(LcSystemMap.DischargeEnergy, names);
        Assert.Contains(LcSystemMap.PcsSummary, names);
        Assert.Contains(LcSystemMap.BmsTotal, names);
        Assert.Contains(LcSystemMap.Word218, names);
        Assert.Contains(LcSystemMap.Word228, names);
        Assert.Contains(LcSystemMap.Restart, names);
    }

    [Fact]
    public void EmuControls_CoverFormerModelSimWrites()
    {
        var fields = LcSystemMap.EmuControls.Select(c => c.EmuField).ToHashSet();
        Assert.Contains("RemoteControlEnable", fields);
        Assert.Contains("RemoteControlMode", fields);
        Assert.Contains("SystemOperation", fields);
        Assert.Contains("BlackStartModeWrite", fields);
        Assert.Contains("TargetActivePower", fields);
        Assert.Contains("TargetReactivePower", fields);
        Assert.Equal(6, LcSystemMap.EmuControls.Length);
    }

    private static string CsvPath() =>
        Path.Combine(FindRepoRoot(), "pointmaps", "models", "lc", "system", "lc.csv");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EssSimulator.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("找不到仓库根目录");
    }
}

public class LcSystemTelemetryTests
{
    [Fact]
    public void Sysyc104_PcsMapperPercentSoc_MustNotSaturateU16()
    {
        // PcsMapper 把 MinClusterSOC(0-1)×100 写入 AverageBatterySoc，例如 80.5。
        // sysyc104 为 u16 Scale=1000：工程值必须是 0-1，否则 80.5×1000 钳成 65535。
        var emu = new LcSystemEmuSnap(80.5, 2750, 2750, 180, -20, 0xAA);
        var pts = LcSystemTelemetry.Collect(emu, Array.Empty<LcSystemPcsSnap>(), default)
            .ToDictionary(p => p.Param, p => p.Value);
        var entry = Sysyc104Entry();
        var encoded = ModbusPointCodec.Encode(pts[LcSystemMap.Soc], entry, applyScale: true);
        double decoded = Convert.ToDouble(ModbusPointCodec.Decode(encoded, entry));

        Assert.InRange(decoded, 0.80, 0.81);
        Assert.True(decoded < 1.01, $"SOC 编码后为 {decoded}，说明仍按百分数×1000 饱和");
    }

    [Theory]
    [InlineData(80.5, 0.805)]
    [InlineData(100, 1.0)]
    [InlineData(0.85, 0.85)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public void NormalizeSoc_AcceptsPercentOrFraction(double input, double expected) =>
        Assert.Equal(expected, LcSystemTelemetry.NormalizeSoc(input), 5);

    [Fact]
    public void Collect_MapsEmuFieldsAndFirstPcs()
    {
        var emu = new LcSystemEmuSnap(80.5, 2750, 2750, 180, -20, 0xAA);
        var pcs = new[]
        {
            new LcSystemPcsSnap(0.99, 100, 90, 10, 50.1, 700),
            new LcSystemPcsSnap(0.98, 110, 80, 12, 50.0, 710)
        };
        var plugins = new LcSystemPluginSnap(1, 3, 1, 2, 1, 0, 1);

        var pts = LcSystemTelemetry.Collect(emu, pcs, plugins)
            .ToDictionary(p => p.Param, p => p.Value);

        Assert.Equal(0.805, Convert.ToDouble(pts[LcSystemMap.Soc]), 5);
        Assert.Equal(2750, pts[LcSystemMap.MaxChargePower]);
        Assert.Equal(2750, pts[LcSystemMap.MaxDischargePower]);
        Assert.Equal(180, pts[LcSystemMap.ActivePower]);
        Assert.Equal(-20, pts[LcSystemMap.ReactivePower]);
        Assert.Equal(0.99, pts[LcSystemMap.PowerFactor]);
        Assert.Equal(50.1, pts[LcSystemMap.Frequency]);
        Assert.Equal(700, pts[LcSystemMap.DcVoltage]);
        Assert.Equal(0xAA, pts[LcSystemMap.HvBreaker]);
        Assert.Equal(1, pts[LcSystemMap.FaultSummary]);
        Assert.Equal(3, pts[LcSystemMap.RunStateSummary]);
        Assert.Equal(1, pts[LcSystemMap.BlackStart]);
        Assert.Equal(2, pts[LcSystemMap.PcsTotal]);
        Assert.Equal(1, pts[LcSystemMap.PcsRunning]);
        Assert.Equal(0, pts[LcSystemMap.PcsAlarm]);
        Assert.Equal(1, pts[LcSystemMap.PcsFault]);
    }

    [Fact]
    public void Collect_SumsActualPcsCount_NotHardcodedEight()
    {
        var emu = default(LcSystemEmuSnap);
        var two = new[]
        {
            new LcSystemPcsSnap(1, 100, 40, 5, 50, 700),
            new LcSystemPcsSnap(1, 50, 30, 4, 50, 700)
        };
        var eight = Enumerable.Range(0, 8)
            .Select(_ => new LcSystemPcsSnap(1, 10, 3, 1, 50, 700))
            .ToArray();
        var plugins = default(LcSystemPluginSnap);

        var twoPts = LcSystemTelemetry.Collect(emu, two, plugins)
            .ToDictionary(p => p.Param, p => p.Value);
        var eightPts = LcSystemTelemetry.Collect(emu, eight, plugins)
            .ToDictionary(p => p.Param, p => p.Value);

        Assert.Equal(150d, Convert.ToDouble(twoPts[LcSystemMap.ApparentPower]));
        Assert.Equal(70d, Convert.ToDouble(twoPts[LcSystemMap.DcPower]));
        Assert.Equal(9d, Convert.ToDouble(twoPts[LcSystemMap.DcCurrent]));
        Assert.Equal(80d, Convert.ToDouble(eightPts[LcSystemMap.ApparentPower]));
        Assert.Equal(24d, Convert.ToDouble(eightPts[LcSystemMap.DcPower]));
    }

    [Fact]
    public void Collect_NoPcs_WritesZerosAndBreakerReset()
    {
        var pts = LcSystemTelemetry.Collect(default, Array.Empty<LcSystemPcsSnap>(), default)
            .ToDictionary(p => p.Param, p => p.Value);

        Assert.Equal(0d, Convert.ToDouble(pts[LcSystemMap.Soc]));
        Assert.Equal(0d, Convert.ToDouble(pts[LcSystemMap.ApparentPower]));
        Assert.Equal(0d, Convert.ToDouble(pts[LcSystemMap.PowerFactor]));
        Assert.Equal(0xEE, Convert.ToInt32(pts[LcSystemMap.HvBreaker]));
        Assert.Equal(0d, Convert.ToDouble(pts[LcSystemMap.FaultSummary]));
        Assert.Equal(0d, Convert.ToDouble(pts[LcSystemMap.NominalPower]));
        Assert.Equal(0, Convert.ToInt32(pts[LcSystemMap.DetailedStatus]));
        Assert.Equal(0, Convert.ToInt32(pts[LcSystemMap.Word219]));
    }

    [Fact]
    public void Collect_CapacityEnergySoh_FromPcsAndBms()
    {
        var pcs = new[]
        {
            new LcSystemPcsSnap(1, 0, 0, 0, 50, 700, RatedPower: 2750),
            new LcSystemPcsSnap(1, 0, 0, 0, 50, 700, RatedPower: 2750)
        };
        var bms = new[]
        {
            new LcSystemBmsSnap(2500, 0.96, 500, 2000, 0, 0, 0),
            new LcSystemBmsSnap(2500, 0.94, 400, 2100, 0, 0, 0)
        };

        var pts = LcSystemTelemetry.Collect(default, pcs, default, bms, Array.Empty<LcSystemGroupSnap>(), live: true)
            .ToDictionary(p => p.Param, p => p.Value);

        Assert.Equal(5500d, Convert.ToDouble(pts[LcSystemMap.NominalPower]));
        Assert.Equal(5000d, Convert.ToDouble(pts[LcSystemMap.NominalEnergy]));
        Assert.Equal(0.95d, Convert.ToDouble(pts[LcSystemMap.Soh]), 5);
        Assert.Equal(900d, Convert.ToDouble(pts[LcSystemMap.ChargeEnergy]));
        Assert.Equal(4100d, Convert.ToDouble(pts[LcSystemMap.DischargeEnergy]));
    }

    [Fact]
    public void Collect_DetailedStatusAndPcsSummary_FollowSimStates()
    {
        var stopped = new[] { Pcs(1), Pcs(1) };
        var standby = new[] { Pcs(2), Pcs(2) };
        var running = new[] { Pcs(2), Pcs(5) };
        var faulted = new[] { Pcs(5), Pcs(6) };
        var alarmed = new[] { Pcs(5, alarm: 1) };

        Assert.Equal(1, StatusOf(stopped)[LcSystemMap.DetailedStatus]);
        Assert.Equal(1, StatusOf(stopped)[LcSystemMap.PcsSummary]);
        Assert.Equal(4, StatusOf(standby)[LcSystemMap.DetailedStatus]);
        Assert.Equal(2, StatusOf(standby)[LcSystemMap.PcsSummary]);
        Assert.Equal(3, StatusOf(running)[LcSystemMap.DetailedStatus]);
        Assert.Equal(3, StatusOf(running)[LcSystemMap.PcsSummary]);
        Assert.Equal(5, StatusOf(faulted)[LcSystemMap.DetailedStatus]);
        Assert.Equal(4, StatusOf(faulted)[LcSystemMap.PcsSummary]);
        Assert.Equal(6, StatusOf(alarmed)[LcSystemMap.DetailedStatus]);
        Assert.Equal(3, StatusOf(alarmed)[LcSystemMap.PcsSummary]);
    }

    [Fact]
    public void Collect_BmsCounts_FaultExcludesRunning()
    {
        var bms = new[]
        {
            new LcSystemBmsSnap(1, 1, 0, 0, 0, 0, 0),
            new LcSystemBmsSnap(1, 1, 0, 0, 0, 0, 4),
            new LcSystemBmsSnap(1, 1, 0, 0, 1, 0, 0),
            new LcSystemBmsSnap(1, 1, 0, 0, 0, 8, 0)
        };
        var pts = LcSystemTelemetry.Collect(default, Array.Empty<LcSystemPcsSnap>(), default, bms, Array.Empty<LcSystemGroupSnap>(), live: true)
            .ToDictionary(p => p.Param, p => p.Value);

        Assert.Equal(4, pts[LcSystemMap.BmsTotal]);
        Assert.Equal(2, pts[LcSystemMap.BmsRunning]);
        Assert.Equal(1, pts[LcSystemMap.BmsAlarm]);
        Assert.Equal(1, pts[LcSystemMap.BmsFault]);
    }

    [Fact]
    public void Collect_CommsWords_FollowActualPcsAndGroups()
    {
        var twoPcs = new[] { Pcs(5), Pcs(5) };
        var groupsOk = new[] { new LcSystemGroupSnap(true, false) };
        var two = LcSystemTelemetry.Collect(default, twoPcs, default, new[] { default(LcSystemBmsSnap), default }, groupsOk, live: true)
            .ToDictionary(p => p.Param, p => p.Value);

        Assert.Equal(0b11, two[LcSystemMap.Word218]);
        Assert.Equal(0b11, two[LcSystemMap.Word228]);
        Assert.Equal(0b11, two[LcSystemMap.Word220]);
        Assert.Equal(1, two[LcSystemMap.Word221]);
        Assert.Equal(0, two[LcSystemMap.Word222]);
        Assert.Equal((1 << 8) | (1 << 10), two[LcSystemMap.Word219]);
        Assert.Equal(0, two[LcSystemMap.Word223]);
        Assert.Equal(0, two[LcSystemMap.Word226]);

        var eightPcs = Enumerable.Range(0, 8).Select(_ => Pcs(1)).ToArray();
        var eightBms = Enumerable.Range(0, 8).Select(_ => default(LcSystemBmsSnap)).ToArray();
        var eight = LcSystemTelemetry.Collect(default, eightPcs, default, eightBms, new[] { new LcSystemGroupSnap(true, true) }, live: true)
            .ToDictionary(p => p.Param, p => p.Value);
        Assert.Equal(0xFF, eight[LcSystemMap.Word218]);
        Assert.Equal(0, eight[LcSystemMap.Word228]);
        Assert.Equal(0, eight[LcSystemMap.Word221]);
    }

    [Theory]
    [InlineData(0xAA, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void IsRestartPulse_OnlyAa(double value, bool expected) =>
        Assert.Equal(expected, LcSystemTelemetry.IsRestartPulse(value));

    [Theory]
    [InlineData(1, 0xAA)]
    [InlineData(0xAA, 0xAA)]
    [InlineData(0, 0xEE)]
    [InlineData(0xEE, 0xEE)]
    public void EncodeHvStatus_OnlyAaOrEe(int closed, int expected) =>
        Assert.Equal(expected, LcSystemTelemetry.EncodeHvStatus(closed));

    [Fact]
    public void EncodeHvStatus_NullIsEe() =>
        Assert.Equal(0xEE, LcSystemTelemetry.EncodeHvStatus(null));

    [Fact]
    public void ApplyStartupDefaults_HvBreakerIsAaNotZero()
    {
        var buf = new Dictionary<string, object> { [LcSystemMap.HvBreaker] = 0 };
        LcSystemMap.ApplyStartupDefaults(buf);
        Assert.Equal((ushort)0xAA, buf[LcSystemMap.HvBreaker]);
    }

    private static MapEntry Sysyc104Entry()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "EssSimulator.sln")))
            dir = dir.Parent;
        var path = Path.Combine(dir!.FullName, "pointmaps", "models", "lc", "system", "lc.csv");
        var row = LcPointMapExpander.LoadTemplate(path)
            .First(r => r.ParamName == LcSystemMap.Soc);
        return new MapEntry
        {
            FunctionCode = row.FunctionCode,
            Address = int.Parse(row.Address),
            Type = row.Type,
            Size = row.Size,
            ParamName = row.ParamName,
            Scale = row.Scale
        };
    }

    private static LcSystemPcsSnap Pcs(int status, int alarm = 0) =>
        new(0, 0, 0, 0, 0, 0, 0, status, alarm);

    private static Dictionary<string, object> StatusOf(LcSystemPcsSnap[] pcs) =>
        LcSystemTelemetry.Collect(default, pcs, default)
            .ToDictionary(p => p.Param, p => p.Value);
}
