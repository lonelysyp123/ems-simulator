using EssSimulator.Configuration;
using EssSimulator.LocalControl;

namespace EssSimulator.Tests.LocalControl;

public class LcBmsMapTests
{
    private static readonly (int Offset, string Type, int Size, int Scale)[] ProtocolPoints =
    [
        (0, "int16", 16, 10),
        (1, "int32", 32, 10),
        (3, "u16", 16, 1000),
        (4, "u16", 16, 1000),
        (5, "u16", 16, 1),
        (6, "u16", 16, 1),
        (7, "u16", 16, 1),
        (8, "u16", 16, 10),
        (9, "u16", 16, 10),
        (10, "u16", 16, 10),
        (11, "u16", 16, 10),
        (12, "u16", 16, 1),
        (13, "u16", 16, 1),
        (14, "u16", 16, 1),
        (15, "int16", 16, 1),
        (16, "u16", 16, 1),
        (17, "u16", 16, 1),
        (18, "u16", 16, 1),
        (19, "int16", 16, 1),
        (20, "int16", 16, 1),
        (21, "u16", 16, 1),
        (22, "u16", 16, 1),
        (23, "u16", 16, 1),
        (24, "int16", 16, 1),
        (25, "u16", 16, 1),
        (26, "u16", 16, 1),
        (27, "u16", 16, 1),
        (28, "int16", 16, 10),
        (29, "int16", 16, 10),
        (30, "u16", 16, 1),
        (31, "u16", 16, 1),
        (32, "u16", 16, 1),
        (33, "u16", 16, 1),
        (34, "u16", 16, 1),
        (35, "u16", 16, 1),
        (36, "u16", 16, 1),
        (37, "u16", 16, 1),
        (38, "u16", 16, 1),
        (39, "u16", 16, 1),
        (60, "u16", 16, 1),
        (61, "u16", 16, 1),
        (62, "u16", 16, 1),
        (71, "u16", 16, 1),
        (72, "u16", 16, 1),
        (73, "u16", 16, 1),
        (74, "u16", 16, 1),
    ];

    [Fact]
    public void PointMap_CoversProtocolBatterySystemSheet()
    {
        Assert.True(File.Exists(CsvPath), CsvPath);
        var g1 = LcPointMapExpander.ExpandFile(CsvPath, 1);
        var g2 = LcPointMapExpander.ExpandFile(CsvPath, 2);
        var names = g1.Select(e => e.ParamName ?? "").ToList();

        Assert.Equal(ProtocolPoints.Length, g1.Count);
        Assert.Equal(ProtocolPoints.Length * 2, g2.Count);
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(g1, e => Assert.Equal(4, e.FunctionCode));

        foreach (var (offset, type, size, scale) in ProtocolPoints)
        {
            int addr = 38000 + offset;
            string name = $"bmsyc{addr}";
            Assert.Contains(g1, e =>
                e.Address == addr && e.ParamName == name && e.Type == type
                && e.Size == size && e.Scale == scale);
            Assert.Contains(g2, e => e.Address == 38200 + offset && e.ParamName == $"bmsyc{38200 + offset}");
        }

        Assert.Contains(g1, e => e.ParamName == "bmsyc38000" && e.Address == 38000);
        Assert.Contains(g1, e => e.ParamName == "bmsyc38001" && e.Type == "int32" && e.Size == 32);
        Assert.Contains(g1, e => e.ParamName == "bmsyc38003" && e.Scale == 1000);
        Assert.Contains(g1, e => e.ParamName == "bmsyc38039");
        Assert.Contains(g1, e => e.ParamName == "bmsyc38060");
        Assert.Contains(g1, e => e.ParamName == "bmsyc38074");
        Assert.DoesNotContain(g1, e => e.Address == 38002);
        Assert.DoesNotContain(g1, e => e.Address == 38040);
        Assert.DoesNotContain(g1, e => e.Address == 38063);
        Assert.DoesNotContain(g1, e => e.Address == 38075);
        Assert.DoesNotContain(g2, e => e.Address == 38400);
    }

