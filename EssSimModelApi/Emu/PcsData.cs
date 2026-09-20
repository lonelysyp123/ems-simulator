using EssSimulator.EssDeviceSimModel;

namespace EssSimulator.EssSimModelApi.EnergyManagementSystem
{

    /// <summary>
    /// PCS数据模型
    /// </summary>
    public class PcsData
    {
        public int PcsId { get; set; }

        // 交流侧电气参数
        public float LineVoltageAB { get; set; }           // 线电压AB
        public float LineVoltageBC { get; set; }           // 线电压BC
        public float LineVoltageCA { get; set; }           // 线电压CA
        public float Frequency { get; set; }               // 频率
        public float PhaseACurrent { get; set; }           // A相电流
        public float PhaseBCurrent { get; set; }           // B相电流
        public float PhaseCCurrent { get; set; }           // C相电流

        // 直流侧电气参数
        public float BatteryVoltage { get; set; }          // 电池电压
        public float BatteryCurrent { get; set; }          // 电池电流
        public float BatteryPower { get; set; }            // 电池功率 kW

        // 功率相关
        public float ApparentPower { get; set; }            // 视在功率
        public float ActivePower { get; set; }             // 有功功率
        public float AvailableCapacity { get; set; }        // 当前可用容量
        public float ReactivePower { get; set; }           // 无功功率
        public float PowerFactor { get; set; }             // 功率因数

        // 能量统计
        public float TotalChargeEnergy { get; set; }       // 总充电量
        public float TotalDischargeEnergy { get; set; }    // 总放电量
        public float DailyChargeEnergy { get; set; }       // 日充电量
        public float DailyDischargeEnergy { get; set; }    // 日放电量

        // 绝缘监测
        public float PositiveToGroundInsulation { get; set; } // 正对地绝缘阻抗值
        public float NegativeToGroundInsulation { get; set; } // 负对地绝缘阻抗值

        // 温度监测
        public float ControlCabinetTemp { get; set; }      // 控制舱温度
        public float ControlCabinetHumidity { get; set; }  // 控制舱湿度
        public float IGBTMaxTemp { get; set; }            // IGBT最大温度

        /// <summary>develop 点表别名（emu.csv yc34/yc61）。</summary>
        public float IGBTTemp
        {
            get => IGBTMaxTemp;
            set => IGBTMaxTemp = value;
        }

        public float PhaseAIGBTTemp { get; set; }            // A相IGBT温度
        public float PhaseBIGBTTemp { get; set; }            // B相IGBT温度
        public float PhaseCIGBTTemp { get; set; }            // C相IGBT温度
        public float AmbientTemp { get; set; }            // 环境温度

        // 保护参数
        public float BatteryChargeProtectionVoltage { get; set; } // 电池充电保护电压
        public float BatteryDischargeProtectionVoltage { get; set; } // 电池放电保护电压
        public float BatteryChargeProtectionCurrent { get; set; } // 电池充电保护电流
        public float BatteryDischargeProtectionCurrent { get; set; } // 电池放电保护电流

        // 限制参数
        public float BatteryChargeCurrentLimit { get; set; } // 电池充电限流点
        public float BatteryChargeVoltageLimit { get; set; } // 电池充电限压点
        public float BatteryDischargeCurrentLimit { get; set; } // 电池放电限流点
        public float BatteryDischargeVoltageLimit { get; set; } // 电池放电限压点
        public float BatteryChargePowerLimit { get; set; }  // 电池充电限功率点
        public float BatteryDischargePowerLimit { get; set; } // 电池放电限功率点
        public float ChargePowerLimit { get; set; }         // 充电功率限值
        public float DischargePowerLimit { get; set; }      // 放电功率限值

        public float DCCCLimit { get; set; }                // 直流恒流电流
        public float DCCVLimit { get; set; }                // 直流恒压电压
        public int ActiveReactivePriority { get; set; }    // 有功无功优先

        // 控制命令
        public bool ChargeProhibited { get; set; }         // 禁止充电
        public bool DischargeProhibited { get; set; }       // 禁止放电

        /// <summary>PCS 开关机（EMS/Modbus 可写）。默认 false，仅外部写 1 后允许进入运行；写 0 为停机。</summary>
        public bool pcsOnOffSwitch { get; set; } = false;

