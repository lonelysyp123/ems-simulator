using EssSimulator.LocalControl;

namespace EssSimulator.Tests.LocalControl;

public class LcUnitMapTests
{
    [Theory]
    [InlineData(1, 0, "unit_param0")]
    [InlineData(1, 6, "unit_param6")]
    [InlineData(2, 0, "unit_param600")]
    [InlineData(2, 231, "unit_param831")]
    public void Param_UsesGroupStride600(int n, int offset, string expected) =>
        Assert.Equal(expected, LcUnitMap.Param(n, offset));

    [Theory]
    [InlineData(1, 0, 2600)]
    [InlineData(1, 6, 2606)]
    [InlineData(2, 0, 2900)]
    [InlineData(2, 231, 3131)]
    public void Address_UsesBase2600AndStride300(int n, int offset, int expected) =>
        Assert.Equal(expected, LcUnitMap.Address(n, offset));

    [Fact]
    public void ModuleAndGroupNames_ExistInExpandedCsv()
    {
        var path = Path.Combine(FindRepoRoot(), "pointmaps", "models", "lc", "unit_10MW", "lc.csv");
        var names = LcPointMapExpander.ExpandFile(path, 2)
            .Select(e => e.ParamName)
            .ToHashSet();

        Assert.Contains(LcUnitMap.BatteryVoltage(1, 0), names);
        Assert.Contains(LcUnitMap.BatteryVoltage(1, 1), names);
        Assert.Contains(LcUnitMap.BatteryVoltage(2, 0), names);
        Assert.Contains(LcUnitMap.WarningWord1(2, 1), names);
        Assert.Contains(LcUnitMap.AcCurrentR(1), names);
        Assert.Contains(LcUnitMap.OverTempNtc(2), names);
        Assert.Contains(LcUnitMap.RatedCapacity(1), names);
    }

    [Fact]
    public void ExpandedCsv_UsesAddressBase2600AndStride300()
    {
        var path = Path.Combine(FindRepoRoot(), "pointmaps", "models", "lc", "unit_10MW", "lc.csv");
        var entries = LcPointMapExpander.ExpandFile(path, 2);
        Assert.Contains(entries, e => e.ParamName == LcUnitMap.BatteryVoltage(1, 0) && e.Address == LcUnitMap.Address(1, 0));
        Assert.Contains(entries, e => e.ParamName == LcUnitMap.BatteryVoltage(2, 0) && e.Address == LcUnitMap.Address(2, 0));
        Assert.Contains(entries, e => e.ParamName == LcUnitMap.WarningWord1(1, 0) && e.Address == LcUnitMap.Address(1, 231));
        Assert.DoesNotContain(entries, e => e.Address == 5000);
    }

    [Fact]
    public void PointMap_ModelSimIsUnbound()
    {
        var path = Path.Combine(FindRepoRoot(), "pointmaps", "models", "lc", "unit_10MW", "lc.csv");
        var rows = LcPointMapExpander.LoadTemplate(path);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("0", r.ModelSim));
    }

    [Fact]
    public void FiveFiveMw_AddressBase5000_PairCount1()
    {
        Assert.Equal(5000, LcUnitMap.FiveFiveMw.AddressBase);
        Assert.Equal(1, LcUnitMap.FiveFiveMw.PairCount);
        Assert.Equal("unit1_param0", LcUnitMap.FiveFiveMw.Param(1, 0));
        Assert.Equal(5000, LcUnitMap.FiveFiveMw.Address(1, 0));
        Assert.Equal(2, LcUnitMap.TenMw.PairCount);
    }

    [Fact]
    public void FiveFiveMw_ExpandedCsv_OnlyN1()
    {
        var path = Path.Combine(FindRepoRoot(), "pointmaps", "models", "lc", "unit_5.5MW", "lc.csv");
        var entries = LcPointMapExpander.ExpandFile(path, 1);
        Assert.Contains(entries, e => e.ParamName == "unit1_param0" && e.Address == 5000);
        Assert.DoesNotContain(entries, e => e.Address == 5600);
        Assert.DoesNotContain(entries, e => e.Address == 2600);
    }

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

