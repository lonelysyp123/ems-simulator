using EssSimulator.Display;

namespace EssSimulator.Web
{
    /// <summary>电池舱总览快照（对应 TUI DrawBatteryInfo 的总览表）。</summary>
    public sealed class BatteryOverviewDto
    {
        public int UnitIndex { get; set; }      // 0-based
        public int UnitNumber => UnitIndex + 1;
        public double TotalVoltage { get; set; }
        public double TotalCurrent { get; set; }
        public double SOC { get; set; }
        public double SOH { get; set; }
        public double MaxCellVoltage { get; set; }
        public double MinCellVoltage { get; set; }
        public int MaxCellVoltageClusterId { get; set; }
        public int MaxCellVoltagePackId { get; set; }
        public int MaxCellVoltageCellId { get; set; }
        public int MinCellVoltageClusterId { get; set; }
        public int MinCellVoltagePackId { get; set; }
        public int MinCellVoltageCellId { get; set; }
        public string GridConnectStatus { get; set; } = "";
        public string BlackStartModeStatus { get; set; } = "";
        public List<ClusterDto> Clusters { get; set; } = new();
    }

    public sealed class ClusterDto
    {
        public int ClusterId { get; set; }
        public double TotalVoltage { get; set; }
        public double TotalCurrent { get; set; }
        public double PowerKw { get; set; }
        public double SOC { get; set; }
        public double SOH { get; set; }
        public double AvgCellVoltage { get; set; }
        public double MaxCellVoltage { get; set; }
        public double MinCellVoltage { get; set; }
        public double AvgCellTemp { get; set; }
        public double MaxCellTemp { get; set; }
        public double MinCellTemp { get; set; }
        /// <summary>簇内扁平单体编号：pack * cellsPerPack + cell。</summary>
        public int MaxCellTempId { get; set; }
        public int MinCellTempId { get; set; }
        public int MaxCellTempPackId { get; set; }
        public int MaxCellTempCellId { get; set; }
        public int MinCellTempPackId { get; set; }
        public int MinCellTempCellId { get; set; }
    }

    /// <summary>电池单体电压/温度快照：4 包 × 104 节。</summary>
    public sealed class CellVoltageDto
    {
        public int UnitIndex { get; set; }
        public int ClusterId { get; set; }
        public int PackCount { get; set; }
        public int CellsPerPack { get; set; }
        /// <summary>按 pack 分组的单体电压（单位 V）；外层索引=包号，内层=单体在该包内的序号。</summary>
        public List<List<float>> Packs { get; set; } = new();
        /// <summary>按 pack 分组的单体温度（单位 °C）；布局与 <see cref="Packs"/> 相同。</summary>
        public List<List<float>> TempPacks { get; set; } = new();
        public double MinCellVoltage { get; set; }
        public double MaxCellVoltage { get; set; }
        public int MaxCellVoltagePackId { get; set; }
        public int MaxCellVoltageCellId { get; set; }
        public int MinCellVoltagePackId { get; set; }
        public int MinCellVoltageCellId { get; set; }
        public double MinCellTemp { get; set; }
        public double MaxCellTemp { get; set; }
        public int MaxCellTempPackId { get; set; }
        public int MaxCellTempCellId { get; set; }
        public int MinCellTempPackId { get; set; }
        public int MinCellTempCellId { get; set; }
        /// <summary>当前舱（BMS 堆）热网络电池节点温度（°C），与功率降额/BMS 过温保护同一感温。</summary>
        public double BatteryNodeTempC { get; set; }
    }

