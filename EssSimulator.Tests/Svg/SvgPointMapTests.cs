using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.Svg;

public class SvgPointMapTests
{
    private static string CsvPath =>
        Path.Combine(AppContext.BaseDirectory, "pointmaps/models/svg/standard/svg.csv");

    [Fact]
    public void Csv_UsesU16AndDocumentsUnitsWithoutAddressOverlap()
    {
        var lines = File.ReadAllLines(CsvPath);
        Assert.Equal("FunctionCode,Address,Type,Size,ParamName,Scale,Description,ModelSim", lines[0]);
        Assert.Equal(15, lines.Length);

        var map = new ModbusPointMap(CsvPath, "simSvg");
        var entries = Assert.Single(map.RawMaps);
        Assert.Equal(14, entries.Length);
        Assert.Equal(12, map.DataMaps.Count);
        Assert.Equal(2, map.ControlMaps.Count);
        Assert.Equal(entries.Length, entries.Select(e => e.ParamName).Distinct().Count());

        var run = entries.Single(e => e.ParamName == "svg1");
        Assert.Equal("u16", run.Type);
        Assert.Equal("System.UInt16", ModbusPointCodec.ToClrType(run));
        Assert.Contains("停机", run.Description);
        Assert.Contains("运行", run.Description);
        Assert.Contains("闭锁", run.Description);

        Assert.Contains("V", entries.Single(e => e.ParamName == "svg2").Description);
        Assert.Contains("A", entries.Single(e => e.ParamName == "svg5").Description);
        Assert.Contains("kvar", entries.Single(e => e.ParamName == "svg8").Description);
        Assert.Contains("Hz", entries.Single(e => e.ParamName == "svg10").Description);
        Assert.Contains("kvar", entries.Single(e => e.ParamName == "svg12").Description);
        var setpoint = entries.Single(e => e.ParamName == "svg14");
        Assert.Contains("kvar", setpoint.Description);
        Assert.Contains("容性", setpoint.Description);
        Assert.Contains("感性", setpoint.Description);

        var occupied = new HashSet<(int Space, int Address)>();
        foreach (var entry in entries)
        {
            Assert.Equal(1, entry.Scale);
            int space = entry.FunctionCode == 4 ? 4 : 3;
            for (int offset = 0; offset < entry.Size / 16; offset++)
                Assert.True(occupied.Add((space, entry.Address + offset)), entry.ParamName);
        }
    }
}