public class LcUnitTelemetryTests
{
    [Fact]
    public void Collect_Group2_MapsModulesAndAggregates()
    {
        var m1 = new LcUnitModuleSnap(700, 11, 690, 690, 690, 1, 2, 3, 100, 40, 50, 2750, 3, 4, 50, 10);
        var m2 = new LcUnitModuleSnap(710, 12, 688, 688, 688, 4, 5, 6, 80, 55, 60, 2750, 7, 8, 80, 5);

        var pts = LcUnitTelemetry.Collect(2, m1, m2)
            .ToDictionary(p => p.Param, p => p.Value);

        Assert.Equal(700, pts[LcUnitMap.BatteryVoltage(2, 0)]);
        Assert.Equal(710, pts[LcUnitMap.BatteryVoltage(2, 1)]);
        Assert.Equal(690d, Convert.ToDouble(pts[LcUnitMap.BusVoltage(2, 0)]), 6);
        Assert.Equal(688d, Convert.ToDouble(pts[LcUnitMap.BusVoltage(2, 1)]), 6);
        Assert.Equal(1d, Convert.ToDouble(pts[LcUnitMap.InductorR(2, 0)]));
        Assert.Equal(2d, Convert.ToDouble(pts[LcUnitMap.InductorS(2, 0)]));
        Assert.Equal(3d, Convert.ToDouble(pts[LcUnitMap.InductorT(2, 0)]));
        Assert.Equal(100, pts[LcUnitMap.BatteryPower(2, 0)]);
        Assert.Equal(80, pts[LcUnitMap.BatteryPower(2, 1)]);
        Assert.Equal(180d, Convert.ToDouble(pts[LcUnitMap.BatteryPowerTotal(2)]));
        Assert.Equal(55d, Convert.ToDouble(pts[LcUnitMap.OverTempNtc(2)]));
        Assert.Equal(110d, Convert.ToDouble(pts[LcUnitMap.AvailableCapacity(2)]));
        Assert.Equal(5500d, Convert.ToDouble(pts[LcUnitMap.RatedCapacity(2)]));
        Assert.Equal(5d, Convert.ToDouble(pts[LcUnitMap.AcCurrentR(2)]));
        Assert.Equal(7d, Convert.ToDouble(pts[LcUnitMap.AcCurrentS(2)]));
        Assert.Equal(9d, Convert.ToDouble(pts[LcUnitMap.AcCurrentT(2)]));
        Assert.Equal(130d, Convert.ToDouble(pts[LcUnitMap.GridActivePower(2)]));
        Assert.Equal(15d, Convert.ToDouble(pts[LcUnitMap.GridReactivePower(2)]));
        Assert.Equal(689d, Convert.ToDouble(pts[LcUnitMap.GridVoltageRs(2)]));
        Assert.Equal(689d, Convert.ToDouble(pts[LcUnitMap.GridVoltageSt(2)]));
        Assert.Equal(689d, Convert.ToDouble(pts[LcUnitMap.GridVoltageTr(2)]));
        Assert.Equal(3, pts[LcUnitMap.WarningWord1(2, 0)]);
        Assert.Equal(8, pts[LcUnitMap.WarningWord2(2, 1)]);
    }

    [Fact]
    public void Collect_FiveFiveMw_WritesUnit1Param()
    {
        var m1 = new LcUnitModuleSnap(700, 11, 690, 690, 690, 1, 2, 3, 100, 40, 50, 2750, 3, 4, 50, 10);
        var pts = LcUnitTelemetry.Collect(LcUnitMap.FiveFiveMw, 1, m1, null)
            .ToDictionary(p => p.Param, p => p.Value);

        Assert.Equal(700, pts[LcUnitMap.FiveFiveMw.BatteryVoltage(1, 0)]);
        Assert.Equal(0, pts[LcUnitMap.FiveFiveMw.BatteryVoltage(1, 1)]);
        Assert.Equal(100d, Convert.ToDouble(pts[LcUnitMap.FiveFiveMw.BatteryPowerTotal(1)]));
        Assert.DoesNotContain(pts.Keys, k => k.StartsWith("unit_param", StringComparison.Ordinal));
    }

    [Fact]
    public void Collect_MissingModule2_WritesZerosAndDoesNotUseItsPower()
    {
        var m1 = new LcUnitModuleSnap(700, 0, 0, 0, 0, 0, 0, 0, 100, 40, 50, 2750, 0, 0, 40, -8);
        var pts = LcUnitTelemetry.Collect(1, m1, null)
            .ToDictionary(p => p.Param, p => p.Value);

        Assert.Equal(0, pts[LcUnitMap.BatteryVoltage(1, 1)]);
        Assert.Equal(0d, Convert.ToDouble(pts[LcUnitMap.BusVoltage(1, 1)]));
        Assert.Equal(100d, Convert.ToDouble(pts[LcUnitMap.BatteryPowerTotal(1)]));
        Assert.Equal(40d, Convert.ToDouble(pts[LcUnitMap.OverTempNtc(1)]));
        Assert.Equal(0d, Convert.ToDouble(pts[LcUnitMap.AcCurrentR(1)]));
        Assert.Equal(40d, Convert.ToDouble(pts[LcUnitMap.GridActivePower(1)]));
        Assert.Equal(-8d, Convert.ToDouble(pts[LcUnitMap.GridReactivePower(1)]));
    }

