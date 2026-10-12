using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel.Battery;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.EssDeviceSimModel.Solver
{
    public static class NetworkTopologyBuilder
    {
        public static ElectricalNetwork Build(
            SimulatorConfig simCfg,
            PcsPhysicalConfig pcsCfg,
            TransformerConfig mainTransCfg,
            UnitTransformerConfig unitTransCfg,
            LoadConfig loadCfg,
            PccConfig pccCfg,
            BreakerConfig? breakerCfg = null,
            MeterConfig? meterCfg = null,
            IReadOnlyList<BmsRackDevice>? bmsRackDevices = null,
            IReadOnlyList<PcsDevice>? externalPcsDevices = null,
            TransformerDevice? externalMainTransformer = null,
            IReadOnlyList<TransformerDevice>? externalUnitTransformers = null,
            IReadOnlyList<IReadOnlyList<DualEarTransformerDevice>>? externalDualEars = null,
            LoadDevice? externalLoadDevice = null,
            MeterSimulator? externalPccMeter = null,
            EnergyStorageSystem? legacyEss = null,
            IReadOnlyList<int>? pcsPerUnit = null)
        {
            breakerCfg ??= new BreakerConfig();
            meterCfg ??= new MeterConfig();

            int unitCount = simCfg.EffectiveEssUnitCount;

            var gridConfig = new GridConfig
            {
                NominalLineVoltageV = pccCfg.NominalLineVoltage,
                ShortCircuitMva = pccCfg.ShortCircuitMva,
                MaxVoltageShiftPercent = pccCfg.MaxVoltageShiftPercent,
                ReactiveVoltageInfluenceCoefficient = pccCfg.ReactiveVoltageInfluenceCoefficient,
                Connection = ThreePhaseConnection.Star
            };

            var mainTransDeviceCfg = TransformerDeviceFactory.CreateConfig(mainTransCfg);
            var unitTransDeviceCfg = TransformerDeviceFactory.CreateConfig(unitTransCfg);

            var loadDeviceCfg = new LoadDeviceConfig
            {
                ActivePowerKw = loadCfg.ActivePowerPlan,
                ReactivePowerKvar = loadCfg.ReactivePowerPlan,
                Connection = ThreePhaseConnection.Star
            };

            var topology = BuildTopology(
                unitCount, pccCfg, unitTransCfg, meterCfg.PccMeter.SourceBusId, mainTransCfg.Present);
            var dcLinks = BuildDcLinks(unitCount, pcsPerUnit);

            var grid = new GridSimulator("grid", gridConfig);
            var mainBreaker = new BreakerSimulator("main_breaker", breakerCfg.Main);
            var mainTransformer = externalMainTransformer
                ?? TransformerDeviceFactory.Create("main_transformer", mainTransDeviceCfg);
            var load = externalLoadDevice ?? new LoadDevice("load_35", loadDeviceCfg);
            var meter = externalPccMeter ?? new MeterSimulator("pcc_meter", meterCfg.PccMeter);

            var unitBreakers = new List<BreakerSimulator>();
            var unitTransformers = new List<TransformerDevice>();
            var dualEars = new List<IReadOnlyList<DualEarTransformerDevice>>();
            var splitAssignments = new List<SplitEarAssignment?>();
            var networkPcsDevices = new List<PcsDevice>();
            var bmsDevices = new List<BmsRackDevice>();

            for (int u = 0; u < unitCount; u++)
            {
                unitBreakers.Add(new BreakerSimulator($"unit_breaker_u{u}", breakerCfg.Unit));
                var (baseIdx, pcsCount) = PcsUnitLayout.RangeOfUnit(pcsPerUnit, u);
                var splitCfgs = u < simCfg.Devices.Count
                    ? simCfg.Devices[u].ResolveSplitTransformers()
                    : Array.Empty<SplitTransformerRuntimeConfig>();
                IReadOnlyList<DualEarTransformerDevice> duals =
                    externalDualEars != null && u < externalDualEars.Count
                        ? externalDualEars[u]
                        : CreateDualEars(u, splitCfgs);

                if (duals.Count > 0)
                {
                    dualEars.Add(duals);
                    if (externalUnitTransformers != null && u < externalUnitTransformers.Count)
                        unitTransformers.Add(externalUnitTransformers[u]);
                    else
                        unitTransformers.Add(duals[0].Through);
                    splitAssignments.Add(ToSplitAssignment(splitCfgs, baseIdx, pcsCount));
                }
                else
                {
                    dualEars.Add(Array.Empty<DualEarTransformerDevice>());
                    splitAssignments.Add(null);
                    if (externalUnitTransformers != null && u < externalUnitTransformers.Count)
                        unitTransformers.Add(externalUnitTransformers[u]);
                    else
                        unitTransformers.Add(TransformerDeviceFactory.Create($"unit_transformer_u{u}", unitTransDeviceCfg));
                }

                for (int ch = 0; ch < pcsCount; ch++)
                {
                    int channel = baseIdx + ch;
                    if (externalPcsDevices != null && channel < externalPcsDevices.Count)
                        networkPcsDevices.Add(externalPcsDevices[channel]);
                    else
                        networkPcsDevices.Add(PcsDeviceFactory.Create(
                            $"pcs_u{u}_ch{ch}",
                            PcsDeviceFactory.CreateConfig(pcsCfg, simCfg.Runtime.PcsRamp)));
                    if (bmsRackDevices != null && channel < bmsRackDevices.Count)
                        bmsDevices.Add(bmsRackDevices[channel]);
                    else
                        bmsDevices.Add(new BmsRackDevice($"bms_u{u}_ch{ch}", BmsRackFactory.CreateRack(new Configuration.BmsDeviceConfig())));
                }
            }

            var network = new ElectricalNetwork
            {
                Topology = topology,
                Grid = grid,
                MainBreaker = mainBreaker,
                MainTransformer = mainTransformer,
                Load = load,
                PccMeter = meter,
                UnitBreakers = unitBreakers,
                UnitTransformers = unitTransformers,
                DualEarTransformers = dualEars,
                SplitEarAssignments = splitAssignments,
                PcsDevices = networkPcsDevices,
                BmsDevices = bmsDevices,
                DcLinks = dcLinks,
                PcsPerUnit = pcsPerUnit ?? Array.Empty<int>(),
                PccLineVoltageV = pccCfg.NominalLineVoltage,
                StationBus35LineVoltageV = pccCfg.StationBusNominalLineVoltage,
                HasMainTransformer = mainTransCfg.Present
            };

            return network;
        }

        private static NetworkTopology BuildTopology(
            int unitCount,
            PccConfig pccCfg,
            UnitTransformerConfig unitTransCfg,
            string? pccMeterSourceBusId,
            bool hasMainTransformer)
        {
            var config = new ElectricalTopologyConfig
            {
                Version = 1,
                DefaultAcConnection = ThreePhaseConnection.Star,
                DefaultFrequencyHz = 50,
                Buses =
                {
                    new BusDefinition
                    {
                        Id = RuntimeBusIds.Grid,
                        NominalLineVoltageV = pccCfg.NominalLineVoltage,
                        Connection = ThreePhaseConnection.Star,
                        Description = "PCC grid"
                    },
                    new BusDefinition
                    {
                        Id = RuntimeBusIds.AfterMainBreaker,
                        NominalLineVoltageV = pccCfg.NominalLineVoltage,
                        Connection = ThreePhaseConnection.Star
                    },
                    new BusDefinition
                    {
                        Id = RuntimeBusIds.Station35,
                        NominalLineVoltageV = pccCfg.StationBusNominalLineVoltage,
                        Connection = ThreePhaseConnection.Star,
                        Description = "station bus"
                    }
                },
                SeriesLinks =
                {
                    new SeriesLinkDefinition
                    {
                        LinkId = "L_MAIN_BRK",
                        DeviceId = "main_breaker",
                        DeviceKind = ElectricalDeviceKind.Breaker,
                        UpstreamBusId = RuntimeBusIds.Grid,
                        DownstreamBusId = hasMainTransformer
                            ? RuntimeBusIds.AfterMainBreaker
                            : RuntimeBusIds.Station35
                    }
                },
                MeasurementTaps =
                {
                    new MeasurementTapDefinition
                    {
                        MeterDeviceId = "pcc_meter",
                        SourceDeviceId = pccMeterSourceBusId ?? RuntimeBusIds.AfterMainBreaker,
                        SourcePortId = "bus"
                    }
                }
            };

            if (hasMainTransformer)
            {
                config.SeriesLinks.Add(new SeriesLinkDefinition
                {
                    LinkId = "L_MAIN_XFMR",
                    DeviceId = "main_transformer",
                    DeviceKind = ElectricalDeviceKind.Transformer,
                    UpstreamBusId = RuntimeBusIds.AfterMainBreaker,
                    DownstreamBusId = RuntimeBusIds.Station35
                });
            }

            for (int u = 0; u < unitCount; u++)
            {
                config.Buses.Add(new BusDefinition
                {
                    Id = $"BUS_35_U{u}",
                    NominalLineVoltageV = pccCfg.StationBusNominalLineVoltage,
                    Connection = ThreePhaseConnection.Star
                });

                config.Buses.Add(new BusDefinition
                {
                    Id = $"BUS_690_U{u}",
                    NominalLineVoltageV = unitTransCfg.SecondaryVoltage,
                    Connection = ThreePhaseConnection.Star,
                    Description = $"Unit-{u + 1} 690V bus"
                });

                config.SeriesLinks.Add(new SeriesLinkDefinition
                {
                    LinkId = $"L_UNIT{u}_BRK",
                    DeviceId = $"unit_breaker_u{u}",
                    DeviceKind = ElectricalDeviceKind.Breaker,
                    UpstreamBusId = "BUS_35",
                    DownstreamBusId = $"BUS_35_U{u}"
                });

                config.SeriesLinks.Add(new SeriesLinkDefinition
                {
                    LinkId = $"L_UNIT{u}_XFMR",
                    DeviceId = $"unit_transformer_u{u}",
                    DeviceKind = ElectricalDeviceKind.Transformer,
                    UpstreamBusId = $"BUS_35_U{u}",
                    DownstreamBusId = $"BUS_690_U{u}"
                });
            }

            return ElectricalTopologyFactory.FromConfig(config);
        }

        private static List<DcLink> BuildDcLinks(int unitCount, IReadOnlyList<int>? pcsPerUnit)
        {
            var links = new List<DcLink>();
            for (int u = 0; u < unitCount; u++)
            {
                int pcsCount = PcsUnitLayout.CountOfUnit(pcsPerUnit, u);
                for (int ch = 0; ch < pcsCount; ch++)
                {
                    links.Add(new DcLink
                    {
                        LinkId = $"DC_U{u}_CH{ch}",
                        PcsDeviceId = $"pcs_u{u}_ch{ch}",
                        BmsDeviceId = $"bms_u{u}_ch{ch}",
                        DefaultClosed = true,
                        IsClosed = true
                    });
                }
            }

            return links;
        }

        private static IReadOnlyList<DualEarTransformerDevice> CreateDualEars(
            int unitIndex, IReadOnlyList<SplitTransformerRuntimeConfig> splitCfgs)
        {
            var list = new List<DualEarTransformerDevice>();
            for (int t = 0; t < splitCfgs.Count; t++)
            {
                string id = t == 0 ? $"unit_transformer_u{unitIndex}" : $"unit_transformer_u{unitIndex}_t{t}";
                list.Add(TransformerDeviceFactory.CreateDualEar(id, splitCfgs[t]));
            }

            return list;
        }

        private static SplitEarAssignment ToSplitAssignment(
            IReadOnlyList<SplitTransformerRuntimeConfig> splitCfgs, int baseIdx, int pcsCount)
        {
            var taken = new HashSet<int>();
            var transformers = new List<SplitXfmrEarChannels>();
            foreach (var splitCfg in splitCfgs)
            {
                var left = new List<int>();
                var right = new List<int>();
                foreach (var i in splitCfg.RightEarPcsIndices)
                {
                    int global = baseIdx + i;
                    if (i < 0 || i >= pcsCount || !taken.Add(global))
                        continue;
                    right.Add(global);
                }

                foreach (var i in splitCfg.LeftEarPcsIndices)
                {
                    int global = baseIdx + i;
                    if (i < 0 || i >= pcsCount || !taken.Add(global))
                        continue;
                    left.Add(global);
                }

                transformers.Add(new SplitXfmrEarChannels { LeftChannels = left, RightChannels = right });
            }

            if (transformers.Count > 0)
            {
                var firstLeft = transformers[0].LeftChannels.ToList();
                for (int ch = 0; ch < pcsCount; ch++)
                {
                    int global = baseIdx + ch;
                    if (taken.Add(global))
                        firstLeft.Add(global);
                }

                transformers[0] = new SplitXfmrEarChannels
                {
                    LeftChannels = firstLeft,
                    RightChannels = transformers[0].RightChannels
                };
            }

            return new SplitEarAssignment { Transformers = transformers };
        }
    }
}