    public static class BatterySnapshotReader
    {
        public static BatteryOverviewDto ReadOverview(int unitIndex0)
        {
            string basePath = $"bms{unitIndex0 + 1}.BatteryStacks[0]";

            double totVolt = GuiSimDataAccess.SafeGetDouble($"{basePath}.TotalVoltage");
            double totCurr = GuiSimDataAccess.SafeGetDouble($"{basePath}.Current");
            double soc = GuiSimDataAccess.SafeGetDouble($"{basePath}.SOC") * 100;
            double soh = GuiSimDataAccess.SafeGetDouble($"{basePath}.SOH") * 100;
            double maxCellV = GuiSimDataAccess.SafeGetDouble($"{basePath}.MaxCellVoltage");
            double minCellV = GuiSimDataAccess.SafeGetDouble($"{basePath}.MinCellVoltage");
            int maxClusterId = (int)GuiSimDataAccess.SafeGetDouble($"{basePath}.MaxCellVoltageClusterId");
            int maxPackId = (int)GuiSimDataAccess.SafeGetDouble($"{basePath}.MaxCellVoltagePackId");
            int maxCellId = (int)GuiSimDataAccess.SafeGetDouble($"{basePath}.MaxCellVoltageCellId");
            int minClusterId = (int)GuiSimDataAccess.SafeGetDouble($"{basePath}.MinCellVoltageClusterId");
            int minPackId = (int)GuiSimDataAccess.SafeGetDouble($"{basePath}.MinCellVoltagePackId");
            int minCellId = (int)GuiSimDataAccess.SafeGetDouble($"{basePath}.MinCellVoltageCellId");

            var dto = new BatteryOverviewDto
            {
                UnitIndex = unitIndex0,
                TotalVoltage = totVolt,
                TotalCurrent = totCurr,
                SOC = soc,
                SOH = soh,
                MaxCellVoltage = maxCellV,
                MinCellVoltage = minCellV,
                MaxCellVoltageClusterId = maxClusterId,
                MaxCellVoltagePackId = maxPackId,
                MaxCellVoltageCellId = maxCellId,
                MinCellVoltageClusterId = minClusterId,
                MinCellVoltagePackId = minPackId,
                MinCellVoltageCellId = minCellId,
                GridConnectStatus = GuiStatusFormatters.FormatGridConnectStatus(unitIndex0),
                BlackStartModeStatus = GuiStatusFormatters.FormatBlackStartModeStatus(unitIndex0)
            };

            int clusterCount = GuiSimDataAccess.GetClusterCount();
            // 与点表/ReadCells 一致：默认 4 包 × 104；若可读到 PackCount 则优先
            int cellsPerPack = 104;
            for (int i = 0; i < clusterCount; i++)
            {
                string clusterPath = $"{basePath}.Cluseter[{i}]";
                double cCurr = GuiSimDataAccess.SafeGetDouble($"{clusterPath}.Measurements.Current");
                double cVolt = GuiSimDataAccess.SafeGetDouble($"{clusterPath}.Measurements.TotalVoltage");
                double cSoc = GuiSimDataAccess.SafeGetDouble($"{clusterPath}.Measurements.SOC") * 100;
                double cSoh = GuiSimDataAccess.SafeGetDouble($"{clusterPath}.Measurements.SOH") * 100;
                double cAvg = GuiSimDataAccess.SafeGetDouble($"{clusterPath}.Measurements.AvgCellVoltage");
                double cMax = GuiSimDataAccess.SafeGetDouble($"{clusterPath}.Measurements.MaxCellVoltage");
                double cMin = GuiSimDataAccess.SafeGetDouble($"{clusterPath}.Measurements.MinCellVoltage");
                double avgTemp = GuiSimDataAccess.SafeGetDouble($"{clusterPath}.Measurements.AvgCellTemp");
                double maxTemp = GuiSimDataAccess.SafeGetDouble($"{clusterPath}.Measurements.MaxCellTemp");
                double minTemp = GuiSimDataAccess.SafeGetDouble($"{clusterPath}.Measurements.MinCellTemp");
                int maxTempId = (int)GuiSimDataAccess.SafeGetDouble($"{clusterPath}.Measurements.MaxCellTempId");
                int minTempId = (int)GuiSimDataAccess.SafeGetDouble($"{clusterPath}.Measurements.MinCellTempId");

                dto.Clusters.Add(new ClusterDto
                {
                    ClusterId = i,
                    TotalVoltage = cVolt,
                    TotalCurrent = cCurr,
                    PowerKw = cCurr * cVolt / 1000.0,
                    SOC = cSoc,
                    SOH = cSoh,
                    AvgCellVoltage = cAvg,
                    MaxCellVoltage = cMax,
                    MinCellVoltage = cMin,
                    AvgCellTemp = avgTemp,
                    MaxCellTemp = maxTemp,
                    MinCellTemp = minTemp,
                    MaxCellTempId = maxTempId,
                    MinCellTempId = minTempId,
                    MaxCellTempPackId = cellsPerPack > 0 ? maxTempId / cellsPerPack : 0,
                    MaxCellTempCellId = cellsPerPack > 0 ? maxTempId % cellsPerPack : maxTempId,
                    MinCellTempPackId = cellsPerPack > 0 ? minTempId / cellsPerPack : 0,
                    MinCellTempCellId = cellsPerPack > 0 ? minTempId % cellsPerPack : minTempId
                });
            }

            return dto;
        }

