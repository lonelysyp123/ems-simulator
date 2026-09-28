using EssSimulator.EssDeviceSimModel.Svg;
using EssSimulator.EssSimModelApi;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.Svg;

public class SvgPointMapBindingTests
{
    private static readonly (string Param, string Property, bool Writable)[] Points =
    {
        ("svg1", "RunState", false),
        ("svg2", "LineVoltageAbV", false),
        ("svg3", "LineVoltageBcV", false),
        ("svg4", "LineVoltageCaV", false),
        ("svg5", "PhaseACurrentA", false),
        ("svg6", "PhaseBCurrentA", false),
        ("svg7", "PhaseCCurrentA", false),
        ("svg8", "ReactivePowerKvar", false),
        ("svg9", "PowerFactor", false),
        ("svg10", "FrequencyHz", false),
        ("svg11", "RatedCapacityKvar", false),
        ("svg12", "AvailableReactiveUpperKvar", false),
        ("svg13", "RunCommand", true),
        ("svg14", "ReactiveSetpointKvar", true)
    };

    [Fact]
    public void EveryPointBindsToSvgRoot()
    {
        var map = new ModbusPointMap("svg.csv", "simSvg");
        Assert.Equal(12, map.DataMaps.Count);
        Assert.Equal(2, map.ControlMaps.Count);
        Assert.Equal(Points.Length, map.ParamModelLookup.Count);

        var protocol = new SvgProtocolData(new SvgDevice("svg"));
        foreach (var point in Points)
        {
            var model = map.ParamModelLookup[point.Param];
            Assert.Equal("4", model.ModelType);
            Assert.Equal($"svg.{point.Property}", model.Arg1);
            Assert.NotNull(ObjectPathResolver.GetValue(protocol, point.Property));
            if (point.Writable)
                Assert.True(ObjectPathResolver.SetValue(protocol, point.Property, point.Param == "svg13" ? 1 : 2500d));
        }

        Assert.Equal((ushort)1, protocol.RunCommand);
        Assert.Equal(2500, protocol.ReactiveSetpointKvar);
    }

    [Fact]
    public void WrittenSetpointStaysRawWhileOutputIsClamped()
    {
        var device = new SvgDevice("svg", new SvgConfig { RatedCapacityKvar = 10000, NominalLineVoltageV = 35000 });
        var protocol = new SvgProtocolData(device);
        Assert.True(ObjectPathResolver.SetValue(protocol, "RunCommand", 1));
        Assert.True(ObjectPathResolver.SetValue(protocol, "ReactiveSetpointKvar", 1_000_000d));

        device.ApplyMeasurement(35000, 50, TimeSpan.FromSeconds(1));

        Assert.Equal(1_000_000, protocol.ReactiveSetpointKvar);
        Assert.Equal(10000, protocol.ReactivePowerKvar, 3);
        Assert.Equal(10000, protocol.AvailableReactiveUpperKvar, 6);
    }
}
