using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EssSimulator.EssDeviceSimModel
{
    // 电池簇配置
    public class ClusterConfiguration
    {
        public int PackCount { get; set; }                  // 串联模组数量
        public PackConfiguration PackConfig { get; set; } = new(); // 模组配置模板
        public double ClusterInternalResistance { get; set; } // 簇总内阻(Ohm)
        public double MaxVoltageImbalance { get; set; } = 5.0; // 允许的最大模组电压不平衡(V)
        public double MaxTempDifference { get; set; } = 15.0; // 允许的最大模组温差(°C)
    }

    // 电池簇状态
    public class ClusterState
    {
        public double TotalVoltage { get; set; }             // 簇总电压(V)
        public double TotalCurrent { get; set; }             // 簇总电流(A)
        public double MaxPackVoltage { get; set; }           // 最大模组电压(V)
        public double MinPackVoltage { get; set; }           // 最小模组电压(V)
        public double AvgPackVoltage { get; set; }           // 平均模组电压(V)
        public double VoltageImbalance { get; set; }         // 电压不平衡度(V)
        public double MaxPackTemp { get; set; }             // 最高模组温度(°C)
        public double MinPackTemp { get; set; }             // 最低模组温度(°C)
        public double AvgPackTemp { get; set; }             // 平均模组温度(°C)
        public double TempDifference { get; set; }           // 最大温差(°C)
        /// <summary>簇等效运算：未逐芯更新，单体电压/温度取同一等效值。</summary>
        public bool EquivalentCells { get; set; }
        /// <summary>等效单体电压 (V)。仅 <see cref="EquivalentCells"/> 时有效。</summary>
        public double EquivalentCellVoltage { get; set; }
        /// <summary>等效单体温度 (°C)。仅 <see cref="EquivalentCells"/> 时有效。</summary>
        public double EquivalentCellTemp { get; set; }
        public double MaxPackSOC { get; set; }              // 最大模组SOC
        public double MinPackSOC { get; set; }              // 最小模组SOC
        public double AvgPackSOC { get; set; }              // 平均模组SOC
        public double StateOfHealth { get; set; }           // 簇健康度(0-1)
        public double RemainingCapacity { get; set; }        // 剩余容量(kWh)
        public double TotalCapacity { get; set; }           // 总容量(kWh)
        public DateTime Timestamp { get; set; }             // 状态时间
        public List<PackState> PackStates { get; set; } = new(); // 所有模组状态
    }

    public class BatteryClusterSimulator
    {
        public readonly ClusterConfiguration _config;
        public readonly List<BatteryPackSimulator> _packs; // 串联的电池模组
        private readonly Random _random = new Random();
        public ClusterState _currentState = new();
        private bool _equivalentLatched;

        // 构造函数
        public BatteryClusterSimulator(ClusterConfiguration config)
        {
            _config = config;
            _packs = new List<BatteryPackSimulator>();

            // 初始化所有模组 (添加随机差异模拟不一致性)
            for (int i = 0; i < config.PackCount; i++)
            {
                // 复制模组配置并添加差异
                var packConfig = new PackConfiguration
                {
                    SeriesCount = config.PackConfig.SeriesCount,
                    ParallelCount = config.PackConfig.ParallelCount,
                    NominalVoltage = config.PackConfig.NominalVoltage,
                    NominalCapacity = config.PackConfig.NominalCapacity * (1 + (_random.NextDouble() - 0.5) * 0.01), // ±1%容量差异
                    InitialSoc = config.PackConfig.InitialSoc,
                    InitialSocRandomRange = config.PackConfig.InitialSocRandomRange,
                    PackInternalResistance = config.PackConfig.PackInternalResistance * (1 + (_random.NextDouble() - 0.5) * 0.05), // ±5%内阻差异
                    CoolingEfficiency = config.PackConfig.CoolingEfficiency * (1 + (_random.NextDouble() - 0.5) * 0.1) // ±10%冷却效率差异
                };

                _packs.Add(new BatteryPackSimulator(packConfig));
            }

            UpdateClusterState(0, 25.0, DateTime.Now);
        }

        // 获取当前簇状态
        public ClusterState GetClusterState() => _currentState;
        public ClusterConfiguration GetConfiguration() => _config;

        /// <summary>热设簇内全部 Pack SOC，并刷新簇汇总。</summary>
        public void SetSoc(double soc)
        {
            foreach (var pack in _packs)
                pack.SetSoc(soc);

            UpdateClusterState(_currentState.TotalCurrent, _currentState.AvgPackTemp, DateTime.UtcNow);
        }

        // 更新簇状态
        /// <param name="nodeTempC">电池节点温度（°C），作为电芯热环境。</param>
        /// <param name="cellPhysicsEnabled">false 时跳过逐芯更新，按单芯等效模型汇总整簇。null 时跟随系统开关。</param>
        public void Update(double clusterCurrent, double nodeTempC, DateTime timeStamp, TimeSpan timeStep, bool? cellPhysicsEnabled = null)
        {
            bool useCells = cellPhysicsEnabled ?? BatteryCalcMode.CellPhysicsEnabled;
            if (!useCells)
            {
                UpdateEquivalent(clusterCurrent, nodeTempC, timeStamp, timeStep);
                return;
            }

            if (_equivalentLatched)
            {
                var rep = _packs[0].GetCellState(0, 0);
                double soc = rep.SOC;
                double temp = rep.Temperature;
                foreach (var pack in _packs)
                    pack.AlignAllCells(soc, temp);
                _equivalentLatched = false;
            }

            // 更新所有模组 (串联电流相同)
            foreach (var pack in _packs)
            {
                // 模拟模组温度环境差异 (如簇中不同位置)
                double packNodeTemp = nodeTempC + (_random.NextDouble() - 0.5) * 5; // ±2.5°C差异

                pack.Update(clusterCurrent, packNodeTemp, timeStamp, timeStep);
            }

            // 更新簇状态
            UpdateClusterState(clusterCurrent, nodeTempC, timeStamp);
        }

        /// <summary>
        /// 簇等效：只积分首节电芯，电压按串联节数 × 包数放大，温度/SOC 写入全部包汇总。
        /// </summary>
        private void UpdateEquivalent(double clusterCurrent, double nodeTempC, DateTime timeStamp, TimeSpan timeStep)
        {
            var lead = _packs[0];
            if (!_equivalentLatched)
            {
                double soc = _currentState.AvgPackSOC;
                double temp = _currentState.AvgPackTemp;
                lead.SetRepresentative(soc, temp);
                _equivalentLatched = true;
            }

            var cfg = lead.GetPackConfiguration();
            double parallel = Math.Max(1, cfg.ParallelCount);
            double cellCurrent = clusterCurrent / parallel;
            var cell = lead.StepRepresentativeCell(cellCurrent, nodeTempC, timeStamp, timeStep);
            double soh = Math.Clamp(1.0 - cell.Age, 0.0, 1.0);

            foreach (var pack in _packs)
            {
                pack.ApplyEquivalentState(
                    cell.Voltage,
                    cell.SOC,
                    cell.Temperature,
                    soh,
                    cell.RemainingCapacity,
                    clusterCurrent,
                    timeStamp);
            }

            UpdateClusterState(clusterCurrent, nodeTempC, timeStamp);
            _currentState.EquivalentCells = true;
            _currentState.EquivalentCellVoltage = cell.Voltage;
            _currentState.EquivalentCellTemp = cell.Temperature;
        }

        // 计算并更新簇状态
        private void UpdateClusterState(double clusterCurrent, double ambientTemp, DateTime timeStamp)
        {
            var packStates = _packs.Select(p => p.GetPackState()).ToList();

            // 计算电压相关参数
            double totalVoltage = packStates.Sum(p => p.TotalVoltage);
            var packVoltages = packStates.Select(p => p.TotalVoltage).ToList();
            double voltageImbalance = packVoltages.Max() - packVoltages.Min();

            // 计算温度相关参数
            var packTemps = packStates.Select(p => p.AvgCellTemp).ToList();
            double tempDifference = packTemps.Max() - packTemps.Min();

            // 计算SOC相关参数
            var packSOCs = packStates.Select(p => p.MinSOC).ToList(); // 使用模组最小SOC

            // 计算容量相关参数
            // 將屶5的 RemainingCapacity 单位统一为 Ah：串联各 Pack 容量相同，簇总容量 = 任意一个 Pack 的容量（短板效应）
            double remainingCapacity = packStates.Min(p => p.RemainingCapacity); // Ah
            double totalCapacity = _config.PackConfig.NominalCapacity * _config.PackConfig.ParallelCount *
                                _config.PackConfig.SeriesCount * _config.PackConfig.NominalVoltage *
                                packStates.Average(p => p.StateOfHealth) / 1000.0; // kWh

            // 更新簇状态
            _currentState = new ClusterState
            {
                TotalVoltage = totalVoltage,
                TotalCurrent = clusterCurrent,
                MaxPackVoltage = packVoltages.Max(),
                MinPackVoltage = packVoltages.Min(),
                AvgPackVoltage = packVoltages.Average(),
                VoltageImbalance = voltageImbalance,
                MaxPackTemp = packTemps.Max(),
                MinPackTemp = packTemps.Min(),
                AvgPackTemp = packTemps.Average(),
                TempDifference = tempDifference,
                MaxPackSOC = packSOCs.Max(),
                MinPackSOC = packSOCs.Min(),
                AvgPackSOC = packSOCs.Average(),
                StateOfHealth = packStates.Average(p => p.StateOfHealth),
                RemainingCapacity = remainingCapacity,
                TotalCapacity = totalCapacity,
                Timestamp = timeStamp,
                PackStates = packStates
            };
        }

        // 簇级平衡控制
        // public void ApplyClusterBalancing(double balancingCurrent, TimeSpan duration)
        // {
        //     // 找出SOC最高的模组
        //     int maxSOCIndex = 0;
        //     double maxSOC = 0;
        //     for (int i = 0; i < _packs.Count; i++)
        //     {
        //         var soc = _packs[i].GetPackSOC();
        //         if (soc > maxSOC)
        //         {
        //             maxSOC = soc;
        //             maxSOCIndex = i;
        //         }
        //     }

        //     // 对SOC最高的模组进行放电平衡
        //     Console.WriteLine($"执行簇级平衡: 对模组{maxSOCIndex}(SOC={maxSOC * 100:F1}%)进行放电");
        //     _packs[maxSOCIndex].Update(-balancingCurrent, _currentState.AvgPackTemp, duration);

        //     UpdateClusterState(_currentState.TotalCurrent, _currentState.AvgPackTemp, duration);
        // }

        // 获取簇SOC (基于最小模组SOC)
        public double GetClusterSOC()
        {
            return _currentState.MinPackSOC; // 保守估计
        }

        // 获取簇SOH (平均健康度)
        public double GetClusterSOH()
        {
            return _currentState.StateOfHealth;
        }

        // 示例使用
        //public static void Main(string[] args)
        //{
        //    // 创建模组配置模板 (例如: 12串4并的模组)
        //    var packConfig = new BatteryPackSimulator.PackConfiguration
        //    {
        //        SeriesCount = 12,
        //        ParallelCount = 4,
        //        NominalVoltage = 3.2,
        //        NominalCapacity = 3.2, // 3.2Ah
        //        PackInternalResistance = 0.05 // 50mOhm
        //    };

        //    // 创建簇配置 (16个模组串联)
        //    var clusterConfig = new ClusterConfiguration
        //    {
        //        PackCount = 16,
        //        PackConfig = packConfig,
        //        ClusterInternalResistance = 0.1, // 100mOhm
        //        MaxVoltageImbalance = 5.0,
        //        MaxTempDifference = 15.0
        //    };

        //    // 创建电池簇
        //    var cluster = new BatteryClusterSimulator(clusterConfig);

        //    // 模拟充放电循环
        //    Console.WriteLine("时间\t总电压\t电流\tMinSOC\tMaxSOC\tMinV\tMaxV\tImb\tMinT\tMaxT\tDiff\tSOH\t报警");

        //    // 0.5C恒流充电2小时
        //    double chargeCurrent = packConfig.NominalCapacity * packConfig.ParallelCount * 0.5; // 0.5C充电电流
        //    for (int i = 0; i < 120; i++)
        //    {
        //        cluster.Update(chargeCurrent, 25.0, TimeSpan.FromMinutes(1));
        //        PrintClusterState(cluster, i + 1);
        //    }

        //    // 静置30分钟
        //    for (int i = 0; i < 30; i++)
        //    {
        //        cluster.Update(0.0, 25.0, TimeSpan.FromMinutes(1));
        //        PrintClusterState(cluster, 120 + i + 1);
        //    }

        //    // 0.5C恒流放电2小时
        //    double dischargeCurrent = -packConfig.NominalCapacity * packConfig.ParallelCount * 0.5; // 0.5C放电电流
        //    for (int i = 0; i < 120; i++)
        //    {
        //        cluster.Update(dischargeCurrent, 25.0, TimeSpan.FromMinutes(1));
        //        PrintClusterState(cluster, 150 + i + 1);
        //    }

        //    // 模拟簇级平衡操作
        //    var state = cluster.GetClusterState();
        //    if (state.VoltageImbalance > clusterConfig.MaxVoltageImbalance)
        //    {
        //        Console.WriteLine("\n检测到电压不平衡，执行簇级平衡...");
        //        cluster.ApplyClusterBalancing(2.0, TimeSpan.FromMinutes(30)); // 2A平衡电流，持续30分钟
        //        PrintClusterState(cluster, 271);
        //    }
        //}

        // private static void PrintClusterState(BatteryClusterSimulator cluster, int minute)
        // {
        //     var state = cluster.GetClusterState();
        //     Console.WriteLine($"{minute}min\t{state.TotalVoltage:F1}V\t{state.TotalCurrent:F1}A\t" +
        //                       $"{state.MinPackSOC * 100:F1}%\t{state.MaxPackSOC * 100:F1}%\t" +
        //                       $"{state.MinPackVoltage:F1}V\t{state.MaxPackVoltage:F1}V\t" +
        //                       $"{state.VoltageImbalance:F1}V\t" +
        //                       $"{state.MinPackTemp:F1}°C\t{state.MaxPackTemp:F1}°C\t" +
        //                       $"{state.TempDifference:F1}°C\t" +
        //                       $"{state.StateOfHealth * 100:F1}%\t" +
        //                       $"{(state.IsAlarm ? "异常" : "正常")}");
        // }
    }
}
