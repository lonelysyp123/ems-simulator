using EssSimulator.DataExchange.Catalog;
using EssSimulator.EssDeviceSimModel.Pv;
using EssSimulator.EssSimModelApi;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.Pv;

/// <summary>
/// 核对光伏电表、Logger、单机逆变器点表的 ModelSim 绑定。
/// </summary>
public class PvPointMapBindingTests
{
    private static readonly string[] Apm810Expected =
    {
        "yc0:MeterLv.PhaseAVoltage",
        "yc1:MeterLv.PhaseBVoltage",
        "yc2:MeterLv.PhaseCVoltage",
        "yc3:MeterLv.LineVoltageAb",
        "yc4:MeterLv.LineVoltageBc",
        "yc5:MeterLv.LineVoltageCa",
        "yc6:MeterLv.PhaseACurrent",
        "yc7:MeterLv.PhaseBCurrent",
        "yc8:MeterLv.PhaseCCurrent",
        "yc9:MeterLv.PhaseAActivePowerW",
        "yc10:MeterLv.PhaseBActivePowerW",
        "yc11:MeterLv.PhaseCActivePowerW",
        "yc12:MeterLv.TotalActivePowerW",
        "yc13:MeterLv.FeedInPowerM1W",
        "yc14:MeterLv.PhaseAReactivePowerVar",
        "yc15:MeterLv.PhaseBReactivePowerVar",
        "yc16:MeterLv.PhaseCReactivePowerVar",
        "yc17:MeterLv.TotalReactivePowerVar",
        "yc18:MeterLv.PhaseAApparentPowerVa",
        "yc19:MeterLv.PhaseBApparentPowerVa",
        "yc20:MeterLv.PhaseCApparentPowerVa",
        "yc21:MeterLv.TotalApparentPowerVa",
        "yc22:MeterLv.TotalPowerFactor",
        "yc23:MeterLv.FrequencyHz",
        "yc24:MeterLv.ForwardActiveEnergyKwh",
        "yc25:MeterLv.ReverseActiveEnergyKwh",
        "yc26:MeterLv.ForwardReactiveEnergyKvarh",
        "yc27:MeterLv.ReverseReactiveEnergyKvarh",
        "yc30:MeterLv.FeedInPowerM2W",
        "yc31:MeterLv.PhaseAPowerFactor",
        "yc32:MeterLv.PhaseBPowerFactor",
        "yc33:MeterLv.PhaseCPowerFactor"
    };

    private static readonly string[] LoggerExpected =
    {
        "yc0:Logger.DigitalInputBitmap",
        "yx7:Logger.OilTemperatureAlarm",
        "yx8:Logger.OilTemperatureTrip",
        "yx11:Logger.WindingTemperatureAlarm",
        "yx12:Logger.WindingTemperatureTrip",
        "yc1:Logger.Pt100_1C",
        "yc2:Logger.Pt100_2C",
        "yc3:Logger.Adc1Voltage",
        "yc4:Logger.Adc1CurrentMa",
        "yc5:Logger.Adc2Voltage",
        "yc6:Logger.Adc2CurrentMa",
        "yc7:Logger.Adc3Voltage",
        "yc8:Logger.Adc4Voltage",
        "yc9:Logger.Adc3CurrentMa",
        "yc10:Logger.Adc4CurrentMa",
        "yc11:Logger.ConnectedDeviceCount",
        "yc12:Logger.FaultDeviceCount",
        "yc13:Logger.RunState",
        "yc14:Logger.UnlatchState",
        "yc15:Logger.TotalActivePowerW",
        "yc16:Logger.DailyYieldKwh",
        "yc17:Logger.TotalReactivePowerVar",
        "yc18:Logger.TotalYieldKwh",
        "yc19:Logger.MinAdjustableActivePowerKw",
        "yc20:Logger.MaxAdjustableActivePowerKw",
        "yc21:Logger.MinAdjustableReactivePowerKvar",
        "yc22:Logger.MaxAdjustableReactivePowerKvar",
        "yc23:Logger.NominalActivePowerKw",
        "yc24:Logger.NominalReactivePowerKvar",
        "yc25:Logger.GridConnectedDeviceCount",
        "yc26:Logger.OffGridDeviceCount",
        "yc27:Logger.MonthlyYieldKwh",
        "yc28:Logger.AnnualYieldKwh",
        "yc29:Logger.ApparentPowerVa",
        "yt0:Logger.DoRmuTrip",
        "yt1:Logger.DoLvFan",
        "yt2:Logger.Do3",
        "yt3:Logger.Do4",
        "yt4:Logger.SubarrayOnOff",
        "yt5:Logger.SubarrayActivePowerKw",
        "yt6:Logger.SubarrayActivePowerPercent",
        "yt7:Logger.SubarrayReactivePowerKvar",
        "yt8:Logger.SubarrayReactivePowerPercent",
        "yt9:Logger.SubarrayPowerFactor"
    };