        /// <summary>物理仿真模式（由 MapPcsState 同步），用于 OperationStatus 与界面展示。</summary>
        public OperationMode SimulatorMode { get; set; }

        /// <summary>孤岛电压设定（V，线电压 0–690）；离网建压/黑启动由 EMS 调节。</summary>
        public ushort IslandVoltageSetting { get; set; }

        /// <summary>孤岛频率设定（Hz）；0 表示沿用额定频率。</summary>
        public float IslandFrequencySetting { get; set; }

        /// <summary>PCS 内部有效孤岛电压反馈（V），在 IslandVoltageRampDurationMs 内趋近设定值。</summary>
        public float IslandVoltageFeedback { get; set; }

        /// <summary>黑启动开启：EMS 有功/无功设定无效，PCS 按孤岛电压内环自动调节有功（V/f 建压）。</summary>
        public bool BlackStartEnabled { get; set; }

        public bool gridOnOffSwitch { get; set; }           //并离网控制及状态

        // 状态（由 MapPcsState 按物理仿真态写入，与界面 GetRunPhaseLabel 一致）
        public int OperationStatus { get; set; }   // 1停机 2待机 4充电 5放电 6未知/故障

        /// <summary>PCS 当前故障（emu yc46）：运行状态为故障，或驱动 / BMS 系统故障。</summary>
        public ushort CurrentFault =>
            (ushort)((OperationStatus == 6 || DriveFault || BmsSystemFault) ? 1 : 0);

        public float PCSActivePowerSetting { get; set; }    //有功率设置
        public float PCSReactivePowerSetting { get; set; }    //无功率设置  
        public float PCSRatePower { get; set; }    //额定功率设置

        // 内部特性
        public float PowerRampRate { get; set; }            // 功率斜率
        public float StartStopRampRate { get; set; }        // 启停斜率

        // 告警1
        public bool InsulationAlarm { get; set; }           // 绝缘阻抗异常告警
        public bool LeakageCurrentAbnormal { get; set; }    // 漏电流异常
        public bool DcOverVoltage { get; set; }             // 直流过压
        public bool GridOverVoltage { get; set; }           // 电网过压异常
        public bool GridUnderVoltage { get; set; }          // 电网欠压异常
        public bool GridOverFrequency { get; set; }         // 电网过频异常
        public bool GridUnderFrequency { get; set; }        // 电网欠频异常
        public bool PowerModuleOverTemp { get; set; }       // 功率模块过温
        public bool GridPhaseSequenceAbnormal { get; set; } // 电网相序异常
        public bool InverterSoftwareOverCurrent { get; set; } // 逆变软件过流
        public bool DcSoftStartAbnormal { get; set; }       // 直流软启动异常
        public bool AcFanAbnormal { get; set; }             // 交流风机异常
        public bool AcMainSwitchAbnormal { get; set; }      // 交流主开关异常
        public bool InternalAbnormal { get; set; }          // 内部异常
        public UInt16 AlarmSummary1
        {
            get
            {
                UInt16 summary = 0;
                summary |= (UInt16)(ToBit(InsulationAlarm) << 0);
                summary |= (UInt16)(ToBit(LeakageCurrentAbnormal) << 1);
                summary |= (UInt16)(ToBit(DcOverVoltage) << 2);
                summary |= (UInt16)(ToBit(GridOverVoltage) << 3);
                summary |= (UInt16)(ToBit(GridUnderVoltage) << 4);
                summary |= (UInt16)(ToBit(GridOverFrequency) << 5);
                summary |= (UInt16)(ToBit(GridUnderFrequency) << 6);
                summary |= (UInt16)(ToBit(PowerModuleOverTemp) << 7);
                summary |= (UInt16)(ToBit(GridPhaseSequenceAbnormal) << 8);
                summary |= (UInt16)(ToBit(InverterSoftwareOverCurrent) << 9);
                summary |= (UInt16)(ToBit(DcSoftStartAbnormal) << 10);
                summary |= (UInt16)(ToBit(false) << 11);
                summary |= (UInt16)(ToBit(AcFanAbnormal) << 12);
                summary |= (UInt16)(ToBit(AcMainSwitchAbnormal) << 13);
                summary |= (UInt16)(ToBit(false) << 14);
                summary |= (UInt16)(ToBit(InternalAbnormal) << 15);
                return summary;
            }
        }

