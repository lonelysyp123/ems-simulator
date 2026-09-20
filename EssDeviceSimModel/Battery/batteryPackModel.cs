using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EssSimulator.EssDeviceSimModel
{
    // 模组配置
    public class PackConfiguration
    {
        public int SeriesCount { get; set; }    // 串联电芯数量
        public int ParallelCount { get; set; }  // 并联电芯数量
        public double NominalVoltage { get; set; } // 单芯额定电压(V)
        public double NominalCapacity { get; set; } // 单芯额定容量(Ah)
        public double InitialSoc { get; set; } = 0.5; // 电芯初始SOC(0-1)，建模时精确使用
        /// <summary>已废弃：初始 SOC 不再施加随机扰动，保留仅兼容旧配置。</summary>
        public double InitialSocRandomRange { get; set; } = 0;
        public double PackInternalResistance { get; set; } // 模组总内阻(Ohm)
        public double CoolingEfficiency { get; set; } = 0.7; // 冷却系统效率(0-1)
    }

    // 模组状态
    public class PackState
    {
        public double TotalVoltage { get; set; }       // 模组总电压(V)
        public double TotalCurrent { get; set; }       // 模组总电流(A)
        public double MaxCellVoltage { get; set; }     // 最大单体电压(V)
        public double MinCellVoltage { get; set; }     // 最小单体电压(V)
        public double AvgCellVoltage { get; set; }     // 平均单体电压(V)
        public double MaxCellTemp { get; set; }        // 最高单体温度(°C)
        public double MinCellTemp { get; set; }        // 最低单体温度(°C)
        public double AvgCellTemp { get; set; }        // 平均单体温度(°C)
        public double MaxSOC { get; set; }             // 最大单体SOC
        public double MinSOC { get; set; }             // 最小单体SOC
        public double AvgSOC { get; set; }             // 平均SOC
        public double StateOfHealth { get; set; }      // 模组健康度(0-1)
        public double RemainingCapacity { get; set; }   // 剩余容量(Ah)
        public double TotalCapacity { get; set; }      // 当前总容量(Ah)
        public DateTime Timestamp { get; set; }        // 状态时间
        public List<CellState> CellStates { get; set; } // 所有电芯状态
    }

    public class BatteryPackSimulator
    {
        private readonly PackConfiguration _config;
        private readonly List<List<LiFePO4CellSimulator>> _cells; // 二维数组表示串并联结构
        private readonly Random _random = new Random(0);
        private PackState _currentState;

        // 构造函数
        public BatteryPackSimulator(PackConfiguration config)
        {
            _config = config;
            _cells = new List<List<LiFePO4CellSimulator>>();

            // 初始化所有电芯
            for (int s = 0; s < config.SeriesCount; s++)
            {
                var parallelCells = new List<LiFePO4CellSimulator>();
                for (int p = 0; p < config.ParallelCount; p++)
                {
                    // 创建电芯规格 - 添加随机差异模拟不一致性
                    var cellSpec = new CellSpecifications
                    {
                        NominalCapacity = config.NominalCapacity * (1 + (_random.NextDouble() - 0.5) * 0.02), // ±2%容量差异
                        NominalVoltage = config.NominalVoltage,
                        MinVoltage = 2.5,
                        MaxVoltage = 3.65,
                        InitialSOC = Math.Clamp(config.InitialSoc, 0.0, 1.0),
                        InternalResistance = 0.0002 * (1 + (_random.NextDouble() - 0.5) * 0.1), // ±10%内阻差异
                        Mass = 0.05,
                        Volume = 0.0001
                    };

                    parallelCells.Add(new LiFePO4CellSimulator(cellSpec));
                }
                _cells.Add(parallelCells);
            }

            UpdatePackState(0, 25.0, DateTime.Now);
        }

        // 获取当前模组状态
        public PackState GetPackState() => _currentState;
        public PackConfiguration GetPackConfiguration() => _config;

        /// <summary>热设模组内全部电芯 SOC，并刷新模组汇总。</summary>
        public void SetSoc(double soc)
        {
            foreach (var series in _cells)
            {
                foreach (var cell in series)
                    cell.SetSoc(soc);
            }

            UpdatePackState(_currentState?.TotalCurrent ?? 0, _currentState?.AvgCellTemp ?? 25.0, DateTime.UtcNow);
        }

        // 更新模组状态
        /// <param name="nodeTempC">电池节点温度（°C），作为电芯热环境。</param>
        public void Update(double packCurrent, double nodeTempC, DateTime timeStamp, TimeSpan timeStep)
        {
            // 计算每个电芯的电流 (考虑并联)
            double parallelCurrent = packCurrent / _config.ParallelCount;

            // 更新每个电芯
            // 1. 先更新所有电芯状态
            for (int s = 0; s < _config.SeriesCount; s++)
            {
                for (int p = 0; p < _config.ParallelCount; p++)
                {
                    _cells[s][p].Update(parallelCurrent, nodeTempC, timeStamp, timeStep);
                }
            }

            // 2. 热扩散：让每个cell温度向相邻电芯靠拢，模拟Pack内传热平滑温差
            // 只对串联方向做一维扩散
            int totalSeries = _config.SeriesCount;
            for (int p = 0; p < _config.ParallelCount; p++)
            {
                var temps = new double[totalSeries];
                for (int s = 0; s < totalSeries; s++)
                {
                    temps[s] = _cells[s][p].GetCurrentState().Temperature;
                }
                double diffusionCoeff = 0.2;
                var newTemps = new double[totalSeries];
                for (int s = 0; s < totalSeries; s++)
                {
                    double neighborSum = 0;
                    int neighborCount = 0;
                    if (s > 0)
                    {
                        neighborSum += temps[s - 1];
                        neighborCount++;
                    }
                    if (s < totalSeries - 1)
                    {
                        neighborSum += temps[s + 1];
                        neighborCount++;
                    }
                    double avgNeighbor = neighborCount > 0 ? neighborSum / neighborCount : temps[s];
                    newTemps[s] = temps[s] + diffusionCoeff * (avgNeighbor - temps[s]);
                }
                // 回写温度
                for (int s = 0; s < totalSeries; s++)
                {
                    _cells[s][p].SetCellTemperature(newTemps[s]);
                }
            }

            // 4. 更新模组状态
            UpdatePackState(packCurrent, nodeTempC, timeStamp);
        }

        // 计算并更新模组状态
        private void UpdatePackState(double packCurrent, double ambientTemp, DateTime timeStamp)
        {
            var cellStates = _cells.SelectMany(s => s.Select(p => p.GetCurrentState())).ToList();

            // 计算电压相关参数
            double totalVoltage = _cells.Sum(s => s.Average(p => p.GetCurrentState().Voltage));
            var cellVoltages = cellStates.Select(c => c.Voltage).ToList();

            // 计算温度相关参数
            var cellTemps = cellStates.Select(c => c.Temperature).ToList();

            // 计算SOC相关参数
            var cellSOCs = cellStates.Select(c => c.SOC).ToList();

            // 计算容量相关参数
            // Pack 剩余容量 = 最弱串联段的并联之和（短板效应）
            double remainingCapacity = 0;
            for (int s = 0; s < _config.SeriesCount; s++)
            {
                double seriesSegmentCapacity = _cells[s].Sum(c => c.GetCurrentState().RemainingCapacity);
                if (s == 0 || seriesSegmentCapacity < remainingCapacity)
                    remainingCapacity = seriesSegmentCapacity;
            }
            double totalCapacity = _config.NominalCapacity * _config.ParallelCount * cellStates.Average(c => 1 - c.Age);

            // 更新模组状态
            _currentState = new PackState
            {
                TotalVoltage = totalVoltage,
                TotalCurrent = packCurrent,
                MaxCellVoltage = cellVoltages.Max(),
                MinCellVoltage = cellVoltages.Min(),
                AvgCellVoltage = cellVoltages.Average(),
                MaxCellTemp = cellTemps.Max(),
                MinCellTemp = cellTemps.Min(),
                AvgCellTemp = cellTemps.Average(),
                MaxSOC = cellSOCs.Max(),
                MinSOC = cellSOCs.Min(),
                AvgSOC = cellSOCs.Average(),
                StateOfHealth = cellStates.Average(c => 1 - c.Age),
                RemainingCapacity = remainingCapacity,
                TotalCapacity = totalCapacity,
                Timestamp = timeStamp,
                CellStates = cellStates
            };
        }

        // 获取模组SOC (基于最小单体SOC)
        public double GetPackSOC()
        {
            return _currentState.MinSOC; // 保守估计，使用最低单体SOC
        }

        // 获取模组SOH (平均健康度)
        public double GetPackSOH()
        {
            return _currentState.StateOfHealth;
        }

        // 平衡控制模拟
        // public void ApplyBalancing(int seriesIndex, double balancingCurrent, TimeSpan duration)
        // {
        //     if (seriesIndex < 0 || seriesIndex >= _config.SeriesCount)
        //         throw new ArgumentOutOfRangeException(nameof(seriesIndex));

        //     double deltaSOC = balancingCurrent * duration.TotalHours / _config.NominalCapacity;

        //     // 对指定串联组的所有并联电芯进行放电平衡
        //     foreach (var cell in _cells[seriesIndex])
        //     {
        //         var state = cell.GetCurrentState();
        //         cell.Update(-balancingCurrent, state.Temperature, duration);
        //     }

        //     UpdatePackState(_currentState.TotalCurrent, _currentState.AvgCellTemp, duration);
        // }

        // 示例使用
        //public static void Main(string[] args)
        //{
        //    // 创建模组配置 (例如: 12串4并的模组)
        //    var packConfig = new PackConfiguration
        //    {
        //        SeriesCount = 12,
        //        ParallelCount = 4,
        //        NominalVoltage = 3.2,
        //        NominalCapacity = 3.2, // 3.2Ah
        //        PackInternalResistance = 0.05 // 50mOhm
        //    };

        //    // 创建电池模组
        //    var pack = new BatteryPackSimulator(packConfig);

        //    // 模拟充放电循环
        //    Console.WriteLine("时间\t总电压\t电流\tMinSOC\tMaxSOC\tMinV\tMaxV\tMinT\tMaxT\tSOH");

        //    // 1C恒流充电1小时
        //    double chargeCurrent = packConfig.NominalCapacity * packConfig.ParallelCount; // 1C充电电流
        //    for (int i = 0; i < 60; i++)
        //    {
        //        pack.Update(chargeCurrent, 25.0, TimeSpan.FromMinutes(1));
        //        PrintPackState(pack, i + 1);
        //    }

        //    // 静置30分钟
        //    for (int i = 0; i < 30; i++)
        //    {
        //        pack.Update(0.0, 25.0, TimeSpan.FromMinutes(1));
        //        PrintPackState(pack, 60 + i + 1);
        //    }

        //    // 1C恒流放电1小时
        //    double dischargeCurrent = -packConfig.NominalCapacity * packConfig.ParallelCount; // 1C放电电流
        //    for (int i = 0; i < 60; i++)
        //    {
        //        pack.Update(dischargeCurrent, 25.0, TimeSpan.FromMinutes(1));
        //        PrintPackState(pack, 90 + i + 1);
        //    }

        //    // 模拟平衡操作 (对电压最高的串联组进行平衡)
        //    var state = pack.GetPackState();
        //    int maxVoltageIndex = Array.IndexOf(state.CellStates.Select(c => c.Voltage).ToArray(), state.MaxCellVoltage);
        //    Console.WriteLine($"\n执行平衡操作: 对串联组{maxVoltageIndex}进行放电平衡");
        //    pack.ApplyBalancing(maxVoltageIndex, 0.5, TimeSpan.FromMinutes(10)); // 0.5A平衡电流，持续10分钟
        //    PrintPackState(pack, 151);
        //}

        private void PrintPackState(BatteryPackSimulator pack, int minute)
        {
            var state = pack.GetPackState();
            Console.WriteLine($"{minute}min\t{state.TotalVoltage:F2}V\t{state.TotalCurrent:F1}A\t" +
                              $"{state.MinSOC * 100:F1}%\t{state.MaxSOC * 100:F1}%\t" +
                              $"{state.MinCellVoltage:F2}V\t{state.MaxCellVoltage:F2}V\t" +
                              $"{state.MinCellTemp:F1}°C\t{state.MaxCellTemp:F1}°C\t" +
                              $"{state.StateOfHealth * 100:F1}%");
        }
    }
}