    private static readonly string[] LoggerMissingDin =
    {
        "yx0", "yx1", "yx2", "yx3", "yx4", "yx5", "yx6",
        "yx9", "yx10", "yx13", "yx14", "yx15"
    };

    private static readonly (string Param, int Fc, int Address, string Type, int Size, int Scale, string? Property)[] InverterExpected =
    {
        ("yc0", 4, 5001, "u16", 16, 10, "RatedActivePowerKw"),
        ("yc1", 4, 5009, "u32", 32, 1, "ApparentPowerVa"),
        ("yc2", 4, 5011, "u16", 16, 10, "MpptVoltageV[0]"),
        ("yc3", 4, 5012, "u16", 16, 10, "MpptCurrentA[0]"),
        ("yc4", 4, 5013, "u16", 16, 10, "MpptVoltageV[1]"),
        ("yc5", 4, 5014, "u16", 16, 10, "MpptCurrentA[1]"),
        ("yc6", 4, 5015, "u16", 16, 10, "MpptVoltageV[2]"),
        ("yc7", 4, 5016, "u16", 16, 10, "MpptCurrentA[2]"),
        ("yc8", 4, 5019, "u16", 16, 10, "LineVoltageV"),
        ("yc9", 4, 5020, "u16", 16, 10, "LineVoltageV"),
        ("yc10", 4, 5021, "u16", 16, 10, "LineVoltageV"),
        ("yc11", 4, 5022, "u16", 16, 10, "PhaseCurrentAmplitudeA"),
        ("yc12", 4, 5023, "u16", 16, 10, "PhaseCurrentAmplitudeA"),
        ("yc13", 4, 5024, "u16", 16, 10, "PhaseCurrentAmplitudeA"),
        ("yc14", 4, 5031, "int32", 32, 1, "ActivePowerW"),
        ("yc15", 4, 5033, "int32", 32, 1, "ReactivePowerVar"),
        ("yc16", 4, 5035, "int16", 16, 1000, "PowerFactor"),
        ("yc17", 4, 5036, "u16", 16, 10, "FrequencyHz"),
        ("yc18", 4, 5038, "u16", 16, 1, "OperationStatus"),
        ("yc19", 4, 5045, "u16", 16, 1, null),
        ("yc20", 4, 5049, "u16", 16, 10, "RatedReactivePowerKvar"),
        ("yc21", 4, 5077, "int32", 32, 1, "ActivePowerSettingW"),
        ("yc22", 4, 5079, "int32", 32, 1, "ReactivePowerSettingVar"),
        ("yc23", 4, 5115, "u16", 16, 10, "MpptVoltageV[3]"),
        ("yc24", 4, 5116, "u16", 16, 10, "MpptCurrentA[3]"),
        ("yc25", 4, 5117, "u16", 16, 10, "MpptVoltageV[4]"),
        ("yc26", 4, 5118, "u16", 16, 10, "MpptCurrentA[4]"),
        ("yc27", 4, 5119, "u16", 16, 10, "MpptVoltageV[5]"),
        ("yc28", 4, 5120, "u16", 16, 10, "MpptCurrentA[5]"),
        ("yc29", 4, 5194, "u16", 16, 1, null),
        ("yc30", 4, 5195, "u16", 16, 1, null),
        ("yk0", 6, 5006, "u16", 16, 1, "RunCommand"),
        ("yt0", 6, 5039, "u16", 16, 10, "ActivePowerSettingKw"),
        ("yt1", 6, 5040, "int16", 16, 10, "ReactivePowerSettingKvar")
    };

    private static string InverterCsvPath =>
        Path.Combine(FindRepoRoot(), "pointmaps", "models", "pv", "standard", "pv_inverter.csv");

