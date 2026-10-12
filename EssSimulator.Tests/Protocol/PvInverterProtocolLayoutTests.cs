using System.Text.Json;
using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel.Pv;
using EssSimulator.Protocol.Modbus;
using EssSimulator.Web;

namespace EssSimulator.Tests.Protocol;

[CollectionDefinition("PvInverterProtocolLayout", DisableParallelization = true)]
public class PvInverterProtocolLayoutCollection
{
}

[Collection("PvInverterProtocolLayout")]
public class PvInverterProtocolLayoutTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Enumerate_NoPv_DoesNotCreateInverterEndpoints(bool hasStorage)
    {
        var config = new SimulatorConfig();
        if (hasStorage)
            config.Devices.Add(new EssUnitConfig());

        Assert.Empty(PvInverterProtocolLayout.Enumerate(config));
        Assert.DoesNotContain(ProtocolPortPlan.BuildDefault(config).Entries,
            e => e.Type == ProtocolDeviceType.PvInverter);
    }

    [Fact]
    public void Enumerate_DifferentUnitSizes_KeepsGlobalAndLocalIndicesSeparate()
    {
        var endpoints = PvInverterProtocolLayout.Enumerate(CreateConfig(2, 3, 1));

        Assert.Equal(new[]
        {
            new PvInverterProtocolLayout.Endpoint(1, 0, 0),
            new PvInverterProtocolLayout.Endpoint(2, 0, 1),
            new PvInverterProtocolLayout.Endpoint(3, 1, 0),
            new PvInverterProtocolLayout.Endpoint(4, 1, 1),
            new PvInverterProtocolLayout.Endpoint(5, 1, 2),
            new PvInverterProtocolLayout.Endpoint(6, 2, 0)
        }, endpoints);
        Assert.Equal(Enumerable.Range(1, 6).Select(i => $"simPvInv{i}"), endpoints.Select(e => e.ServerName));
        Assert.Equal(new[] { 1, 1, 2, 2, 2, 3 }, endpoints.Select(e => e.UnitId));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    public void Enumerate_UsesSameEffectiveCountAsRuntime(int count)
    {
        var config = CreateConfig(count);
        var runtimeCount = PvUnitDevice.ToConfig(config.PvUnits[0]).InverterCount;
        var endpoints = PvInverterProtocolLayout.Enumerate(config);

        Assert.Equal(runtimeCount, endpoints.Count);
        Assert.Equal(Enumerable.Range(0, runtimeCount), endpoints.Select(e => e.InverterIndex0));
    }

    [Fact]
    public void BuildDefault_UsesDedicatedPortDefaults()
    {
        var config = CreateConfig(2, 1);
        var plan = ProtocolPortPlan.BuildDefault(config);
        var entries = plan.Entries.Where(e => e.Type == ProtocolDeviceType.PvInverter).ToArray();

        Assert.Equal(2101, config.Protocol.BasePvInverterModbusPort);
        Assert.Equal(1, config.Protocol.PvInverterPortStep);
        Assert.Equal(new[] { 2101, 2102, 2103 }, entries.Select(e => e.Port));
        Assert.All(entries, entry =>
        {
            Assert.Equal("pv_inverter.csv", entry.PointMapFile);
            Assert.Equal(1, entry.SlaveId);
            Assert.Equal(1, entry.DefaultSlaveId);
            Assert.Equal(entry.Port, entry.DefaultPort);
            Assert.True(entry.IsDefault);
            Assert.Equal(0, entry.RackCount);
            Assert.Equal(0, entry.LcGroupCount);
        });
    }

    [Fact]
    public void BuildDefault_CustomPortsCarryBindingIndicesWithoutChangingLegacyEndpoints()
    {
        var config = CreateConfig(2, 3);
        config.Devices.Add(new EssUnitConfig());
        config.Protocol.EnableLocalControl = true;
        config.Protocol.EnableSel = true;
        config.Protocol.BasePvInverterModbusPort = 4201;
        config.Protocol.PvInverterPortStep = 7;
        var plan = ProtocolPortPlan.BuildDefault(config);
        var entries = plan.Entries.Where(e => e.Type == ProtocolDeviceType.PvInverter).ToArray();

        Assert.Equal(new[] { 4201, 4208, 4215, 4222, 4229 }, entries.Select(e => e.Port));
        Assert.Equal(new int?[] { 1, 1, 2, 2, 2 }, entries.Select(e => e.PvUnitId));
        Assert.Equal(new int?[] { 0, 1, 0, 1, 2 }, entries.Select(e => e.InverterIndex0));
        var legacyPorts = new Dictionary<string, int>
        {
            ["simBms1"] = 1502, ["simBms2"] = 1512,
            ["simEmu1"] = 1601, ["simEmu2"] = 1602,
            ["simEm"] = 1500, ["simLc1"] = 1700, ["simSel"] = 2001,
            ["simPv1"] = 1801, ["simPv2"] = 1802,
            ["simPvMeter1"] = 1901, ["simPvMeter2"] = 1902
        };
        Assert.Equal(legacyPorts.Count + entries.Length, plan.Entries.Count);
        foreach (var (name, port) in legacyPorts)
        {
            var entry = plan.Find(name)!;
            Assert.Equal(port, entry.Port);
            Assert.Equal(1, entry.SlaveId);
            Assert.True(entry.IsDefault);
            Assert.Null(entry.PvUnitId);
            Assert.Null(entry.InverterIndex0);
        }
        Assert.Empty(plan.ValidateRanges());
    }

    [Fact]
    public void BuildDefault_SecondUnitFirstInverter_BindsToUnitNotServerNumber()
    {
        var entry = ProtocolPortPlan.BuildDefault(CreateConfig(2, 3)).Find("simPvInv3")!;
        var map = new ModbusPointMap(entry.PointMapFile, entry.Name,
            pvDeviceIdOverride: entry.PvUnitId, inverterIndex: entry.InverterIndex0);

        Assert.Equal("4", map.ParamModelLookup["yc0"].ModelType);
        Assert.Equal("pv2.Inverters[0].Protocol.RatedActivePowerKw", map.ParamModelLookup["yc0"].Arg1);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(65535, true)]
    [InlineData(65536, false)]
    public void ValidateRanges_InverterPortRespectsBounds(int port, bool valid)
    {
        var config = CreateConfig(1);
        config.Protocol.BasePvInverterModbusPort = port;
        var errors = ProtocolPortPlan.BuildDefault(config).ValidateRanges();

        if (valid)
            Assert.Empty(errors);
        else
            Assert.Contains(errors, e => e.Contains("simPvInv1") && e.Contains("端口"));
    }

    [Fact]
    public void ValidateRanges_PortStepCanExceedUpperBound()
    {
        var config = CreateConfig(3);
        config.Protocol.BasePvInverterModbusPort = 65534;
        var errors = ProtocolPortPlan.BuildDefault(config).ValidateRanges();

        Assert.Contains("simPvInv3", Assert.Single(errors));
        Assert.Contains("65536", errors[0]);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(247, true)]
    [InlineData(248, false)]
    public void ValidateRanges_InverterSlaveIdRespectsBounds(int slaveId, bool valid)
    {
        var plan = ProtocolPortPlan.BuildDefault(CreateConfig(1));
        plan.Find("simPvInv1")!.SlaveId = (byte)slaveId;
        var errors = plan.ValidateRanges();

        if (valid)
            Assert.Empty(errors);
        else
            Assert.Contains(errors, e => e.Contains("simPvInv1") && e.Contains("从站号"));
    }

    [Fact]
    public void Overrides_RoundTripKeepsBindingAndIgnoresEndpointsRemovedFromTopology()
    {
        var originalDirectory = Directory.GetCurrentDirectory();
        var temporaryDirectory = Directory.CreateTempSubdirectory("pv-inverter-ports-");
        try
        {
            var overridesPath = Path.Combine(temporaryDirectory.FullName, ProtocolPortPlan.OverridesRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(overridesPath)!);
            File.WriteAllText(overridesPath, "{\"entries\":[]}");
            Directory.SetCurrentDirectory(temporaryDirectory.FullName);
            var config = CreateConfig(2, 3);
            var plan = ProtocolPortPlan.BuildDefault(config);
            var entry = plan.Find("simPvInv3")!;
            entry.Port = 3207;
            entry.SlaveId = 7;

            ProtocolPortPlan.SaveOverrides(plan.Entries);

            var saved = JsonSerializer.Deserialize<ProtocolPortOverrides>(File.ReadAllText(overridesPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Assert.Equal("simPvInv3", Assert.Single(saved.Entries).Name);
            var loaded = ProtocolPortPlan.Load(config, out var error);
            Assert.Null(error);
            var overridden = loaded.Find("simPvInv3")!;
            Assert.Equal(3207, overridden.Port);
            Assert.Equal(7, overridden.SlaveId);
            Assert.Equal(2103, overridden.DefaultPort);
            Assert.Equal(1, overridden.DefaultSlaveId);
            Assert.Equal(2, overridden.PvUnitId);
            Assert.Equal(0, overridden.InverterIndex0);
            Assert.False(overridden.IsDefault);
            Assert.All(loaded.Entries.Where(e => e.Name != overridden.Name), e => Assert.True(e.IsDefault));

            var noPv = ProtocolPortPlan.Load(new SimulatorConfig(), out error);
            Assert.Null(error);
            Assert.DoesNotContain(noPv.Entries, e => e.Type == ProtocolDeviceType.PvInverter);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            temporaryDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void SnapshotRebuild_PreservesInverterBindingForPortChangeValidation()
    {
        var plan = ProtocolPortEndpoints.BuildPlanFromSnapshot(new List<ProtocolDeviceSnapshot>
        {
            new()
            {
                Name = "simPvInv3",
                Type = ProtocolDeviceType.PvInverter,
                PointMapFile = "pv_inverter.csv",
                PvUnitId = 2,
                InverterIndex0 = 0,
                DefaultPort = 2103,
                DefaultSlaveId = 1,
                Port = 3207,
                SlaveId = 7
            }
        });

        var entry = Assert.Single(plan.Entries);
        Assert.Equal(2, entry.PvUnitId);
        Assert.Equal(0, entry.InverterIndex0);
        Assert.Equal(3207, entry.Port);
        Assert.Equal(7, entry.SlaveId);
        Assert.Equal(2103, entry.DefaultPort);
        Assert.Equal(1, entry.DefaultSlaveId);
        Assert.False(entry.IsDefault);
        Assert.Empty(new ProtocolLayerManager().ValidatePlan(plan));
    }

    [Fact]
    public void ValidatePlan_DefaultPvPlanLoadsMapsWithoutConflicts()
    {
        var plan = ProtocolPortPlan.BuildDefault(CreateConfig(2, 3));

        Assert.Empty(new ProtocolLayerManager().ValidatePlan(plan));
    }

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(0, 2, false)]
    [InlineData(1, 1, false)]
    public void ValidatePlan_OverlappingInverterMapsRequireSamePortAndSlave(int portStep, int secondSlave, bool conflict)
    {
        var config = CreateConfig(1, 1);
        config.Protocol.PvInverterPortStep = portStep;
        var plan = ProtocolPortPlan.BuildDefault(config);
        plan.Entries.RemoveAll(e => e.Type != ProtocolDeviceType.PvInverter);
        plan.Find("simPvInv2")!.SlaveId = (byte)secondSlave;

        Assert.Empty(plan.ValidateRanges());
        var errors = new ProtocolLayerManager().ValidatePlan(plan);
        if (conflict)
        {
            Assert.NotEmpty(errors);
            Assert.All(errors, error =>
            {
                Assert.Contains("端口 2101", error);
                Assert.Contains("从站 1", error);
                Assert.Contains("simPvInv1", error);
                Assert.Contains("simPvInv2", error);
                Assert.Contains("冲突", error);
            });
        }
        else
        {
            Assert.Empty(errors);
        }
    }

    private static SimulatorConfig CreateConfig(params int[] inverterCounts) => new()
    {
        PvUnits = inverterCounts.Select(count => new PvUnitRuntimeConfig { InverterCount = count }).ToList()
    };
}
