using EssSimulator.LocalControl;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.LocalControl;

public class LcEmuRegisterCodecTests
{
    private static string StandardEmuCsvPath
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var csv = Path.Combine(dir.FullName, "pointmaps", "models", "emu", "standard", "emu.csv");
                if (File.Exists(csv))
                    return csv;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("找不到 emu/standard/emu.csv");
        }
    }

    [Fact]
    public void ActivePower_LcEngineering100_WritesEmuRaw1000()
    {
        var map = new ModbusPointMap(StandardEmuCsvPath, "simEmu1");
        var yt0 = Assert.Single(map.ControlMaps, e => e.ParamName == "yt0");
        Assert.Equal(10, yt0.Scale);

        var raw = LcEmuRegisterCodec.ToControlRegisterRaw(map.ControlMaps, "yt0", 100);
        Assert.Equal((short)1000, Convert.ToInt16(raw));
    }

    [Fact]
    public void ReactivePower_LcEngineering100_WritesEmuRaw1000()
    {
        var map = new ModbusPointMap(StandardEmuCsvPath, "simEmu1");
        var raw = LcEmuRegisterCodec.ToControlRegisterRaw(map.ControlMaps, "yt1", 100);
        Assert.Equal((short)1000, Convert.ToInt16(raw));
    }

    [Fact]
    public void IslandVoltage_Scale1_KeepsEngineering()
    {
        var map = new ModbusPointMap(StandardEmuCsvPath, "simEmu1");
        var raw = LcEmuRegisterCodec.ToControlRegisterRaw(map.ControlMaps, "yt3", 690);
        Assert.Equal((ushort)690, Convert.ToUInt16(raw));
    }

    [Fact]
    public void IslandFrequency_Engineering50_WritesEmuRaw5000()
    {
        var map = new ModbusPointMap(StandardEmuCsvPath, "simEmu1");
        var yt4 = Assert.Single(map.ControlMaps, e => e.ParamName == "yt4");
        Assert.Equal(100, yt4.Scale);

        var raw = LcEmuRegisterCodec.ToControlRegisterRaw(map.ControlMaps, "yt4", 50);
        Assert.Equal((ushort)5000, Convert.ToUInt16(raw));
    }

    [Fact]
    public void UnknownParam_ReturnsEngineeringUnchanged()
    {
        var raw = LcEmuRegisterCodec.ToControlRegisterRaw(Array.Empty<MapEntry>(), "yt0", 100);
        Assert.Equal(100.0, Convert.ToDouble(raw));
    }
}