        // 告警2
        public bool InternalOverTemp { get; set; }          // 机内过温
        public bool AcSoftStartAbnormal { get; set; }       // 交流软启动异常
        public bool HeatExchangerFault { get; set; }        // 热交换机故障
        public bool AcSurgeProtectorAbnormal { get; set; }  // 交流防雷器异常
        public bool InternalEmergencyStopFault { get; set; } // 内部急停故障
        public bool ExternalEmergencyStopFault { get; set; } // 外部急停故障
        public bool BusVoltageNotReady { get; set; }        // 母线电压不符合开机条件
        public bool BusOverCurrent { get; set; }            // 母线电流过流
        public bool DoorAlarm { get; set; }                 // 门禁告警
        public bool PllAbnormal { get; set; }               // 锁相异常
        public bool DcSurgeProtectorAbnormal { get; set; }  // 直流防雷器异常
        public bool InverterHardwareOverCurrent { get; set; } // 逆变硬件过流
        public bool DriveFault { get; set; }                // 驱动故障
        public bool IdConflict { get; set; }                // ID 冲突
        public UInt16 AlarmSummary2
        {
            get
            {
                UInt16 summary = 0;
                summary |= (UInt16)(ToBit(InternalOverTemp) << 0);
                summary |= (UInt16)(ToBit(AcSoftStartAbnormal) << 1);
                summary |= (UInt16)(ToBit(HeatExchangerFault) << 2);
                summary |= (UInt16)(ToBit(AcSurgeProtectorAbnormal) << 3);
                summary |= (UInt16)(ToBit(InternalEmergencyStopFault) << 4);
                summary |= (UInt16)(ToBit(ExternalEmergencyStopFault) << 5);
                summary |= (UInt16)(ToBit(BusVoltageNotReady) << 6);
                summary |= (UInt16)(ToBit(BusOverCurrent) << 7);
                summary |= (UInt16)(ToBit(false) << 8);
                summary |= (UInt16)(ToBit(DoorAlarm) << 9);
                summary |= (UInt16)(ToBit(PllAbnormal) << 10);
                summary |= (UInt16)(ToBit(DcSurgeProtectorAbnormal) << 11);
                summary |= (UInt16)(ToBit(false) << 12);
                summary |= (UInt16)(ToBit(InverterHardwareOverCurrent) << 13);
                summary |= (UInt16)(ToBit(DriveFault) << 14);
                summary |= (UInt16)(ToBit(IdConflict) << 15);
                return summary;
            }
        }

        // 告警3
        public bool MainsUnbalance { get; set; }            // 市电不平衡（备用）
        public bool SmokeAlarm { get; set; }                // 烟雾告警
        public bool ParallelCanCommFault { get; set; }      // 并机 CAN 通讯异常
        public bool HmiCanCommFault { get; set; }           // HMI CAN 通讯异常
        public bool ModelSettingError { get; set; }         // 机型设置错误
        public bool Hmi485CommFault { get; set; }           // HMI 485 通讯异常
        public bool RemoteCommFault { get; set; }           // 远程通讯故障
        public UInt16 AlarmSummary3
        {
            get
            {
                UInt16 summary = 0;
                summary |= (UInt16)(ToBit(false) << 0);
                summary |= (UInt16)(ToBit(false) << 1);
                summary |= (UInt16)(ToBit(false) << 2);
                summary |= (UInt16)(ToBit(false) << 3);
                summary |= (UInt16)(ToBit(false) << 4);
                summary |= (UInt16)(ToBit(MainsUnbalance) << 5);
                summary |= (UInt16)(ToBit(SmokeAlarm) << 6);
                summary |= (UInt16)(ToBit(ParallelCanCommFault) << 7);
                summary |= (UInt16)(ToBit(HmiCanCommFault) << 8);
                summary |= (UInt16)(ToBit(ModelSettingError) << 9);
                summary |= (UInt16)(ToBit(Hmi485CommFault) << 10);
                summary |= (UInt16)(ToBit(RemoteCommFault) << 11);
                summary |= (UInt16)(ToBit(false) << 12);
                summary |= (UInt16)(ToBit(false) << 13);
                summary |= (UInt16)(ToBit(false) << 14);
                summary |= (UInt16)(ToBit(false) << 15);
                return summary;
            }
        }

