using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EssSimulator.EssDeviceSimModel
{
    // 电芯基本特性参数
    public class CellSpecifications
    {
        public double NominalCapacity { get; set; }    // 额定容量 (Ah)
        public double NominalVoltage { get; set; }     // 额定电压 (V)
        public double MinVoltage { get; set; }         // 最低电压 (V)
        public double MaxVoltage { get; set; }         // 最高电压 (V)
        public double InitialSOC { get; set; }         // 初始SOC (0-1)
        public double InternalResistance { get; set; } // 内阻 (Ohm)
        public double Mass { get; set; }               // 质量 (kg)
        public double Volume { get; set; }             // 体积 (m³)
    }

    // 电芯动态状态
    public class CellState
    {
        public double SOC { get; set; }                // 当前荷电状态 (0-1)
        public double Voltage { get; set; }            // 端电压 (V)
        public double Current { get; set; }            // 电流 (A), 正为充电,负为放电
        public double Temperature { get; set; }        // 温度 (°C)
        public double RemainingCapacity { get; set; }   // 剩余容量 (Ah)
        public double CycleCount { get; set; }          // 循环次数
        public double Age { get; set; }                // 老化程度 (0-1)
        public DateTime Timestamp { get; set; }         // 状态时间戳
    }

    public class LiFePO4CellSimulator
    {
        //// OCV-SOC曲线数据
        //private static readonly Dictionary<double, double> OpenCircuitOcvCurve = new Dictionary<double, double>
        //{
        //    {0.00, 2.50}, {0.05, 2.90}, {0.10, 3.00}, {0.15, 3.05},
        //    {0.20, 3.10}, {0.25, 3.15}, {0.30, 3.20}, {0.35, 3.25},
        //    {0.40, 3.30}, {0.45, 3.33}, {0.50, 3.35}, {0.55, 3.37},
        //    {0.60, 3.39}, {0.65, 3.40}, {0.70, 3.42}, {0.75, 3.45},
        //    {0.80, 3.48}, {0.85, 3.50}, {0.90, 3.53}, {0.95, 3.58},
        //    {1.00, 3.65}
        //};

        private readonly CellSpecifications _specs;
        private CellState _currentState;
        private double _totalAhThroughput;  // 累计安时吞吐量
        private double _chargeAhAccum;      // 累计充电安时，用于循环计数
        private double _dischargeAhAccum;   // 累计放电安时，用于循环计数
        private double _calendarAgeAccum;   // 温度相关日历老化累积 (0-1)

        // 构造函数
        public LiFePO4CellSimulator(CellSpecifications specs)
        {
            _specs = specs;
            _currentState = new CellState
            {
                SOC = specs.InitialSOC,
                RemainingCapacity = specs.NominalCapacity * specs.InitialSOC,
                Voltage = LiFePO4BatteryOCVModel.GetVoltageFromSOC(specs.InitialSOC, 0, false),
                Temperature = 25.0, // 默认室温
                CycleCount = 0,
                Age = 0,
                Timestamp = DateTime.Now
            };
        }

        // 获取当前电芯状态
        public CellState GetCurrentState() => _currentState;
        public CellSpecifications GetSpecs() => _specs;

        public void SetCellTemperature(double temperature)
        {
            _currentState.Temperature = temperature;
        }

        /// <summary>热设电芯 SOC（0~1），同步剩余容量与 OCV 端电压。</summary>
        public void SetSoc(double soc)
        {
            soc = Math.Clamp(soc, 0.0, 1.0);
            _currentState.SOC = soc;
            _currentState.RemainingCapacity = _specs.NominalCapacity * soc;
            _currentState.Voltage = LiFePO4BatteryOCVModel.GetVoltageFromSOC(soc, 0, false);
            _currentState.Timestamp = DateTime.UtcNow;
        }

        // 应用充放电条件更新电芯状态
        /// <param name="nodeTempC">电池节点温度（°C）：作为电芯热环境；节点温度越高，电芯散热效率越低。</param>
        public void Update(double current, double nodeTempC, DateTime timeStamp, TimeSpan timeStep)
        {
            bool isCharging = false;

            if (current > 0)
            {
                isCharging = true;
            }
            // 计算时间差 (小时)
            double deltaTimeHours = timeStep.TotalHours;

            // 更新安时吞吐量 (用于老化计算)
            double deltaAhAbs = Math.Abs(current) * deltaTimeHours;
            _totalAhThroughput += deltaAhAbs;

            // 计算SOC变化
            double deltaSOC = current * deltaTimeHours / _specs.NominalCapacity;
            double newSOC = _currentState.SOC + deltaSOC;

            // SOC边界检查
            newSOC = Math.Max(0, Math.Min(1, newSOC));

            // 计算剩余容量
            double newRemainingCapacity = _specs.NominalCapacity * newSOC;

            // 计算C-rate
            double cRate = Math.Abs(current) / _specs.NominalCapacity;

            // 获取OCV
            double ocv = LiFePO4BatteryOCVModel.GetVoltageFromSOC(newSOC, cRate, isCharging);

            // 计算端电压 (考虑内阻)
            double voltage = ocv + current * _specs.InternalResistance;

            // 电压边界检查
            voltage = Math.Max(_specs.MinVoltage, Math.Min(_specs.MaxVoltage, voltage));


            // 温度模型（一阶滤波，与步长解耦）
            // 稳态温升 = P_loss / h_eff（线性冷却系数，W/°C）
            // 热时间常数 tau = mass * Cp / h_eff
            // 一阶滤波：T(t) = T_ss + (T(t-1) - T_ss) * exp(-dt/tau)
            //
            // 散热效率受「电池节点温度」影响：节点温度越高，散热越慢（h_eff 越小），
            // 电芯温度越容易被节点高温带高；电芯热环境即为电池节点温度。
            double powerLoss    = current * current * _specs.InternalResistance; // W
            const double hBase  = 0.5;   // 基准等效散热系数 W/°C
            const double Cp     = 900.0; // 比热容 J/(kg·°C)
            double coolingFactor = ComputeCoolingFactor(nodeTempC);              // 散热效率系数 0~1
            double hEff         = hBase * coolingFactor;                          // 有效散热系数 W/°C
            double tau      = _specs.Mass * Cp / hEff;                 // 热时间常数(s)
            double deltaTimeSec = deltaTimeHours * 3600.0;              // 步长(s)
            double T_ss     = nodeTempC + powerLoss / hEff;            // 稳态温度（环境=电池节点温度）
            double alpha    = Math.Exp(-deltaTimeSec / tau);            // 滤波系数
            double newTemp  = T_ss + (_currentState.Temperature - T_ss) * alpha;

            // 老化模型 (循环/吞吐 + 温度加速日历老化)
            double newAge = CalculateAging(_totalAhThroughput, _currentState.CycleCount, newTemp, deltaTimeHours);

            // 循环计数：充、放分开累计安时，满 1C 各计 0.5 个循环，再求和
            if (current > 0)
            {
                _chargeAhAccum += deltaAhAbs;
            }
            else if (current < 0)
            {
                _dischargeAhAccum += deltaAhAbs;
            }

            double chargeCycles = Math.Floor(_chargeAhAccum / _specs.NominalCapacity) * 0.5;
            double dischargeCycles = Math.Floor(_dischargeAhAccum / _specs.NominalCapacity) * 0.5;
            _currentState.CycleCount = chargeCycles + dischargeCycles;

            // 更新状态
            _currentState = new CellState
            {
                SOC = newSOC,
                Voltage = voltage,
                Current = current,
                Temperature = newTemp,
                RemainingCapacity = newRemainingCapacity,
                CycleCount = _currentState.CycleCount,
                Age = newAge,
                Timestamp = timeStamp
            };
        }

        /// <summary>
        /// 散热效率系数：电池节点温度越高，电芯散热越慢（1.0 → MinFraction 线性下降）。
        /// 25°C 及以下散热正常；55°C 及以上散热最差（MinFraction）。
        /// </summary>
        private static double ComputeCoolingFactor(double nodeTempC)
        {
            const double refC = 25.0;
            const double fullC = 55.0;
            const double minFraction = 0.3;
            if (nodeTempC <= refC)
                return 1.0;
            if (nodeTempC >= fullC)
                return minFraction;
            double t = (nodeTempC - refC) / (fullC - refC);
            return 1.0 - t * (1.0 - minFraction);
        }

        // 老化模型计算
        private double CalculateAging(double totalAhThroughput, double cycleCount, double temperatureCelsius, double deltaTimeHours)
        {
            // 简化老化模型: 基于循环次数和总吞吐量
            double cycleAging = cycleCount / 2000.0;  // 假设2000次循环后老化100%
            double throughputAging = totalAhThroughput / (2 * _specs.NominalCapacity * 2000);
            double baseAge = Math.Min(1.0, 0.7 * cycleAging + 0.3 * throughputAging);

            if (Thermal.ThermalAgingContext.Enabled && deltaTimeHours > 0)
            {
                double years = deltaTimeHours / (365.25 * 24.0);
                double arr = Thermal.ThermalAgingContext.ArrheniusFactor(temperatureCelsius);
                _calendarAgeAccum += years * Thermal.ThermalAgingContext.CalendarAgingPerYearAtRef * arr;
                _calendarAgeAccum = Math.Min(1.0, _calendarAgeAccum);
            }

            return Math.Min(1.0, baseAge + _calendarAgeAccum);
        }

        // 示例使用
        //public static void EssMain(string[] args)
        //{
        //    // 创建电芯规格
        //    var specs = new CellSpecifications
        //    {
        //        NominalCapacity = 3.2,    // 3.2Ah
        //        NominalVoltage = 3.2,     // 3.2V
        //        MinVoltage = 2.5,         // 2.5V
        //        MaxVoltage = 3.65,        // 3.65V
        //        InitialSOC = 0.5,         // 初始50% SOC
        //        InternalResistance = 0.02, // 20mOhm
        //        Mass = 0.05,              // 50g
        //        Volume = 0.0001           // 100cm³
        //    };

        //    // 创建电芯仿真器
        //    var cell = new LiFePO4CellSimulator(specs);

        //    // 模拟充放电过程
        //    Console.WriteLine("开始模拟...");
        //    Console.WriteLine("时间\t电流(A)\tSOC\t电压(V)\t温度(°C)\t剩余容量(Ah)\t老化程度");

        //    // 1C恒流充电1小时
        //    for (int i = 0; i < 60; i++)
        //    {
        //        cell.Update(3.2, 25.0, TimeSpan.FromMinutes(1));
        //        PrintState(cell, i + 1);
        //    }

        //    // 静置30分钟
        //    for (int i = 0; i < 30; i++)
        //    {
        //        cell.Update(0.0, 25.0, TimeSpan.FromMinutes(1));
        //        PrintState(cell, 60 + i + 1);
        //    }

        //    // 1C恒流放电1小时
        //    for (int i = 0; i < 60; i++)
        //    {
        //        cell.Update(-3.2, 25.0, TimeSpan.FromMinutes(1));
        //        PrintState(cell, 90 + i + 1);
        //    }
        //}

        private static void PrintState(LiFePO4CellSimulator cell, int minute)
        {
            //var state = cell.GetCurrentState();
            //Console.WriteLine($"{minute}min\t{state.Current:F1}\t{state.SOC * 100:F1}%\t" +
            //                  $"{state.Voltage:F3}\t{state.Temperature:F1}\t" +
            //                  $"{state.RemainingCapacity:F3}\t{state.Age * 100:F2}%");
        }
    }
}
