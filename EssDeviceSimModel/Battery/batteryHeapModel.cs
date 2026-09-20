using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EssSimulator.EssDeviceSimModel
{
    // 电池堆状态
    public class RackState
    {
        public double TotalVoltage { get; set; }                  // 堆总电压(V)
        public double TotalCurrent { get; set; }                  // 堆总电流(A)
        public double MaxClusterCurrent { get; set; }             // 最大簇电流(A)
        public double MinClusterCurrent { get; set; }             // 最小簇电流(A)
        public double CurrentImbalanceRatio { get; set; }         // 电流不平衡比例(0-1)
        public double MaxClusterVoltage { get; set; }             // 最大簇电压(V)
        public double MinClusterVoltage { get; set; }             // 最小簇电压(V)
        public double VoltageDifference { get; set; }             // 电压差异(V)
        public double MaxClusterSOC { get; set; }                // 最大簇SOC
        public double MinClusterSOC { get; set; }                // 最小簇SOC
        public double SOCDifference { get; set; }                // SOC差异
        public double MaxClusterTemp { get; set; }               // 最高簇温度(°C)
        public double MinClusterTemp { get; set; }               // 最低簇温度(°C)

        public double AvgClusterTemp { get; set; }               //堆平均温度
        public double StateOfHealth { get; set; }                // 堆健康度(0-1)
        public double RemainingEnergy { get; set; }              // 剩余能量(kWh)
        public double TotalEnergy { get; set; }                  // 总能量(kWh)
        public double TotalChargeEnergy { get; set; }        // 累计充电能量(kWh)
        public double TotalDischargeEnergy { get; set; }     // 累计放电能量(kWh)

        /// <summary>堆内被动均衡是否正在动作。</summary>
        public bool IsPassiveBalancing { get; set; }
        /// <summary>本步被动均衡泄放电流合计 (A，放电为正)。</summary>
        public double PassiveBalanceDischargeCurrentA { get; set; }

        public bool IsAlarm { get; set; }                        // 是否报警
        public ushort IsFault { get; set; }                      // 是否故障 0-无故障 1-充电故障 2-放电故障 3-其他故障
        public bool IsProtection { get; set; }                    // 是否保护动作
        /// <summary>BMS 与 PCS 直流侧已关联（并网）；false 时 PCS 直流失电。</summary>
        public bool IsPcsLinked { get; set; }
        public DateTime Timestamp { get; set; }                  // 状态时间
        public List<ClusterState>? ClusterStates { get; set; } //所有簇状态
    }

    // 电池堆配置
    public class RackConfiguration
    {
        public int ClusterCount { get; set; }                      // 并联簇数量
        public ClusterConfiguration? ClusterConfig { get; set; } // 簇配置模板
        public double RackInternalResistance { get; set; }        // 堆总内阻(Ohm)
        public double MaxCurrentImbalance { get; set; } = 0.1;    // 允许的最大电流不平衡比例(0-1)
        public double MaxSOCDifference { get; set; } = 0.2;       // 允许的最大SOC差异(0-1)

        /// <summary>堆内簇间被动均衡（高 SOC 簇泄放）。</summary>
        public RackPassiveBalanceConfig PassiveBalance { get; set; } = new();
    }

    /// <summary>
    /// 堆内簇间被动均衡参数（电阻泄放型：只对高 SOC 簇施加小放电电流，能量以热耗散）。
    /// 建议默认面向大储 LFP 联调，可按项目调整。
    /// </summary>
    public class RackPassiveBalanceConfig
    {
        public bool Enabled { get; set; } = true;

        /// <summary>簇间 SOC 差 ≥ 此值时启动均衡（建议 2%）。</summary>
        public double StartSocDelta { get; set; } = 0.02;

        /// <summary>簇间 SOC 差 ≤ 此值时停止均衡（建议 1%，回滞）。</summary>
        public double StopSocDelta { get; set; } = 0.01;

        /// <summary>被动均衡放电倍率（建议 0.02C，约 314Ah×0.02≈6.3A）。</summary>
        public double BalanceCRate { get; set; } = 0.02;

        /// <summary>仅在堆电流绝对值低于此阈值时均衡（建议 10A，近似静置/小电流）。</summary>
        public bool IdleOnly { get; set; } = true;

        /// <summary>IdleOnly 时的堆电流门槛 (A)。</summary>
        public double IdleCurrentThresholdA { get; set; } = 10.0;

        /// <summary>
        /// 仅当簇 SOC 高于「最低簇 SOC + 此裕度」才泄放（建议等于 StopSocDelta）。
        /// </summary>
        public double BleedAboveMinMargin { get; set; } = 0.01;
    }

    public class BatteryRackSimulator
    {
        public RackConfiguration _config { get; }
        public List<BatteryClusterSimulator> _clusters { get; set; } //并联的电池簇
        private readonly Random _random = new Random(0);
        public RackState _currentState { get; set; }
        private double _totalChargeEnergy;        // 累计充电能量(kWh)
        private double _totalDischargeEnergy;     // 累计放电能量(kWh)
        private bool _passiveBalanceActive;

        // 构造函数
        public BatteryRackSimulator(RackConfiguration config)
        {
            _currentState = new RackState();
            _config = config;
            _clusters = new List<BatteryClusterSimulator>();

            // 初始化所有电池簇 (添加随机差异模拟不一致性)
            for (int i = 0; i < config.ClusterCount; i++)
            {
                // 复制簇配置并添加差异
                var clusterConfig = new ClusterConfiguration
                {
                    PackCount = config.ClusterConfig!.PackCount,
                    PackConfig = config.ClusterConfig.PackConfig,
                    ClusterInternalResistance = config.ClusterConfig.ClusterInternalResistance *
                                              (1 + (_random.NextDouble() - 0.5) * 0.03), // ±3%内阻差异
                    MaxVoltageImbalance = config.ClusterConfig.MaxVoltageImbalance,
                    MaxTempDifference = config.ClusterConfig.MaxTempDifference
                };

                _clusters.Add(new BatteryClusterSimulator(clusterConfig));
            }

            UpdateRackState(0, 25.0, DateTime.Now, TimeSpan.Zero);
        }

        internal (double Soc, double TotalCurrent, double ChargeEnergy, double DischargeEnergy) CaptureStepMemento()
        {
            return (
                _currentState.MinClusterSOC,
                _currentState.TotalCurrent,
                _totalChargeEnergy,
                _totalDischargeEnergy);
        }

        internal void RestoreStepMemento(double soc, double totalCurrent, double chargeEnergy, double dischargeEnergy)
        {
            foreach (var cluster in _clusters)
                cluster.SetSoc(soc);

            _totalChargeEnergy = chargeEnergy;
            _totalDischargeEnergy = dischargeEnergy;
            UpdateRackState(
                totalCurrent,
                _currentState.AvgClusterTemp != 0 ? _currentState.AvgClusterTemp : 25.0,
                _currentState.Timestamp == default ? DateTime.UtcNow : _currentState.Timestamp,
                TimeSpan.Zero);
        }

        // 获取当前堆状态
        public RackState GetRackState() => _currentState;
        public RackConfiguration GetRackConfig() => _config;

        /// <summary>热设整堆 SOC（0~1）：写透全部电芯并刷新堆/簇汇总，不累计充放电电能。</summary>
        public bool TrySetSoc(double soc, out string message)
        {
            if (double.IsNaN(soc) || double.IsInfinity(soc) || soc < 0.0 || soc > 1.0)
            {
                message = "SOC 须在 0~1（或界面按 % 换算后传入）";
                return false;
            }

            foreach (var cluster in _clusters)
                cluster.SetSoc(soc);

            UpdateRackState(
                _currentState.TotalCurrent,
                _currentState.AvgClusterTemp != 0 ? _currentState.AvgClusterTemp : 25.0,
                DateTime.UtcNow,
                TimeSpan.Zero);

            message = $"堆 SOC 已设为 {soc:F4}（MinClusterSOC={_currentState.MinClusterSOC:F4}）";
            return true;
        }

        // 更新堆状态
        /// <param name="nodeTempC">电池节点温度（°C），作为电芯热环境。</param>
        public void Update(double rackCurrent, double nodeTempC, DateTime timeStamp, TimeSpan timeStep)
        {
            // 计算各簇电流分配 (考虑内阻差异)
            var clusterCurrents = CalculateCurrentDistribution(rackCurrent);

            // 堆内被动均衡：对高 SOC 簇叠加小放电电流（能量耗散，不转移给低 SOC 簇）
            double balanceDischargeSum = ApplyPassiveBalanceCurrents(clusterCurrents, rackCurrent);

            // 更新所有簇 (并联电压相同，电流不同)
            for (int i = 0; i < _clusters.Count; i++)
            {
                // 模拟簇间环境温度差异
                double clusterNodeTemp = nodeTempC + (_random.NextDouble() - 0.5) * 3; // ±1.5°C差异

                _clusters[i].Update(clusterCurrents[i], clusterNodeTemp, timeStamp, timeStep);
            }

            // 更新堆状态
            UpdateRackState(rackCurrent, nodeTempC, timeStamp, timeStep);
            _currentState.IsPassiveBalancing = _passiveBalanceActive && balanceDischargeSum > 0;
            _currentState.PassiveBalanceDischargeCurrentA = balanceDischargeSum;
        }

        /// <summary>
        /// 按簇 SOC 差启动/停止被动均衡，并对高 SOC 簇叠加放电电流（约定：电流&lt;0 为放电）。
        /// </summary>
        /// <returns>本步泄放电流合计（放电为正，A）。</returns>
        internal double ApplyPassiveBalanceCurrents(double[] clusterCurrents, double rackCurrent)
        {
            var cfg = _config.PassiveBalance ?? new RackPassiveBalanceConfig();
            if (!cfg.Enabled || _clusters.Count < 2 || clusterCurrents.Length != _clusters.Count)
            {
                _passiveBalanceActive = false;
                return 0;
            }

            if (cfg.IdleOnly && Math.Abs(rackCurrent) > Math.Max(0, cfg.IdleCurrentThresholdA))
            {
                _passiveBalanceActive = false;
                return 0;
            }

            var socs = _clusters.Select(c => c.GetClusterSOC()).ToArray();
            double maxSoc = socs.Max();
            double minSoc = socs.Min();
            double delta = maxSoc - minSoc;

            double start = Math.Max(0, cfg.StartSocDelta);
            double stop = Math.Clamp(cfg.StopSocDelta, 0, start);
            if (!_passiveBalanceActive && delta >= start)
                _passiveBalanceActive = true;
            else if (_passiveBalanceActive && delta <= stop)
                _passiveBalanceActive = false;

            if (!_passiveBalanceActive)
                return 0;

            double packAh = Math.Max(1e-6,
                _config.ClusterConfig!.PackConfig.NominalCapacity * _config.ClusterConfig.PackConfig.ParallelCount);
            double balanceDischargeA = Math.Max(0, cfg.BalanceCRate) * packAh; // 放电幅度（正值）
            if (balanceDischargeA <= 0)
                return 0;

            double bleedFloor = minSoc + Math.Max(0, cfg.BleedAboveMinMargin);
            double sum = 0;
            for (int i = 0; i < _clusters.Count; i++)
            {
                if (socs[i] <= bleedFloor)
                    continue;
                // 物理约定：电流 &lt; 0 为放电
                clusterCurrents[i] -= balanceDischargeA;
                sum += balanceDischargeA;
            }

            return sum;
        }

        // 计算电流分配 (考虑并联簇的内阻差异)
        private double[] CalculateCurrentDistribution(double totalCurrent)
        {
            // 并联簇按电导比分配电流：I_i = I_total × G_i / ΣG_j。电压差异已内包含在内阻中，不需额外修正。
            double totalConductance = _clusters.Sum(c => 1.0 / c._config.ClusterInternalResistance);

            double[] currents = new double[_clusters.Count];
            if (totalConductance <= 0) return currents; // 防御性保护
            for (int i = 0; i < _clusters.Count; i++)
            {
                double g = 1.0 / _clusters[i]._config.ClusterInternalResistance;
                currents[i] = totalCurrent * g / totalConductance;
            }
            return currents;
        }

        // 计算并更新堆状态
        private void UpdateRackState(double rackCurrent, double ambientTemp, DateTime timeStamp, TimeSpan timeStep)
        {
            var clusterStates = _clusters.Select(c => c.GetClusterState()).ToList();

            // 计算电压相关参数 (并联系统电压应相同，差异反映测量误差)
            var clusterVoltages = clusterStates.Select(c => c.TotalVoltage).ToList();
            double avgVoltage = clusterVoltages.Average();

            // 计算电流相关参数
            var clusterCurrents = clusterStates.Select(c => c.TotalCurrent).ToList();
            double currentImbalanceRatio = Math.Abs(rackCurrent) > 0
                ? (clusterCurrents.Max() - clusterCurrents.Min()) / Math.Abs(rackCurrent)
                : 0.0;

            // 计算SOC相关参数
            var clusterSOCs = clusterStates.Select(c => c.MinPackSOC).ToList();

            // 计算温度相关参数
            var clusterTemps = clusterStates.Select(c => c.AvgPackTemp).ToList();

            // 计算能量相关参数 (kWh)
            // remainingEnergy = 各簇（MinSOC × 簇电压 × 簇配置容量）之和
            double nominalClusterCapacityAh = _config.ClusterConfig!.PackConfig.NominalCapacity
                                            * _config.ClusterConfig.PackConfig.ParallelCount; // Ah
            double remainingEnergy = clusterStates.Sum(c =>
                c.MinPackSOC * c.TotalVoltage * nominalClusterCapacityAh / 1000.0); // kWh
            double totalEnergy = clusterStates.Sum(c =>
                c.TotalVoltage * nominalClusterCapacityAh * c.StateOfHealth / 1000.0); // kWh

            // 使用积分和时间计算累计充放电能量
            if (rackCurrent > 0)
            {
                _totalChargeEnergy += (rackCurrent * avgVoltage) * (timeStep.TotalHours) / 1000.0; // kWh 
            }
            else if (rackCurrent < 0)
            {
                _totalDischargeEnergy += (-rackCurrent * avgVoltage) * (timeStep.TotalHours) / 1000.0; // kWh
            }

            // 更新堆状态
            _currentState.TotalVoltage = avgVoltage;
            _currentState.TotalCurrent = rackCurrent;
            _currentState.MaxClusterCurrent = clusterCurrents.Max();
            _currentState.MinClusterCurrent = clusterCurrents.Min();
            _currentState.CurrentImbalanceRatio = currentImbalanceRatio;
            _currentState.MaxClusterVoltage = clusterVoltages.Max();
            _currentState.MinClusterVoltage = clusterVoltages.Min();
            _currentState.VoltageDifference = clusterVoltages.Max() - clusterVoltages.Min();
            _currentState.MaxClusterSOC = clusterSOCs.Max();
            _currentState.MinClusterSOC = clusterSOCs.Min();
            _currentState.SOCDifference = clusterSOCs.Max() - clusterSOCs.Min();
            _currentState.MaxClusterTemp = clusterTemps.Max();
            _currentState.MinClusterTemp = clusterTemps.Min();
            _currentState.AvgClusterTemp = clusterTemps.Average();
            _currentState.StateOfHealth = clusterStates.Average(c => c.StateOfHealth);
            _currentState.RemainingEnergy = remainingEnergy;
            _currentState.TotalEnergy = totalEnergy;
            _currentState.TotalChargeEnergy = _totalChargeEnergy;
            _currentState.TotalDischargeEnergy = _totalDischargeEnergy;
            _currentState.Timestamp = timeStamp;
            _currentState.ClusterStates = clusterStates;
        }

        // 堆级被动均衡已在 Update() 中按 RackPassiveBalanceConfig 自动执行。

        // 获取堆SOC (基于最小簇SOC)
        public double GetRackSOC()
        {
            return _currentState!.MinClusterSOC; // 保守估计
        }

        // 获取堆SOH (平均健康度)
        public double GetRackSOH()
        {
            return _currentState!.StateOfHealth;
        }

        // 示例使用
        //public static void Main(string[] args)
        //{
        //    // 创建簇配置模板 (例如: 16个12串4并模组组成的簇)
        //    var clusterConfig = new BatteryClusterSimulator.ClusterConfiguration
        //    {
        //        PackCount = 16,
        //        PackConfig = new BatteryPackSimulator.PackConfiguration
        //        {
        //            SeriesCount = 12,
        //            ParallelCount = 4,
        //            NominalVoltage = 3.2,
        //            NominalCapacity = 3.2, // 3.2Ah
        //            PackInternalResistance = 0.05 // 50mOhm
        //        },
        //        ClusterInternalResistance = 0.1, // 100mOhm
        //        MaxVoltageImbalance = 5.0,
        //        MaxTempDifference = 15.0
        //    };

        //    // 创建电池堆配置 (5个簇并联)
        //    var rackConfig = new RackConfiguration
        //    {
        //        ClusterCount = 5,
        //        ClusterConfig = clusterConfig,
        //        RackInternalResistance = 0.02, // 20mOhm
        //        MaxCurrentImbalance = 0.1,
        //        MaxSOCDifference = 0.2
        //    };

        //    // 创建电池堆
        //    var rack = new BatteryRackSimulator(rackConfig);

        //    // 模拟充放电循环
        //    Console.WriteLine("时间\t总电压\t总电流\tMinSOC\tMaxSOC\tSOC差\t最大簇流\t最小簇流\t不平衡\t报警");

        //    // 0.2C恒流充电5小时
        //    double chargeCurrent = clusterConfig.PackConfig.NominalCapacity *
        //                         clusterConfig.PackConfig.ParallelCount *
        //                         clusterConfig.PackCount * 0.2; // 0.2C充电电流

        //    for (int i = 0; i < 300; i++)
        //    {
        //        rack.Update(chargeCurrent, 25.0, TimeSpan.FromMinutes(1));
        //        PrintRackState(rack, i + 1);
        //    }

        //    // 静置1小时
        //    for (int i = 0; i < 60; i++)
        //    {
        //        rack.Update(0.0, 25.0, TimeSpan.FromMinutes(1));
        //        PrintRackState(rack, 300 + i + 1);
        //    }

        //    // 0.2C恒流放电5小时
        //    double dischargeCurrent = -clusterConfig.PackConfig.NominalCapacity *
        //                            clusterConfig.PackConfig.ParallelCount *
        //                            clusterConfig.PackCount * 0.2; // 0.2C放电电流

        //    for (int i = 0; i < 300; i++)
        //    {
        //        rack.Update(dischargeCurrent, 25.0, TimeSpan.FromMinutes(1));
        //        PrintRackState(rack, 360 + i + 1);
        //    }

        //    // 模拟堆级均衡操作
        //    var state = rack.GetRackState();
        //    if (state.SOCDifference > rackConfig.MaxSOCDifference)
        //    {
        //        Console.WriteLine("\n检测到SOC不平衡，执行堆级均衡...");
        //        rack.ApplyRackBalancing(TimeSpan.FromHours(1)); // 均衡1小时
        //        PrintRackState(rack, 661);
        //    }
        //}

        public void PrintRackState(BatteryRackSimulator rack)
        {
            var state = rack.GetRackState();
            if (state == null) return;
            Console.WriteLine($"{state.TotalVoltage:F1}V\t{state.TotalCurrent:F1}A\t" +
                              $"{state.MinClusterSOC * 100:F1}%\t{state.MaxClusterSOC * 100:F1}%\t" +
                              $"{state.SOCDifference * 100:F1}%\t" +
                              $"{state.MaxClusterCurrent:F1}A\t{state.MinClusterCurrent:F1}A\t" +
                              $"{state.CurrentImbalanceRatio * 100:F1}%\t" +
                              $"{(state.IsAlarm ? "告警" : "无告警")}" +
                              $"{(state.IsProtection ? "保护" : "无保护")}" +
                              $"{(state.IsFault != 0 ? "故障" : "无故障")}");
        }
    }
}