        // 告警4
        public bool LvrtRunning { get; set; }               // 低电压穿越运行
        public bool HvrtRunning { get; set; }               // 高电压穿越运行
        public bool DcFanAbnormal { get; set; }             // 直流风机异常
        public bool HeatsinkTempSwitchAbnormal { get; set; } // 散热器温度开关异常
        public bool ExternalTempSwitchAbnormal { get; set; } // 外部温度开关异常
        public bool AuxTransformerTempSwitchAbnormal { get; set; } // 辅源变压器温度开关异常
        public bool InductorTempSwitchAbnormal { get; set; } // 电感温度开关异常
        public bool PositiveGroundAbnormal { get; set; }    // 正极接地异常
        public bool NegativeGroundAbnormal { get; set; }    // 负极接地异常
        public bool AcGroundAbnormal { get; set; }          // 交流接地异常
        public bool GridGroundAbnormal { get; set; }        // 并网接地异常
        public bool InductorFanAbnormal { get; set; }       // 电感风机异常
        public UInt16 AlarmSummary4
        {
            get
            {
                UInt16 summary = 0;
                summary |= (UInt16)(ToBit(false) << 0);
                summary |= (UInt16)(ToBit(LvrtRunning) << 1);
                summary |= (UInt16)(ToBit(HvrtRunning) << 2);
                summary |= (UInt16)(ToBit(DcFanAbnormal) << 3);
                summary |= (UInt16)(ToBit(HeatsinkTempSwitchAbnormal) << 4);
                summary |= (UInt16)(ToBit(ExternalTempSwitchAbnormal) << 5);
                summary |= (UInt16)(ToBit(AuxTransformerTempSwitchAbnormal) << 6);
                summary |= (UInt16)(ToBit(InductorTempSwitchAbnormal) << 7);
                summary |= (UInt16)(ToBit(PositiveGroundAbnormal) << 8);
                summary |= (UInt16)(ToBit(NegativeGroundAbnormal) << 9);
                summary |= (UInt16)(ToBit(AcGroundAbnormal) << 10);
                summary |= (UInt16)(ToBit(GridGroundAbnormal) << 11);
                summary |= (UInt16)(ToBit(InductorFanAbnormal) << 12);
                summary |= (UInt16)(ToBit(false) << 13);
                summary |= (UInt16)(ToBit(false) << 14);
                summary |= (UInt16)(ToBit(false) << 15);
                return summary;
            }
        }

        // 告警5
        public bool BatteryOverVoltage { get; set; }        // 电池过压
        public bool BatteryUnderVoltage { get; set; }       // 电池欠压
        public bool DcOverCurrent { get; set; }             // 直流过流
        public bool OutputOverVoltage { get; set; }         // 输出电压异常
        public bool OutputVoltageNotReadyForGrid { get; set; } // 输出电压不符合离网条件
        public bool OverloadProtection { get; set; }        // 过载保护
        public bool ShortCircuitProtection { get; set; }    // 短路保护
        public bool DcFuseAbnormal { get; set; }            // 直流保险丝异常
        public bool BatteryHeavyLoadUnderVoltage { get; set; } // 电池重载欠压
        public bool BatteryLowVoltageWarning { get; set; }  // 电池低压告警
        public bool BatteryReverseConnection { get; set; }  // 电池反接
        public bool BatteryVoltageNotReadyForCharge { get; set; } // 电池电压不符合充电条件
        public bool OverloadWarning { get; set; }           // 过载告警
        public UInt16 AlarmSummary5
        {
            get
            {
                UInt16 summary = 0;
                summary |= (UInt16)(ToBit(BatteryOverVoltage) << 0);
                summary |= (UInt16)(ToBit(BatteryUnderVoltage) << 1);
                summary |= (UInt16)(ToBit(DcOverCurrent) << 2);
                summary |= (UInt16)(ToBit(OutputOverVoltage) << 3);
                summary |= (UInt16)(ToBit(OutputVoltageNotReadyForGrid) << 4);
                summary |= (UInt16)(ToBit(OverloadProtection) << 5);
                summary |= (UInt16)(ToBit(ShortCircuitProtection) << 6);
                summary |= (UInt16)(ToBit(false) << 7);
                summary |= (UInt16)(ToBit(DcFuseAbnormal) << 8);
                summary |= (UInt16)(ToBit(BatteryHeavyLoadUnderVoltage) << 9);
                summary |= (UInt16)(ToBit(BatteryLowVoltageWarning) << 10);
                summary |= (UInt16)(ToBit(false) << 11);
                summary |= (UInt16)(ToBit(BatteryReverseConnection) << 12);
                summary |= (UInt16)(ToBit(BatteryVoltageNotReadyForCharge) << 13);
                summary |= (UInt16)(ToBit(OverloadWarning) << 14);
                summary |= (UInt16)(ToBit(false) << 15);
                return summary;
            }
        }