    [Fact]
    public void PointMap_ModelSimIsUnbound()
    {
        var rows = LcPointMapExpander.LoadTemplate(CsvPath);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("0", r.ModelSim));
        Assert.All(rows, r => Assert.True(LcAddressExpr.IsGroupScoped(r.Address)));
    }

    [Fact]
    public void SourceParams_ExistInBmsBankCsv()
    {
        var csv = File.ReadAllText(BmsBankCsvPath);
        foreach (var param in LcBmsBankMap.All)
            Assert.Contains($",{param},", csv);
    }

    [Fact]
    public void FromCsv_G2Pro_KeepsKnownParamNames()
    {
        var map = LcBmsBankMap.FromCsv(BmsBankCsvPath);
        Assert.Equal("param39", map.Voltage);
        Assert.Equal("param47", map.Soc);
        Assert.Equal("param69", map.MaxCellVoltage);
        Assert.Equal("param2", map.RunMode);
    }

    [Fact]
    public void FromCsv_G24Mwh_DoesNotUseG2ProParamNumbers()
    {
        var map = LcBmsBankMap.FromCsv(G24MwhBankCsvPath);
        Assert.Equal("param24", map.Voltage);
        Assert.Equal("param26", map.Soc);
        Assert.Equal("param38", map.MaxCellVoltage);
        Assert.Equal("param18", map.RunMode);
        Assert.Equal("param25", map.Current);
        Assert.NotEqual(LcBmsBankMap.Voltage, map.Voltage);
    }

    [Fact]
    public void FromCsv_Standard_UsesYcNamesNotParam()
    {
        var map = LcBmsBankMap.FromCsv(StandardBankCsvPath);
        Assert.Equal("yc9", map.Voltage);
        Assert.Equal("yc11", map.Soc);
        Assert.Equal("yc23", map.MaxCellVoltage);
        Assert.Equal("yc3", map.RunMode);
        Assert.DoesNotContain("param", map.Voltage);
    }

    [Fact]
    public void Collect_CopiesBankRegisters_AndConvertsCellVoltsToMv()
    {
        var bank = new LcBmsBankSnap
        {
            Voltage = 1330.4,
            Current = -41.2,
            Soc = 0.552,
            Soh = 0.98,
            Insulation = 1200,
            ChargeRemainKwh = 800,
            DischargeRemainKwh = 2100,
            MaxChargeCurrent = 55.5,
            MaxDischargeCurrent = 60.1,
            ClusterCurrentDiff = 1.2,
            ClusterVoltageDiff = 3.4,
            MaxCellVoltage = 3.365,
            MinCellVoltage = 3.291,
            AvgCellVoltage = 3.330,
            MaxCellTemp = 31.6,
            MinCellTemp = 24.2,
            AvgCellTemp = 27.8,
            ChargeEnergyKwh = 0x0001_2345,
            DischargeEnergyKwh = 0x00AB_CDEF,
            OnlineClusters = 10,
            TotalClusters = 12,
            MinParallelClusters = 8,
            DischargePowerKw = 2500,
            ChargePowerKw = 2400,
            BlackStartEnter = 1,
            BlackStartStatus = 3,
            GridConnectStatus = 2,
            RunMode = 0,
            ChargeDischarge = 2,
            Heartbeat = 17,
            MaxCellVoltageRack = 3,
            MaxCellVoltagePack = 2,
            MaxCellVoltageCell = 8
        };

        var pts = LcBmsTelemetry.Collect(1, bank).ToDictionary(p => p.Param, p => p.Value);

        Assert.Equal(1330.4, pts[LcBmsMap.TotalVoltage(1)]);
        Assert.Equal(-41.2, pts[LcBmsMap.TotalCurrent(1)]);
        Assert.Equal(0.552, pts[LcBmsMap.Soc(1)]);
        Assert.Equal(0.98, pts[LcBmsMap.Soh(1)]);
        Assert.Equal(3365d, Convert.ToDouble(pts[LcBmsMap.MaxCellVoltage(1)]));
        Assert.Equal(3291d, Convert.ToDouble(pts[LcBmsMap.MinCellVoltage(1)]));
        Assert.Equal(3330d, Convert.ToDouble(pts[LcBmsMap.AvgCellVoltage(1)]));
        Assert.Equal(31.6, pts[LcBmsMap.MaxCellTemp(1)]);
        Assert.Equal(24.2, pts[LcBmsMap.MinCellTemp(1)]);
        Assert.Equal(0x0001, Convert.ToInt32(pts[LcBmsMap.ChargeEnergyHigh(1)]));
        Assert.Equal(0x2345, Convert.ToInt32(pts[LcBmsMap.ChargeEnergyLow(1)]));
        Assert.Equal(0x00AB, Convert.ToInt32(pts[LcBmsMap.DischargeEnergyHigh(1)]));
        Assert.Equal(0xCDEF, Convert.ToInt32(pts[LcBmsMap.DischargeEnergyLow(1)]));
        Assert.Equal(10, pts[LcBmsMap.OnlineClusters(1)]);
        Assert.Equal(1, pts[LcBmsMap.BlackStartMode(1)]);
        Assert.Equal(3, pts[LcBmsMap.BlackStartStatus(1)]);
        Assert.Equal(2, pts[LcBmsMap.GridConnect(1)]);
        Assert.Equal(2, pts[LcBmsMap.ChargeDischarge(1)]);
        Assert.Equal(17, pts[LcBmsMap.Heartbeat(1)]);
        Assert.Equal(3, pts[LcBmsMap.MaxCellVoltageRack(1)]);
        Assert.Equal("bmsyc38000", LcBmsMap.TotalVoltage(1));
        Assert.Equal("bmsyc38200", LcBmsMap.TotalVoltage(2));
    }

    [Fact]
    public void Collect_Group2_UsesBankStride200()
    {
        var pts = LcBmsTelemetry.Collect(2, new LcBmsBankSnap { Voltage = 700 })
            .ToDictionary(p => p.Param, p => p.Value);
        Assert.Equal(700, pts[LcBmsMap.TotalVoltage(2)]);
        Assert.DoesNotContain(LcBmsMap.TotalVoltage(1), pts.Keys);
    }

    [Fact]
    public void Collect_NullBank_WritesZeros()
    {
        var pts = LcBmsTelemetry.Collect(1, null).ToDictionary(p => p.Param, p => p.Value);
        Assert.Equal(0d, Convert.ToDouble(pts[LcBmsMap.TotalVoltage(1)]));
        Assert.Equal(0d, Convert.ToDouble(pts[LcBmsMap.FaultSummary(1)]));
        Assert.Equal(0d, Convert.ToDouble(pts[LcBmsMap.Heartbeat(1)]));
        Assert.Equal(ProtocolPoints.Length, pts.Count);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0, 0)]
    [InlineData(1, 0, 0, 0, 0, 0, 0b0000_0001)]
    [InlineData(0, 3, 0, 0, 0, 0, 0b0000_0010)]
    [InlineData(0, 0, 5, 0, 0, 0, 0b0000_0100)]
    [InlineData(0, 0, 0, 2, 0, 0, 0b0001_0000)]
    [InlineData(1, 2, 4, 1, 9, 7, 0b0110_1111)]
    public void EncodeFaultWord_FromBankSummaries(
        int protection, int alarm, int fault, int runMode, int pcsFault, int dropFault, int expected) =>
        Assert.Equal(expected, LcBmsTelemetry.EncodeFaultWord(
            protection, alarm, fault, runMode, pcsFault, dropFault));

    [Fact]
    public void Collect_ComposesFaultAndDryContactFromBankWords()
    {
        var bank = new LcBmsBankSnap
        {
            ProtectionSummary = 1,
            AlarmSummary = 2,
            FaultSummary = 0,
            RunMode = 1,
            PcsFaultStatus = 0,
            ClusterDropFault = 1,
            OnlineClusters = 10
        };
        var pts = LcBmsTelemetry.Collect(1, bank).ToDictionary(p => p.Param, p => p.Value);
        Assert.Equal(0b0100_1011, Convert.ToInt32(pts[LcBmsMap.FaultSummary(1)]));
        Assert.Equal(0b0000_1110, Convert.ToInt32(pts[LcBmsMap.DryContact(1)]));
    }

    [Fact]
    public void ServerNameForGroup_UsesFirstPcsOfGroup()
    {
        var units = new List<EssUnitConfig>
        {
            new()
            {
                Groups =
                {
                    new EmuGroupConfig { Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig() } },
                    new EmuGroupConfig { Pcs = { new PcsDeviceConfig() } }
                }
            },
            new() { Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig() } }
        };

        Assert.Equal("simBms1", LcBmsLayout.ServerNameForGroup(units, 0, units[0], 1));
        Assert.Equal("simBms3", LcBmsLayout.ServerNameForGroup(units, 0, units[0], 2));
        Assert.Equal("simBms4", LcBmsLayout.ServerNameForGroup(units, 1, units[1], 1));
        Assert.Null(LcBmsLayout.ServerNameForGroup(units, 0, units[0], 3));
    }

    [Fact]
    public void ServerNamesForGroup_ReturnsAllPcsBmsInGroup()
    {
        var units = new List<EssUnitConfig>
        {
            new()
            {
                Groups =
                {
                    new EmuGroupConfig { Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig() } },
                    new EmuGroupConfig { Pcs = { new PcsDeviceConfig() } }
                }
            }
        };

        Assert.Equal(new[] { "simBms1", "simBms2" }, LcBmsLayout.ServerNamesForGroup(units, 0, units[0], 1));
        Assert.Equal(new[] { "simBms3" }, LcBmsLayout.ServerNamesForGroup(units, 0, units[0], 2));
        Assert.Empty(LcBmsLayout.ServerNamesForGroup(units, 0, units[0], 3));
    }

    [Fact]
    public void Aggregate_AveragesVoltageSoc_SumsCurrentEnergyAndOrsFaults()
    {
        var a = new LcBmsBankSnap
        {
            Voltage = 1300,
            Current = 10,
            Soc = 0.4,
            Soh = 0.9,
            ChargeRemainKwh = 100,
            ChargeEnergyKwh = 20,
            DischargePowerKw = 50,
            MaxCellVoltage = 3.6,
            MinCellVoltage = 3.2,
            MaxCellVoltageRack = 1,
            OnlineClusters = 8,
            TotalClusters = 10,
            FaultSummary = 1,
            AlarmSummary = 0,
            Heartbeat = 3
        };
        var b = new LcBmsBankSnap
        {
            Voltage = 1500,
            Current = 30,
            Soc = 0.6,
            Soh = 0.95,
            ChargeRemainKwh = 200,
            ChargeEnergyKwh = 40,
            DischargePowerKw = 70,
            MaxCellVoltage = 3.8,
            MinCellVoltage = 3.1,
            MaxCellVoltageRack = 2,
            OnlineClusters = 9,
            TotalClusters = 10,
            FaultSummary = 0,
            AlarmSummary = 4,
            Heartbeat = 7
        };

        var agg = LcBmsTelemetry.Aggregate(new[] { a, b });
        Assert.Equal(1400d, Convert.ToDouble(agg.Voltage));
        Assert.Equal(40d, Convert.ToDouble(agg.Current));
        Assert.Equal(0.5d, Convert.ToDouble(agg.Soc), 5);
        Assert.Equal(0.925d, Convert.ToDouble(agg.Soh), 5);
        Assert.Equal(300d, Convert.ToDouble(agg.ChargeRemainKwh));
        Assert.Equal(60d, Convert.ToDouble(agg.ChargeEnergyKwh));
        Assert.Equal(120d, Convert.ToDouble(agg.DischargePowerKw));
        Assert.Equal(3.8d, Convert.ToDouble(agg.MaxCellVoltage));
        Assert.Equal(2, Convert.ToInt32(agg.MaxCellVoltageRack));
        Assert.Equal(3.1d, Convert.ToDouble(agg.MinCellVoltage));
        Assert.Equal(17d, Convert.ToDouble(agg.OnlineClusters));
        Assert.Equal(20d, Convert.ToDouble(agg.TotalClusters));
        Assert.Equal(1d, Convert.ToDouble(agg.FaultSummary));
        Assert.Equal(4d, Convert.ToDouble(agg.AlarmSummary));
        Assert.Equal(7d, Convert.ToDouble(agg.Heartbeat));

        var pts = LcBmsTelemetry.Collect(1, agg).ToDictionary(p => p.Param, p => p.Value);
        Assert.Equal(1400d, Convert.ToDouble(pts[LcBmsMap.TotalVoltage(1)]));
        Assert.Equal(40d, Convert.ToDouble(pts[LcBmsMap.TotalCurrent(1)]));
    }

    private static string CsvPath =>
        Path.Combine(FindRepoRoot(), "pointmaps", "models", "lc", "bms", "lc.csv");

    private static string BmsBankCsvPath =>
        Path.Combine(FindRepoRoot(), "pointmaps", "models", "bms", "g2_pro", "bms_bank.csv");

    private static string G24MwhBankCsvPath =>
        Path.Combine(FindRepoRoot(), "pointmaps", "models", "bms", "g2_4mwh", "bms_bank.csv");

    private static string StandardBankCsvPath =>
        Path.Combine(FindRepoRoot(), "pointmaps", "models", "bms", "standard", "bms_bank.csv");

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
