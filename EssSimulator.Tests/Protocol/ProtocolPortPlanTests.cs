using System.Text.Json;
using EssSimulator.Configuration;
using EssSimulator.Protocol.Modbus;
using EssSimulator.Web.Topology;

namespace EssSimulator.Tests;

public class ProtocolPortPlanTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static string OverridesPath =>
        Path.Combine(Directory.GetCurrentDirectory(), ProtocolPortPlan.OverridesRelativePath);

    public ProtocolPortPlanTests() => CleanupFile();

    public void Dispose() => CleanupFile();

    private static void CleanupFile()
    {
        if (File.Exists(OverridesPath))
            File.Delete(OverridesPath);
    }

    [Fact]
    public void BuildDefault_EmptyConfig_MatchesLegacyPortRules()
    {
        var plan = ProtocolPortPlan.BuildDefault(new SimulatorConfig());

        Assert.Equal(5, plan.Entries.Count);

        var bms1 = plan.Find("simBms1")!;
        Assert.Equal(1502, bms1.Port);
        Assert.Equal(12, bms1.RackCount);
        Assert.Equal("bms_bank.csv", bms1.PointMapFile);

        Assert.Equal(1512, plan.Find("simBms2")!.Port);
        Assert.Equal(1601, plan.Find("simEmu1")!.Port);
        Assert.Equal(1602, plan.Find("simEmu2")!.Port);
        Assert.Equal(1500, plan.Find("simEm")!.Port);

        Assert.All(plan.Entries, e =>
        {
            Assert.Equal(1, e.SlaveId);
            Assert.True(e.IsDefault);
        });

        Assert.Null(plan.Find("simLc1"));
    }

    [Fact]
    public void BuildDefault_LocalControlEnabled_IncludesLcEntry()
    {
        var cfg = new SimulatorConfig();
        cfg.Protocol.EnableLocalControl = true;

        var plan = ProtocolPortPlan.BuildDefault(cfg);
        var lc = plan.Find("simLc1");

        Assert.NotNull(lc);
        Assert.Equal(1700, lc!.Port);
        Assert.Equal(ProtocolDeviceType.Lc, lc.Type);
        Assert.Equal("lc.csv", lc.PointMapFile);
        Assert.Equal(1, lc.LcGroupCount);
        Assert.Null(plan.Find("simLc2"));
    }

    [Fact]
    public void BuildDefault_LocalControlEnabled_OneSimLcPerEssUnit()
    {
        var cfg = new SimulatorConfig
        {
            Devices =
            {
                new EssUnitConfig { Name = "A" },
                new EssUnitConfig { Name = "B" }
            }
        };
        cfg.Protocol.EnableLocalControl = true;
        cfg.Protocol.LocalControlEmuPerGroup = 4;

        var plan = ProtocolPortPlan.BuildDefault(cfg);

        Assert.NotNull(plan.Find("simLc1"));
        Assert.NotNull(plan.Find("simLc2"));
        Assert.Null(plan.Find("simLc3"));
        Assert.Equal(1700, plan.Find("simLc1")!.Port);
        Assert.Equal(1701, plan.Find("simLc2")!.Port);
        Assert.Equal(1, plan.Find("simLc1")!.LcGroupCount);
        Assert.Equal(1, plan.Find("simLc2")!.LcGroupCount);
    }

    [Fact]
    public void BuildDefault_LcGroupCount_FollowsUnitGroups()
    {
        var cfg = new SimulatorConfig
        {
            Devices =
            {
                new EssUnitConfig { Name = "flat" },
                new EssUnitConfig
                {
                    Name = "grouped",
                    Groups =
                    {
                        new EmuGroupConfig { Name = "g1", Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig() } },
                        new EmuGroupConfig { Name = "g2", Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig() } }
                    }
                }
            }
        };
        cfg.Protocol.EnableLocalControl = true;

        var plan = ProtocolPortPlan.BuildDefault(cfg);

        Assert.Equal(1, plan.Find("simLc1")!.LcGroupCount);
        Assert.Equal(2, plan.Find("simLc2")!.LcGroupCount);
    }

    [Fact]
    public void BuildDefault_LocalControlDisabled_OmitsSimLcEvenWithUnits()
    {
        var cfg = new SimulatorConfig
        {
            Devices = { new EssUnitConfig { Name = "A" }, new EssUnitConfig { Name = "B" } }
        };
        cfg.Protocol.EnableLocalControl = false;

        var plan = ProtocolPortPlan.BuildDefault(cfg);
        Assert.Null(plan.Find("simLc1"));
    }

    [Fact]
    public void SaveOverrides_RoundTrip_OnlyNonDefaultPersisted()
    {
        var plan = ProtocolPortPlan.BuildDefault(new SimulatorConfig());
        var emu = plan.Find("simEmu1")!;
        emu.Port = 2501;
        emu.SlaveId = 2;

        ProtocolPortPlan.SaveOverrides(plan.Entries);
        Assert.True(File.Exists(OverridesPath));

        // 仅非默认条目落盘
        var saved = JsonSerializer.Deserialize<ProtocolPortOverrides>(File.ReadAllText(OverridesPath), JsonOpts);
        Assert.NotNull(saved);
        Assert.Single(saved!.Entries);
        Assert.Equal("simEmu1", saved.Entries[0].Name);

        var loaded = ProtocolPortPlan.Load(new SimulatorConfig(), out var error);
        Assert.Null(error);
        Assert.Equal(2501, loaded.Find("simEmu1")!.Port);
        Assert.Equal(2, loaded.Find("simEmu1")!.SlaveId);
        Assert.False(loaded.Find("simEmu1")!.IsDefault);
        // 未覆盖条目保持默认
        Assert.Equal(1502, loaded.Find("simBms1")!.Port);
        Assert.True(loaded.Find("simBms1")!.IsDefault);
    }

    [Fact]
    public void Load_Mw300_IgnoresOverridesFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OverridesPath)!);
        File.WriteAllText(OverridesPath, """
            { "entries": [ { "name": "simBms1", "port": 1999, "slaveId": 9 } ] }
            """);

        var loaded = ProtocolPortPlan.Load(
            new SimulatorConfig { StationProfileId = Mw300EssStation.Id },
            out var error);

        Assert.Null(error);
        Assert.Equal(10001, loaded.Find("simBms1")!.Port);
        Assert.Equal(10, loaded.Find("simBms1")!.SlaveId);
        Assert.True(File.Exists(OverridesPath));
    }

    [Fact]
    public void TryPersistPortOverrides_FrozenStationLeavesFileUntouched()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OverridesPath)!);
        File.WriteAllText(OverridesPath, """{ "entries": [] }""");
        var before = File.ReadAllText(OverridesPath);

        Assert.False(Mw300EssStation.TryPersistPortOverrides(
            Mw300EssStation.Id, entries: null, clear: true, out var error));
        Assert.Equal(Mw300EssStation.LockedMessage, error);
        Assert.Equal(before, File.ReadAllText(OverridesPath));

        Assert.True(Mw300EssStation.TryPersistPortOverrides(
            stationProfileId: null, entries: null, clear: true, out _));
        Assert.False(File.Exists(OverridesPath));
    }

    [Fact]
    public void ClearOverrides_RestoresDefaultsAndRemovesFile()
    {
        var plan = ProtocolPortPlan.BuildDefault(new SimulatorConfig());
        plan.Find("simEm")!.Port = 3000;
        ProtocolPortPlan.SaveOverrides(plan.Entries);
        Assert.True(File.Exists(OverridesPath));

        ProtocolPortPlan.ClearOverrides();
        Assert.False(File.Exists(OverridesPath));

        var loaded = ProtocolPortPlan.Load(new SimulatorConfig(), out _);
        Assert.Equal(1500, loaded.Find("simEm")!.Port);
    }

    [Fact]
    public void CorruptedOverridesFile_FallsBackToDefaultWithError()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OverridesPath)!);
        File.WriteAllText(OverridesPath, "{ this is not json ");

        var loaded = ProtocolPortPlan.Load(new SimulatorConfig(), out var error);

        Assert.NotNull(error);
        Assert.Equal(1601, loaded.Find("simEmu1")!.Port);
    }

    [Fact]
    public void SaveOverrides_AllDefault_DeletesFile()
    {
        var plan = ProtocolPortPlan.BuildDefault(new SimulatorConfig());
        plan.Find("simEm")!.Port = 3000;
        ProtocolPortPlan.SaveOverrides(plan.Entries);
        Assert.True(File.Exists(OverridesPath));

        plan.Find("simEm")!.Port = 1500; // 恢复默认后再保存
        ProtocolPortPlan.SaveOverrides(plan.Entries);
        Assert.False(File.Exists(OverridesPath));
    }

    [Fact]
    public void ValidateRanges_DetectsInvalidPortAndSlaveId()
    {
        var plan = new ProtocolPortPlan();
        plan.Entries.Add(new ProtocolPortEntry { Name = "a", Port = 0, SlaveId = 1 });
        plan.Entries.Add(new ProtocolPortEntry { Name = "b", Port = 70000, SlaveId = 1 });
        plan.Entries.Add(new ProtocolPortEntry { Name = "c", Port = 1502, SlaveId = 248 });

        var errors = plan.ValidateRanges();
        Assert.Contains(errors, e => e.Contains("a") && e.Contains("端口"));
        Assert.Contains(errors, e => e.Contains("b") && e.Contains("端口"));
        Assert.Contains(errors, e => e.Contains("c") && e.Contains("从站号"));
    }

    [Fact]
    public void ValidateRanges_SamePortSameSlaveId_IsMergeScenario_NotRangeError()
    {
        // 同端口同从站号属于合并点表场景，范围校验不拒绝，合法性由地址查重判定
        var plan = new ProtocolPortPlan();
        plan.Entries.Add(new ProtocolPortEntry { Name = "simEmu1", Port = 1601, SlaveId = 1 });
        plan.Entries.Add(new ProtocolPortEntry { Name = "simLc1", Port = 1601, SlaveId = 1 });
        plan.Entries.Add(new ProtocolPortEntry { Name = "simBms1", Port = 1601, SlaveId = 1, RackCount = 2 });

        Assert.Empty(plan.ValidateRanges());
    }
}