        // 告警6（BMS相关）
        public bool BmsSystemFault { get; set; }            // BMS 系统故障
        public bool BmsCommFault { get; set; }              // BMS 通信异常
        public bool BmsDryContactAbnormal { get; set; }     // BMS 干接点异常
        public bool BmsChargeProhibit { get; set; }         // BMS 禁充
        public bool BmsDischargeProhibit { get; set; }      // BMS 禁放
        public bool BmsStandby { get; set; }                // BMS 待机
        public bool BmsAlarm { get; set; }                  // BMS 告警
        public UInt16 AlarmSummary6
        {
            get
            {
                UInt16 summary = 0;
                summary |= (UInt16)(ToBit(BmsSystemFault) << 0);
                summary |= (UInt16)(ToBit(BmsCommFault) << 1);
                summary |= (UInt16)(ToBit(BmsDryContactAbnormal) << 2);
                summary |= (UInt16)(ToBit(BmsChargeProhibit) << 3);
                summary |= (UInt16)(ToBit(BmsDischargeProhibit) << 4);
                summary |= (UInt16)(ToBit(BmsStandby) << 5);
                summary |= (UInt16)(ToBit(BmsAlarm) << 6);
                summary |= (UInt16)(ToBit(false) << 7);
                summary |= (UInt16)(ToBit(false) << 8);
                summary |= (UInt16)(ToBit(false) << 9);
                summary |= (UInt16)(ToBit(false) << 10);
                summary |= (UInt16)(ToBit(false) << 11);
                summary |= (UInt16)(ToBit(false) << 12);
                summary |= (UInt16)(ToBit(false) << 13);
                summary |= (UInt16)(ToBit(false) << 14);
                summary |= (UInt16)(ToBit(false) << 15);
                return summary;
            }
        }

        // 告警7
        public bool AntiPidModuleAbnormal { get; set; }     // 防 PID 模块异常
        public bool PhaseSyncAbnormal { get; set; }         // 相位同步异常
        public bool DcPathConfigAbnormal { get; set; }      // 直流路径数配置异常
        public bool AntiIslandingAbnormal { get; set; }     // 防孤岛异常
        public bool GroundLoopAbnormal { get; set; }        // 接地回路异常
        public bool MidpointContactAbnormal { get; set; }   // 中点接触器异常
        public bool AcSurgeSuppressionAbnormal { get; set; } // 交流缓冲异常
        public bool SystemOverTemp { get; set; }            // 系统过温
        public bool SystemOverHumidity { get; set; }        // 系统过湿
        public bool PvPolarityReverse { get; set; }         // PV 极性反接
        public bool TransformerOverTemp { get; set; }       // 箱变超温
        public bool DcdcCommAbnormal { get; set; }          // 储能 DCDC 通讯异常
        public bool DcdcRunningAbnormal { get; set; }       // 储能 DCDC 运行异常
        public UInt16 AlarmSummary7
        {
            get
            {
                UInt16 summary = 0;
                summary |= (UInt16)(ToBit(AntiPidModuleAbnormal) << 0);
                summary |= (UInt16)(ToBit(PhaseSyncAbnormal) << 1);
                summary |= (UInt16)(ToBit(DcPathConfigAbnormal) << 2);
                summary |= (UInt16)(ToBit(AntiIslandingAbnormal) << 3);
                summary |= (UInt16)(ToBit(false) << 4);
                summary |= (UInt16)(ToBit(false) << 5);
                summary |= (UInt16)(ToBit(GroundLoopAbnormal) << 6);
                summary |= (UInt16)(ToBit(MidpointContactAbnormal) << 7);
                summary |= (UInt16)(ToBit(AcSurgeSuppressionAbnormal) << 8);
                summary |= (UInt16)(ToBit(false) << 9);
                summary |= (UInt16)(ToBit(SystemOverTemp) << 10);
                summary |= (UInt16)(ToBit(SystemOverHumidity) << 11);
                summary |= (UInt16)(ToBit(PvPolarityReverse) << 12);
                summary |= (UInt16)(ToBit(TransformerOverTemp) << 13);
                summary |= (UInt16)(ToBit(DcdcCommAbnormal) << 14);
                summary |= (UInt16)(ToBit(DcdcRunningAbnormal) << 15);
                return summary;
            }
        }

