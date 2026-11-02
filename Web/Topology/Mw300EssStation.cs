using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Web.Topology
{
    /// <summary>
    /// 300MW 纯储能测试工程。拓扑和端口都是常量，不读画布 JSON，也不读 protocol-ports.json。
    /// 电气模型仍是一台 220/35 kV 主变；三段只体现在单元名称上。
    /// </summary>
    public static class Mw300EssStation
    {
        public const string Id = "mw300-ess-test";
        public const string Name = "300MW纯储能测试工程";
        public const string LockedMessage = "300MW 纯储能测试工程的拓扑和端口已写死，不能修改";

        public const int UnitCount = 60;
        public const int PcsPerUnit = 2;
        public const int UnitsPerSection = 20;
        public const int SectionCount = 3;

        public const int PortBase = 10000;
        public const int EmPort = 1500;
        public const int SelPort = 2001;
        public const int SvgPort = 2201;

        public const byte LcSlaveId = 1;
        public const byte FirstPcsSlaveId = 2;
        public const byte FirstBmsSlaveId = 10;
        public const byte SecondBmsSlaveId = 42;
        public const int BmsRackCount = 12;

        public const double PcsRatedKw = 2500;
        public const double PcsMaxKw = 2750;
        public const double PcsMaxCurrentA = 2300;
        public const double MainTransformerKva = 300000;
        public const double UnitTransformerKva = 6300;
        public const double SvgRatedKvar = 10000;

        public static bool IsThis(string? projectId) =>
            string.Equals(projectId, Id, StringComparison.OrdinalIgnoreCase);

        public static int PortForUnit(int unit1Based) => PortBase + unit1Based;

        public static TopologyRuntimeOverlay BuildOverlay()
        {
            var overlay = new TopologyRuntimeOverlay
            {
                SourceProjectId = Id,
                SourceProjectName = Name,
                PvUnits = new List<PvUnitRuntimeConfig>(),
                EnableSvg = true,
                SvgRatedCapacityKvar = SvgRatedKvar,
                Pcs = new PcsPhysicalConfig
                {
                    RatedPower = PcsRatedKw,
                    MaxPower = PcsMaxKw,
                    MaxCurrent = PcsMaxCurrentA,
                    AcVoltageNominal = 690
                },
                Transformer = new TransformerConfig
                {
                    Present = true,
                    RatedPower = MainTransformerKva,
                    PrimaryVoltage = 220000,
                    SecondaryVoltage = 35000
                },
                UnitTransformer = new UnitTransformerConfig
                {
                    RatedPower = UnitTransformerKva,
                    PrimaryVoltage = 35000,
                    SecondaryVoltage = 690
                },
                Pcc = new PccConfig
                {
                    NominalLineVoltage = 220000,
                    StationBusNominalLineVoltage = 35000
                },
                Notes =
                {
                    "写死测试工程：拓扑与端口不可编辑",
                    "60 个储能单元，每单元 2 台 2500 kW PCS，合计 300 MW",
                    "名称按每 20 台分为 3 段；电气上仍是一台 220/35 kV 主变"
                }
            };

            for (int i = 0; i < UnitCount; i++)
            {
                int section = i / UnitsPerSection + 1;
                int indexInSection = i % UnitsPerSection + 1;
                string unitName = $"S{section}-U{indexInSection:D2}";
                overlay.EssUnits.Add(new EssUnitConfig
                {
                    Name = unitName,
                    Pcs =
                    {
                        new PcsDeviceConfig { Name = $"{unitName}-A" },
                        new PcsDeviceConfig { Name = $"{unitName}-B" }
                    },
                    Bms =
                    {
                        NewBms($"{unitName}-BMS-A"),
                        NewBms($"{unitName}-BMS-B")
                    }
                });
            }

            return overlay;
        }

        /// <summary>
        /// 一个储能单元一个 TCP 端口。PCS 通道全局编号，故 BMS / EMU 各 120 台，LC 60 路。
        /// </summary>
        public static ProtocolPortPlan BuildPortPlan()
        {
            var plan = new ProtocolPortPlan();
            for (int unit0 = 0; unit0 < UnitCount; unit0++)
            {
                int unit1 = unit0 + 1;
                int port = PortForUnit(unit1);
                plan.Entries.Add(Entry($"simLc{unit1}", ProtocolDeviceType.Lc, "lc.csv", port, LcSlaveId, lcGroupCount: 1));
                for (int pcs = 0; pcs < PcsPerUnit; pcs++)
                {
                    int sim = unit0 * PcsPerUnit + pcs + 1;
                    byte pcsSlave = (byte)(FirstPcsSlaveId + pcs);
                    byte bmsSlave = pcs == 0 ? FirstBmsSlaveId : SecondBmsSlaveId;
                    plan.Entries.Add(Entry($"simEmu{sim}", ProtocolDeviceType.Emu, "emu.csv", port, pcsSlave));
                    plan.Entries.Add(Entry($"simBms{sim}", ProtocolDeviceType.Bms, "bms_bank.csv", port, bmsSlave, rackCount: BmsRackCount));
                }
            }

            plan.Entries.Add(Entry("simEm", ProtocolDeviceType.Em, "em.csv", EmPort, 1));
            plan.Entries.Add(Entry("simSel", ProtocolDeviceType.Sel, "sel.csv", SelPort, 1));
            plan.Entries.Add(Entry("simSvg", ProtocolDeviceType.Svg, "svg.csv", SvgPort, 1));
            return plan;
        }

        public static void ApplyRuntimePolicy(string? stationProfileId)
        {
            if (IsThis(stationProfileId))
                BatteryCalcMode.CellPhysicsEnabled = false;
        }

        /// <summary>该工程下拒绝切换逐芯。返回 false 表示请求被拒绝。</summary>
        public static bool TrySetCellPhysics(string? stationProfileId, bool enabled)
        {
            if (IsThis(stationProfileId))
            {
                BatteryCalcMode.CellPhysicsEnabled = false;
                return false;
            }

            BatteryCalcMode.CellPhysicsEnabled = enabled;
            return true;
        }

        public static bool AllowsPortOverride(string? stationProfileId) => !IsThis(stationProfileId);

        /// <summary>
        /// 写端口覆盖或删除覆盖文件。测试工程直接拒绝，不碰 protocol-ports.json。
        /// </summary>
        public static bool TryPersistPortOverrides(
            string? stationProfileId,
            IEnumerable<ProtocolPortEntry>? entries,
            bool clear,
            out string? error)
        {
            if (!AllowsPortOverride(stationProfileId))
            {
                error = LockedMessage;
                return false;
            }

            error = null;
            if (clear)
                ProtocolPortPlan.ClearOverrides();
            else
                ProtocolPortPlan.SaveOverrides(entries ?? Array.Empty<ProtocolPortEntry>());
            return true;
        }

        public static TopologyRuntimeOverlay ApplyToStore(TopologyStore store)
        {
            var overlay = BuildOverlay();
            store.SaveOverlay(overlay);
            store.SaveRuntimeMode(new TopologyRuntimeMode
            {
                EngineeringMode = true,
                ActiveProjectId = Id,
                ActiveProjectName = Name
            });
            return overlay;
        }

        public static IReadOnlyList<TopologyProjectSummary> WithBuiltin(IEnumerable<TopologyProjectSummary> projects)
        {
            var list = projects.Where(p => !IsThis(p.Id)).ToList();
            list.Insert(0, new TopologyProjectSummary
            {
                Id = Id,
                Name = Name,
                EmuCount = UnitCount,
                PvCount = 0,
                NodeCount = 0,
                ReadOnly = true,
                UpdatedAtUtc = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc)
            });
            return list;
        }

        private static BmsDeviceConfig NewBms(string name) => new()
        {
            Name = name,
            ClusterCount = BmsRackCount,
            PackCount = 4,
            CellSeriesCount = 104
        };

        private static ProtocolPortEntry Entry(
            string name,
            ProtocolDeviceType type,
            string pointMapFile,
            int port,
            byte slaveId,
            int rackCount = 0,
            int lcGroupCount = 0)
        {
            return new ProtocolPortEntry
            {
                Name = name,
                Type = type,
                PointMapFile = pointMapFile,
                RackCount = rackCount,
                LcGroupCount = lcGroupCount,
                DefaultPort = port,
                DefaultSlaveId = slaveId,
                Port = port,
                SlaveId = slaveId
            };
        }
    }
}