    [Fact]
    public void ObjectPathResolver_ReadsEveryBoundMeterAndLoggerPath()
    {
        var unit = PvUnitDevice.CreateDefault("pv1");
        foreach (var spec in Apm810Expected.Concat(LoggerExpected))
        {
            var path = spec.Split(':')[1];
            Assert.False(ObjectPathResolver.GetValue(unit, path) is null, path);
        }
    }

    [Fact]
    public void Apm810Csv_BindsKnownPoints_LeavesReservedUnbound()
    {
        var map = LoadMap("pv_apm810.csv");
        AssertBindings(map, Apm810Expected);
        Assert.False(map.ParamModelLookup.ContainsKey("yc28"));
        Assert.False(map.ParamModelLookup.ContainsKey("yc29"));
        Assert.Equal(Apm810Expected.Length, map.ParamModelLookup.Count);
    }

    [Fact]
    public void LoggerCsv_BindsKnownPoints_LeavesMissingDinUnbound()
    {
        var map = LoadMap("pv_logger.csv");
        AssertBindings(map, LoggerExpected);
        foreach (var param in LoggerMissingDin)
            Assert.False(map.ParamModelLookup.ContainsKey(param), param);
        Assert.Equal(LoggerExpected.Length, map.ParamModelLookup.Count);
    }

    [Fact]
    public void InverterCsv_PreservesProtocolContractAndUnboundRegisters()
    {
        var lines = File.ReadAllLines(InverterCsvPath);
        Assert.Equal("FunctionCode,Address,Type,Size,ParamName,Scale,Description,ModelSim", lines[0]);
        Assert.Equal(35, lines.Length);
        Assert.All(lines.Skip(1), line => Assert.Equal(8, line.Split(',').Length));

        var map = new ModbusPointMap(InverterCsvPath, "simPvInv17", pvDeviceIdOverride: 2, inverterIndex: 0);
        var entries = Assert.Single(map.RawMaps);
        Assert.Equal(34, entries.Length);
        Assert.Equal(31, map.DataMaps.Count);
        Assert.Equal(3, map.ControlMaps.Count);
        Assert.Equal(31, map.ParamModelLookup.Count);
        Assert.Equal(3, map.DefaultBuffer.Count);
        Assert.Equal(entries.Length, entries.Select(e => e.ParamName).Distinct().Count());

        var occupied = new HashSet<(int Area, int Address)>();
        for (int i = 0; i < InverterExpected.Length; i++)
        {
            var expected = InverterExpected[i];
            var entry = entries[i];
            Assert.Equal(expected.Param, entry.ParamName);
            Assert.Equal(expected.Fc, entry.FunctionCode);
            Assert.Equal(expected.Address, entry.Address);
            Assert.Equal(expected.Type, entry.Type);
            Assert.Equal(expected.Size, entry.Size);
            Assert.Equal(expected.Scale, entry.Scale);
            Assert.False(string.IsNullOrWhiteSpace(entry.Description));
            for (int offset = 0; offset < entry.Size / 16; offset++)
            {
                int address = entry.Address + offset;
                Assert.InRange(address, 0, ushort.MaxValue);
                Assert.True(occupied.Add((entry.FunctionCode == 4 ? 4 : 3, address)), entry.ParamName);
            }

            var template = lines[i + 1].Split(',')[7];
            if (expected.Property == null)
            {
                Assert.Equal("0", template);
                Assert.Equal("0", entry.ModelSim);
                Assert.False(map.ParamModelLookup.ContainsKey(expected.Param));
                Assert.Equal(0f, Assert.IsType<float>(map.DefaultBuffer[expected.Param]));
            }
            else
            {
                Assert.Equal($"model=4|arg1=pvDeviceId.Inverters[inverterIndex].Protocol.{expected.Property}", template);
                var model = map.ParamModelLookup[expected.Param];
                Assert.Equal("4", model.ModelType);
                Assert.Equal($"pv2.Inverters[0].Protocol.{expected.Property}", model.Arg1);
            }
        }

        Assert.Equal("有功功率调节设置点 系数0.1(阳光电源逆变器 保持寄存器 读0x03 写0x06/0x10)",
            entries.Single(e => e.ParamName == "yt0").Description);
    }