        private static UInt16 ToBit(bool? flag)
        {
            return flag == true ? (UInt16)1 : (UInt16)0;
        }

        // ============ 通用告警/扩展区（适配多厂家点表） ============

        /// <summary>
        /// 自定义告警字：字编号自 7 起（0~6 由 AlarmSummary1~7 占用）。
        /// 新厂家点表只需读写 CustomAlarmWords[n]，无需再向 PcsData 追加大量 bool 字段。
        /// </summary>
        public Dictionary<int, UInt16> CustomAlarmWords { get; } = new Dictionary<int, UInt16>();

        /// <summary>置/清指定自定义告警字的某位（word ≥7；bit 0~15，字不存在时自动创建）。</summary>
        public void SetAlarmBit(int word, int bit, bool value)
        {
            if (word < 7 || bit < 0 || bit > 15)
                return;
            UInt16 current = CustomAlarmWords.TryGetValue(word, out var w) ? w : (UInt16)0;
            UInt16 mask = (UInt16)(1 << bit);
            CustomAlarmWords[word] = value ? (UInt16)(current | mask) : (UInt16)(current & ~mask);
        }

        /// <summary>按字编号读告警字：0~6 → AlarmSummary1~7，≥7 → CustomAlarmWords。</summary>
        public UInt16 GetAlarmWord(int word) => word switch
        {
            0 => AlarmSummary1,
            1 => AlarmSummary2,
            2 => AlarmSummary3,
            3 => AlarmSummary4,
            4 => AlarmSummary5,
            5 => AlarmSummary6,
            6 => AlarmSummary7,
            _ => CustomAlarmWords.TryGetValue(word, out var w) ? w : (UInt16)0
        };

        /// <summary>数值遥测扩展区（反射可绑 emuN.PcsList[i].ExtraAnalogValues[j]，供新厂家点表按槽位映射）。</summary>
        public float[] ExtraAnalogValues { get; set; } = new float[16];

        /// <summary>数值遥信扩展区（反射可绑 emuN.PcsList[i].ExtraDigitalValues[j]）。</summary>
        public bool[] ExtraDigitalValues { get; set; } = new bool[16];

        /// <summary>PTP 同步状态：0关闭 1未锁定 2捕获 3已锁定 4守时。未启用时为 0，不对时告警。</summary>
        public ushort PtpSyncStatus { get; set; }

        /// <summary>相对主钟残余偏差（ns，软件戳）。</summary>
        public long PtpOffsetFromMasterNs { get; set; }

        /// <summary>P2P 平均路径时延（ns）。</summary>
        public long PtpMeanPathDelayNs { get; set; }

        /// <summary>当前时间品质（ns）。锁定时约 100 μs 档。</summary>
        public long PtpTimeAccuracyNs { get; set; }

        /// <summary>对时异常：未锁定或守时。PTP 关闭时为 false。</summary>
        public bool PtpLostAlarm { get; set; }
    }
}
