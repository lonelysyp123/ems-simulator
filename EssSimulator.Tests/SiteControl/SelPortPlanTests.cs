using EssSimulator.Configuration;
using EssSimulator.Protocol.Modbus;
using EssSimulator.SiteControl;

namespace EssSimulator.Tests.SiteControl;

public class SelPortPlanTests
{
    [Fact]
    public void EnablingSel_AddsExactlyOneDeviceWithoutChangingExistingEntries()
    {
        var config = new SimulatorConfig
        {
            Devices = { new EssUnitConfig(), new EssUnitConfig() }
        };
        config.Protocol.EnableLocalControl = true;
        var before = ProtocolPortPlan.BuildDefault(config);
        config.Protocol.EnableSel = true;
        config.Protocol.SelModbusPort = 2050;
        var after = ProtocolPortPlan.BuildDefault(config);

        Assert.Equal(before.Entries.Count + 1, after.Entries.Count);
        var sel = Assert.Single(after.Entries.Where(e => e.Type == ProtocolDeviceType.Sel));
        Assert.Equal("simSel", sel.Name);
        Assert.Equal("sel.csv", sel.PointMapFile);
        Assert.Equal(2050, sel.Port);
        Assert.Equal(1, sel.SlaveId);
        foreach (var previous in before.Entries)
        {
            var current = after.Find(previous.Name)!;
            Assert.Equal(previous.Port, current.Port);
            Assert.Equal(previous.SlaveId, current.SlaveId);
            Assert.Equal(previous.PointMapFile, current.PointMapFile);
        }
    }

    [Fact]
    public void SelDoesNotDependOnLcBeingEnabled()
    {
        var config = new SimulatorConfig();
        config.Protocol.EnableSel = true;
        config.Protocol.EnableLocalControl = false;
        var plan = ProtocolPortPlan.BuildDefault(config);
        Assert.NotNull(plan.Find("simSel"));
        Assert.DoesNotContain(plan.Entries, e => e.Type == ProtocolDeviceType.Lc);
    }

    [Fact]
    public void DisabledSel_DoesNotAddAnEndpoint()
    {
        Assert.Null(ProtocolPortPlan.BuildDefault(new SimulatorConfig()).Find("simSel"));
    }

    [Fact]
    public void PointMapUsesZeroBasedAddressesAndDocumentedScales()
    {
        var map = new ModbusPointMap("sel.csv", "simSel");
        var points = map.RawMaps[0];
        Assert.Equal(Enumerable.Range(0, 7), points.Select(p => p.Address));
        Assert.All(points, p =>
        {
            Assert.Equal(6, p.FunctionCode);
            Assert.Equal(16, p.Size);
            Assert.Equal("u16", p.Type);
        });
        Assert.Equal(10, points[2].Scale);
        Assert.Equal(100, points[3].Scale);
        Assert.Equal(100, points[5].Scale);
    }

    [Fact]
    public void TargetsIncludeEveryStoragePcsRegardlessOfUnitSize()
    {
        var config = new SimulatorConfig
        {
            Devices =
            {
                new EssUnitConfig
                {
                    Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig(), new PcsDeviceConfig() }
                },
                new EssUnitConfig
                {
                    Pcs = { new PcsDeviceConfig() }
                }
            }
        };
        Assert.Equal(new[] { "simEmu1", "simEmu2", "simEmu3", "simEmu4" },
            SelPcsProtocolWriter.GetTargetNames(config));
    }
}