        public static CellVoltageDto ReadCells(int unitIndex0, int clusterId)
        {
            string basePath = $"bms{unitIndex0 + 1}.BatteryStacks[0]";
            int clusterCount = Math.Max(1, GuiSimDataAccess.GetClusterCount());
            clusterId = Math.Clamp(clusterId, 0, clusterCount - 1);

            const int packCount = 4;
            const int cellsPerPack = 104;
            string clusterPath = $"{basePath}.Cluseter[{clusterId}]";

            var dto = new CellVoltageDto
            {
                UnitIndex = unitIndex0,
                ClusterId = clusterId,
                PackCount = packCount,
                CellsPerPack = cellsPerPack,
                MaxCellVoltage = GuiSimDataAccess.SafeGetDouble($"{clusterPath}.Measurements.MaxCellVoltage"),
                MinCellVoltage = GuiSimDataAccess.SafeGetDouble($"{clusterPath}.Measurements.MinCellVoltage"),
                MaxCellTemp = GuiSimDataAccess.SafeGetDouble($"{clusterPath}.Measurements.MaxCellTemp"),
                MinCellTemp = GuiSimDataAccess.SafeGetDouble($"{clusterPath}.Measurements.MinCellTemp"),
                BatteryNodeTempC = GuiSimDataAccess.SafeGetDouble(
                    $"ess._bmsRackDevices[{unitIndex0}].BatteryNodeTemperatureCelsius", 25)
            };

            int maxIdFlat = (int)GuiSimDataAccess.SafeGetDouble($"{clusterPath}.Measurements.MaxCellVoltageId");
            int minIdFlat = (int)GuiSimDataAccess.SafeGetDouble($"{clusterPath}.Measurements.MinCellVoltageId");
            dto.MaxCellVoltagePackId = maxIdFlat / cellsPerPack;
            dto.MaxCellVoltageCellId = maxIdFlat % cellsPerPack;
            dto.MinCellVoltagePackId = minIdFlat / cellsPerPack;
            dto.MinCellVoltageCellId = minIdFlat % cellsPerPack;

            int maxTempFlat = (int)GuiSimDataAccess.SafeGetDouble($"{clusterPath}.Measurements.MaxCellTempId");
            int minTempFlat = (int)GuiSimDataAccess.SafeGetDouble($"{clusterPath}.Measurements.MinCellTempId");
            dto.MaxCellTempPackId = maxTempFlat / cellsPerPack;
            dto.MaxCellTempCellId = maxTempFlat % cellsPerPack;
            dto.MinCellTempPackId = minTempFlat / cellsPerPack;
            dto.MinCellTempCellId = minTempFlat % cellsPerPack;

            for (int pack = 0; pack < packCount; pack++)
            {
                var packCells = new List<float>(cellsPerPack);
                var packTemps = new List<float>(cellsPerPack);
                for (int c = 0; c < cellsPerPack; c++)
                {
                    int cellIdx = pack * cellsPerPack + c;
                    float v = 0;
                    float t = 0;
                    try
                    {
                        v = (float)SimServer.GetExtIfVariableVal(
                            $"{clusterPath}.ClusterCellVoltages.CellVoltages[{cellIdx}]");
                    }
                    catch { v = 0; }
                    try
                    {
                        t = (float)SimServer.GetExtIfVariableVal(
                            $"{clusterPath}.ClusterCellTemperatures.CellTemperatures[{cellIdx}]");
                    }
                    catch { t = 0; }

                    packCells.Add(v);
                    packTemps.Add(t);
                }
                dto.Packs.Add(packCells);
                dto.TempPacks.Add(packTemps);
            }

            return dto;
        }
    }
}