    [Theory]
    [InlineData("simPvInv1", 1, 0)]
    [InlineData("simPvInv17", 2, 0)]
    [InlineData("simPvInv18", 2, 1)]
    [InlineData("simPv2Inv16", 3, 15)]
    [InlineData("pv-inverter", 4, 0)]
    public void InverterBindings_UseExplicitUnitAndIndexInBothConstructors(string serverName, int unitId, int index)
    {
        var unit = new PvUnitDevice($"pv{unitId}", new PvUnitConfig { InverterCount = index + 1 });
        var maps = new[]
        {
            new ModbusPointMap(InverterCsvPath, serverName, pvDeviceIdOverride: unitId, inverterIndex: index),
            new ModbusPointMap(CSVUtil.CSV2Class<MapEntry>(InverterCsvPath)!.ToArray(), serverName,
                pvDeviceIdOverride: unitId, inverterIndex: index)
        };
        foreach (var map in maps)
        {
            Assert.Equal(31, map.ParamModelLookup.Count);
            foreach (var expected in InverterExpected.Where(e => e.Property != null))
            {
                string path = $"Inverters[{index}].Protocol.{expected.Property}";
                Assert.Equal($"pv{unitId}.{path}", map.ParamModelLookup[expected.Param].Arg1);
                Assert.NotNull(ObjectPathResolver.GetValue(unit, path));
            }
        }
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, 0)]
    [InlineData(1, null)]
    public void InverterBindings_RequireExplicitUnitAndIndex(int? unitId, int? index)
    {
        Assert.Throws<ArgumentException>(() => new ModbusPointMap(InverterCsvPath, "simPvInv17",
            pvDeviceIdOverride: unitId, inverterIndex: index));
        Assert.Throws<ArgumentException>(() => new ModbusPointMap(
            CSVUtil.CSV2Class<MapEntry>(InverterCsvPath)!.ToArray(), "pv-inverter",
            pvDeviceIdOverride: unitId, inverterIndex: index));
    }

    [Theory]
    [InlineData(0, 0, "pvDeviceIdOverride")]
    [InlineData(-1, 0, "pvDeviceIdOverride")]
    [InlineData(1, -1, "inverterIndex")]
    public void InverterBindings_RejectInvalidUnitAndIndex(int unitId, int index, string parameter)
    {
        var fileError = Assert.Throws<ArgumentOutOfRangeException>(() => new ModbusPointMap(
            InverterCsvPath, "simPvInv17", pvDeviceIdOverride: unitId, inverterIndex: index));
        Assert.Equal(parameter, fileError.ParamName);
        var entriesError = Assert.Throws<ArgumentOutOfRangeException>(() => new ModbusPointMap(
            CSVUtil.CSV2Class<MapEntry>(InverterCsvPath)!.ToArray(), "pv-inverter",
            pvDeviceIdOverride: unitId, inverterIndex: index));
        Assert.Equal(parameter, entriesError.ParamName);
    }

    [Theory]
    [InlineData("pv_logger.csv", "simPv7")]
    [InlineData("pv_apm810.csv", "simPvMeter7")]
    public void ExistingPvMaps_KeepLegacyNumberingAndAllowExplicitUnit(string fileName, string serverName)
    {
        var path = Path.Combine(FindRepoRoot(), "pointmaps", "models", "pv", "standard", fileName);
        var legacy = new ModbusPointMap(path, serverName);
        Assert.NotEmpty(legacy.ParamModelLookup);
        Assert.All(legacy.ParamModelLookup.Values, model => Assert.StartsWith("pv7.", model.Arg1));

        var explicitUnit = new ModbusPointMap(path, "pv-endpoint", pvDeviceIdOverride: 2);
        Assert.Equal(legacy.ParamModelLookup.Count, explicitUnit.ParamModelLookup.Count);
        Assert.All(explicitUnit.ParamModelLookup.Values, model => Assert.StartsWith("pv2.", model.Arg1));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    [InlineData(2, 2)]
    public void InverterCatalog_ControlsAndReadbackTargetOnlySelectedMachine(int unitId, int index)
    {
        var units = new Dictionary<string, PvUnitDevice>
        {
            ["pv1"] = new("pv1", new PvUnitConfig { InverterCount = 2 }),
            ["pv2"] = new("pv2", new PvUnitConfig { InverterCount = 3 })
        };
        foreach (var unit in units.Values)
        {
            unit.Logger.SubarrayOnOff = 0;
            unit.Logger.SubarrayActivePowerKw = 40 * unit.InverterCount;
            unit.Logger.SubarrayReactivePowerKvar = 10 * unit.InverterCount;
        }

        var map = new ModbusPointMap(InverterCsvPath, "simPvInv99",
            pvDeviceIdOverride: unitId, inverterIndex: index);
        var catalog = PointCatalogLoader.FromPointMap(map, "simPvInv99");
        Assert.Equal(28, catalog.TelemetryPoints.Count);
        Assert.Equal(3, catalog.ControlPoints.Count);
        Assert.All(catalog.ControlPoints, point =>
        {
            Assert.Equal(ControlSemantics.Hold, point.Semantics);
            Assert.Equal(ControlEffectId.None, point.Effect);
        });
        var points = catalog.TelemetryPoints.Concat(catalog.ControlPoints).ToDictionary(p => p.ParamName);
        Assert.All(points.Values, point =>
        {
            Assert.Equal($"pv{unitId}", point.Target.RootKey);
            Assert.NotNull(ObjectPathResolver.GetValue(units[point.Target.RootKey], point.Target.PropertyPath));
        });

        void WriteControl(string param, object value)
        {
            var point = catalog.ControlPoints.Single(p => p.ParamName == param);
            Assert.True(ObjectPathResolver.SetValue(units[point.Target.RootKey], point.Target.PropertyPath, value));
        }

        double ReadPoint(string param)
        {
            var point = points[param];
            return Convert.ToDouble(ObjectPathResolver.GetValue(units[point.Target.RootKey], point.Target.PropertyPath));
        }

        WriteControl("yt0", 123.4);
        Assert.Equal(10d, ReadPoint("yt1"));
        WriteControl("yt1", -23.4);
        Assert.Equal(123.4, ReadPoint("yt0"));
        Assert.Equal(123400d, ReadPoint("yc21"), 6);
        Assert.Equal(-23400d, ReadPoint("yc22"), 6);
        Assert.Equal(0d, ReadPoint("yk0"));
        WriteControl("yk0", (ushort)1);
        Assert.Equal(1d, ReadPoint("yk0"));

        var selected = units[$"pv{unitId}"].Inverters[index];
        Assert.True(selected.IsExternalRunCommand);
        Assert.Equal(123.4, selected.ActivePowerSettingKw);
        Assert.Equal(-23.4, selected.ReactivePowerSettingKvar);
        foreach (var unit in units.Values)
        {
            Assert.Equal(40d * unit.InverterCount, unit.Logger.SubarrayActivePowerKw);
            Assert.Equal(10d * unit.InverterCount, unit.Logger.SubarrayReactivePowerKvar);
            Assert.Equal((ushort)0, unit.Logger.SubarrayOnOff);
            foreach (var inverter in unit.Inverters.Where(inv => !ReferenceEquals(inv, selected)))
            {
                Assert.False(inverter.IsExternalRunCommand);
                Assert.Equal(40d, inverter.ActivePowerSettingKw);
                Assert.Equal(10d, inverter.ReactivePowerSettingKvar);
            }
        }

        WriteControl("yk0", (ushort)0);
        Assert.False(selected.IsExternalRunCommand);
        Assert.Equal(0d, ReadPoint("yk0"));
        Assert.Equal(123.4, ReadPoint("yt0"));
        Assert.Equal(-23.4, ReadPoint("yt1"));
        Assert.Equal(123400d, ReadPoint("yc21"), 6);
        Assert.Equal(-23400d, ReadPoint("yc22"), 6);
    }

    private static void AssertBindings(ModbusPointMap map, string[] expected)
    {
        foreach (var spec in expected)
        {
            var parts = spec.Split(':');
            Assert.True(map.ParamModelLookup.TryGetValue(parts[0], out var model), parts[0]);
            Assert.Equal($"pv1.{parts[1]}", model.Arg1);
        }
    }

    private static ModbusPointMap LoadMap(string fileName)
    {
        var path = Path.Combine(FindRepoRoot(), "pointmaps", "models", "pv", "standard", fileName);
        Assert.True(File.Exists(path), path);
        return new ModbusPointMap(path, "simPv1");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EssSimulator.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("未找到仓库根目录");
    }
}
