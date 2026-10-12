using System.Text.Json;
using EssSimulator.Configuration;
using EssSimulator.Web.Topology;
using Xunit;

namespace EssSimulator.Tests.Topology;

public class TopologyOverlayLoaderTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void IsUsable_requires_units_with_pcs_and_bms()
    {
        Assert.False(TopologyOverlayLoader.IsUsable(null));
        Assert.False(TopologyOverlayLoader.IsUsable(new TopologyRuntimeOverlay()));
        Assert.False(TopologyOverlayLoader.IsUsable(new TopologyRuntimeOverlay
        {
            EssUnits = { new EssUnitConfig { Name = "U1" } }
        }));
        Assert.True(TopologyOverlayLoader.IsUsable(new TopologyRuntimeOverlay
        {
            EssUnits =
            {
                new EssUnitConfig
                {
                    Name = "U1",
                    Pcs = { new PcsDeviceConfig { Name = "PCS-A" } },
                    Bms = { new BmsDeviceConfig { Name = "BMS-A" } }
                }
            }
        }));
    }

    [Fact]
    public void IsUsable_true_when_only_pv_units()
    {
        Assert.True(TopologyOverlayLoader.IsUsable(new TopologyRuntimeOverlay
        {
            PvUnits = { new PvUnitRuntimeConfig { Name = "光伏单元-1" } }
        }));
    }

    [Fact]
    public void TryLoad_returns_null_when_engineering_mode_off()
    {
        using var tmp = new TempTopologyRoot(engineeringMode: false, overlay: UsableOverlay());
        Assert.Null(TopologyOverlayLoader.TryLoad(tmp.Root));
    }

    [Fact]
    public void TryLoad_returns_null_when_overlay_has_no_units()
    {
        using var tmp = new TempTopologyRoot(engineeringMode: true, overlay: new TopologyRuntimeOverlay());
        CaptureConsole(() => Assert.Null(TopologyOverlayLoader.TryLoad(tmp.Root)));
    }

    [Fact]
    public void TryLoad_returns_overlay_when_engineering_mode_on_and_usable()
    {
        var expected = UsableOverlay();
        using var tmp = new TempTopologyRoot(engineeringMode: true, overlay: expected);
        var loaded = TopologyOverlayLoader.TryLoad(tmp.Root);
        Assert.NotNull(loaded);
        Assert.Equal(expected.SourceProjectName, loaded!.SourceProjectName);
        Assert.Single(loaded.EssUnits);
    }

    [Fact]
    public void TryLoad_mw300_uses_code_even_when_overlay_file_differs_or_is_missing()
    {
        using var garbage = new TempTopologyRoot(
            engineeringMode: true,
            overlay: new TopologyRuntimeOverlay { SourceProjectName = "被人改过" },
            activeProjectId: Mw300EssStation.Id);
        var fromGarbage = TopologyOverlayLoader.TryLoad(garbage.Root);
        Assert.NotNull(fromGarbage);
        Assert.Equal(Mw300EssStation.Id, fromGarbage!.SourceProjectId);
        Assert.Equal(60, fromGarbage.EssUnits.Count);

        using var missing = new TempTopologyRoot(
            engineeringMode: true,
            overlay: null,
            activeProjectId: Mw300EssStation.Id,
            writeOverlay: false);
        var fromMissing = TopologyOverlayLoader.TryLoad(missing.Root);
        Assert.NotNull(fromMissing);
        Assert.Equal(60, fromMissing!.EssUnits.Count);
    }

    [Fact]
    public void TryLoad_returns_null_when_json_corrupt()
    {
        using var tmp = new TempTopologyRoot(engineeringMode: true, overlay: UsableOverlay());
        File.WriteAllText(tmp.OverlayPath, "{ not-json");
        var log = CaptureConsole(() => Assert.Null(TopologyOverlayLoader.TryLoad(tmp.Root)));
        Assert.Contains(tmp.OverlayPath, log);
        Assert.Contains("JsonException", log);
    }

    [Fact]
    public void TryLoad_unusable_overlay_logs_path_and_falls_back()
    {
        using var tmp = new TempTopologyRoot(engineeringMode: true, overlay: new TopologyRuntimeOverlay());
        var log = CaptureConsole(() => Assert.Null(TopologyOverlayLoader.TryLoad(tmp.Root)));
        Assert.Contains(tmp.OverlayPath, log);
        Assert.Contains("改用 appsettings", log);
    }

    [Fact]
    public void IsUsable_true_when_grouped_units_have_pcs_and_bms()
    {
        Assert.True(TopologyOverlayLoader.IsUsable(new TopologyRuntimeOverlay
        {
            EssUnits =
            {
                new EssUnitConfig
                {
                    Name = "U1",
                    Groups =
                    {
                        new EmuGroupConfig
                        {
                            Name = "A",
                            Pcs = { new PcsDeviceConfig { Name = "PCS-A" } },
                            Bms = { new BmsDeviceConfig { Name = "BMS-A" } }
                        },
                        new EmuGroupConfig
                        {
                            Name = "B",
                            Pcs = { new PcsDeviceConfig { Name = "PCS-B" } },
                            Bms = { new BmsDeviceConfig { Name = "BMS-B" } }
                        }
                    }
                }
            }
        }));
    }

    [Fact]
    public void IsUsable_false_when_a_group_is_missing_pcs()
    {
        Assert.False(TopologyOverlayLoader.IsUsable(new TopologyRuntimeOverlay
        {
            EssUnits =
            {
                new EssUnitConfig
                {
                    Name = "U1",
                    Groups =
                    {
                        new EmuGroupConfig
                        {
                            Name = "空组",
                            Bms = { new BmsDeviceConfig { Name = "BMS-A" } }
                        }
                    }
                }
            }
        }));
    }

    [Fact]
    public void TryLoad_returns_overlay_when_only_pv_units()
    {
        var expected = new TopologyRuntimeOverlay
        {
            SourceProjectName = "pv-only",
            PvUnits = { new PvUnitRuntimeConfig { Name = "光伏单元-1", InverterCount = 16 } }
        };
        using var tmp = new TempTopologyRoot(engineeringMode: true, overlay: expected);
        var loaded = TopologyOverlayLoader.TryLoad(tmp.Root);
        Assert.NotNull(loaded);
        Assert.Equal("pv-only", loaded!.SourceProjectName);
        Assert.Single(loaded.PvUnits);
        Assert.Empty(loaded.EssUnits);
    }

    private static readonly object ConsoleGate = new();

    private static string CaptureConsole(Action act)
    {
        lock (ConsoleGate)
        {
            var original = Console.Out;
            var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                act();
                return sw.ToString();
            }
            finally
            {
                Console.SetOut(original);
            }
        }
    }

    private static TopologyRuntimeOverlay UsableOverlay() => new()
    {
        SourceProjectName = "fixture",
        EssUnits =
        {
            new EssUnitConfig
            {
                Name = "Unit-1",
                Pcs = { new PcsDeviceConfig { Name = "PCS-1A" } },
                Bms = { new BmsDeviceConfig { Name = "BMS-1A" } }
            }
        }
    };

    private sealed class TempTopologyRoot : IDisposable
    {
        public string Root { get; }
        public string OverlayPath { get; }

        public TempTopologyRoot(
            bool engineeringMode,
            TopologyRuntimeOverlay? overlay,
            string? activeProjectId = null,
            bool writeOverlay = true)
        {
            Root = Path.Combine(Path.GetTempPath(), "ess-overlay-" + Guid.NewGuid().ToString("N"));
            var generated = Path.Combine(Root, "configs", "topology", "generated");
            Directory.CreateDirectory(generated);
            OverlayPath = Path.Combine(generated, "runtime-overlay.json");
            File.WriteAllText(
                Path.Combine(Root, "configs", "topology", "runtime-mode.json"),
                JsonSerializer.Serialize(new TopologyRuntimeMode
                {
                    EngineeringMode = engineeringMode,
                    ActiveProjectId = activeProjectId
                }, JsonOpts));
            if (writeOverlay && overlay != null)
                File.WriteAllText(OverlayPath, JsonSerializer.Serialize(overlay, JsonOpts));
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch { /* temp */ }
        }
    }
}