    [Fact]
    public void Collect_BusVoltage_IsRmsOfPcsLineVoltages()
    {
        var m1 = new LcUnitModuleSnap(0, 0, 690, 680, 700, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var pts = LcUnitTelemetry.Collect(1, m1, null)
            .ToDictionary(p => p.Param, p => p.Value);

        double expected = Math.Sqrt((690d * 690 + 680d * 680 + 700d * 700) / 3.0);
        Assert.Equal(expected, Convert.ToDouble(pts[LcUnitMap.BusVoltage(1, 0)]), 6);
        Assert.Equal(0d, Convert.ToDouble(pts[LcUnitMap.BusVoltage(1, 1)]));
    }

    [Fact]
    public void Collect_InductorCurrents_AreAbsolutePcsPhaseCurrents()
    {
        var m1 = new LcUnitModuleSnap(0, 0, 0, 0, 0, -12.5, 8, -3, 0, 0, 0, 0, 0, 0, 0, 0);
        var pts = LcUnitTelemetry.Collect(1, m1, null)
            .ToDictionary(p => p.Param, p => p.Value);

        Assert.Equal(12.5, Convert.ToDouble(pts[LcUnitMap.InductorR(1, 0)]));
        Assert.Equal(8d, Convert.ToDouble(pts[LcUnitMap.InductorS(1, 0)]));
        Assert.Equal(3d, Convert.ToDouble(pts[LcUnitMap.InductorT(1, 0)]));
        Assert.Equal(0d, Convert.ToDouble(pts[LcUnitMap.InductorR(1, 1)]));
        Assert.Equal(12.5, Convert.ToDouble(pts[LcUnitMap.AcCurrentR(1)]));
        Assert.Equal(8d, Convert.ToDouble(pts[LcUnitMap.AcCurrentS(1)]));
        Assert.Equal(3d, Convert.ToDouble(pts[LcUnitMap.AcCurrentT(1)]));
    }

    [Fact]
    public void Collect_AcCurrents_SumModuleInductorCurrents_IgnoringMeter()
    {
        var m1 = new LcUnitModuleSnap(0, 0, 0, 0, 0, -10, 20, -30, 0, 0, 0, 0, 0, 0, 0, 0);
        var m2 = new LcUnitModuleSnap(0, 0, 0, 0, 0, 4, -5, 6, 0, 0, 0, 0, 0, 0, 0, 0);

        var pts = LcUnitTelemetry.Collect(1, m1, m2)
            .ToDictionary(p => p.Param, p => p.Value);

        Assert.Equal(14d, Convert.ToDouble(pts[LcUnitMap.AcCurrentR(1)]));
        Assert.Equal(25d, Convert.ToDouble(pts[LcUnitMap.AcCurrentS(1)]));
        Assert.Equal(36d, Convert.ToDouble(pts[LcUnitMap.AcCurrentT(1)]));
    }

    [Fact]
    public void Collect_GridPqAndVoltages_ComeFromPcsAcSideNotMeter()
    {
        var m1 = new LcUnitModuleSnap(0, 0, 690, 691, 692, 0, 0, 0, 100, 0, 0, 0, 0, 0, 120, -30);
        var m2 = new LcUnitModuleSnap(0, 0, 694, 695, 696, 0, 0, 0, 80, 0, 0, 0, 0, 0, 80, 10);

        var pts = LcUnitTelemetry.Collect(1, m1, m2)
            .ToDictionary(p => p.Param, p => p.Value);

        Assert.Equal(200d, Convert.ToDouble(pts[LcUnitMap.GridActivePower(1)]));
        Assert.Equal(-20d, Convert.ToDouble(pts[LcUnitMap.GridReactivePower(1)]));
        Assert.Equal(692d, Convert.ToDouble(pts[LcUnitMap.GridVoltageRs(1)]));
        Assert.Equal(693d, Convert.ToDouble(pts[LcUnitMap.GridVoltageSt(1)]));
        Assert.Equal(694d, Convert.ToDouble(pts[LcUnitMap.GridVoltageTr(1)]));
    }
}
