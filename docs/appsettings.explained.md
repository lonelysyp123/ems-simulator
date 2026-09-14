# appsettings.json 字段说明（EssSimulator）

本文档用于解释 `appsettings.json` 中每个字段的含义与影响范围。`appsettings.json` 本身保持为**纯 JSON**（不包含注释），便于与第三方工具/格式化器兼容。

字段定义以 `Configuration/`、`DataExchange/Config/`、`EssDeviceSimModel/Model/MeterConfig.cs` 中的强类型配置类为准。

## 总体单位与约定

- **功率**：kW（有功）、kvar（无功）
- **电压**：V（一次侧/二次侧均使用 V；界面可能会换算成 kV 显示）
- **电流**：A
- **频率**：Hz
- **SOC**：0~1（配置里）、界面/对外展示可能换算为 %
- **变压器容量**：配置字段名为 `RatedPower`，单位为 **kVA**

## 顶层结构

`appsettings.json` 的顶层包含以下配置段：

| 配置段 | 绑定类型 | 说明 |
|--------|----------|------|
| `Simulator` | `SimulatorConfig` | 运行时、协议端口、Web、档位、授权；`PvUnits` 通常由组态 overlay 写入 |
| `EssUnits` | `List<EssUnitConfig>` → `SimulatorConfig.Devices` | 储能单元清单（每单元 PCS/BMS 台数可配，未配默认 2） |
| `DataExchange` | `DataExchangeOptions` | simEmu / simBms / simEm 遥测与控制轮询 |
| `Pcc` | `PccConfig` | 220kV 并网点无功—电压模型 |
| `Meter` | `MeterConfig` | 并网电表 PT/CT 与上报侧 |
| `Transformer` | `TransformerConfig` | 主变（220kV/35kV） |
| `UnitTransformer` | `UnitTransformerConfig` | 单元变（35kV/690V） |
| `Load` | `LoadConfig` | 站内负载计划 |
| `Pcs` | `PcsPhysicalConfig` | PCS 物理参数（全局，各通道共用） |

> **注意**：单元列表写在顶层 **`EssUnits`** 数组，而非 `Simulator.Devices`。程序启动时通过 `PostConfigure` 将 `EssUnits` 绑定到 `SimulatorConfig.Devices`（见 `Program.cs`）。光伏单元列表在 **`Simulator.PvUnits`**（工程模式由 overlay 覆盖；根配置通常为空）。

---

## 当前配置快照（仓库根 `appsettings.json`）

以下为关键取值摘要；完整 JSON 以仓库文件为准。

- **`Simulator.Runtime`**
  - `IntegrationStepMultiplier = 1.0`
  - `AutoStartPcsOnStartup = true`
  - `PropagationIntervalMs = 100`
  - `PropagationQuvMaxIterations = 3`
  - `PropagationVoltageTolerancePu = 0.001`
- **`Simulator.Protocol`**
  - `BaseBmsModbusPort = 1501`，`BmsPortStep = 1`
  - `BaseEmuModbusPort = 1601`，`EmuPortStep = 1`
  - `EmModbusPort = 1500`
  - `EnableLocalControl = true`
  - `BaseLocalControlModbusPort = 1701`，`LocalControlPortStep = 1`，`LocalControlEmuPerGroup = 4`
  - `BasePvLoggerModbusPort = 1801`，`PvLoggerPortStep = 1`
  - `BasePvMeterModbusPort = 1901`，`PvMeterPortStep = 1`
- **`EssUnits`**
  - 共 **8** 个 Unit（Unit-1 ~ Unit-8），每单元 2 路 PCS + 2 路 BMS
  - BMS 拓扑一致：`ClusterCount=12`、`PackCount=4`、`CellSeriesCount=104`、`CellParallelCount=1`
  - 单体：`CellNominalVoltage=3.2`、`CellNominalCapacity=314`
  - 各 BMS 的 `CellInitialSoc` 按单元单独配置（见 JSON）；初始 SOC 精确取值，不再施加随机扰动
- **`DataExchange`**
  - `TelemetryIntervalMs = 500`，`EmuTelemetryIntervalMs = 100`
  - `ControlEventDriven = true`，`ControlPollIntervalMs = 100`
- **`Pcc`**
  - `NominalLineVoltage=220000`、`ShortCircuitMva=750`、`MaxVoltageShiftPercent=5`
  - `ReactiveVoltageInfluenceCoefficient=1.0`、`StationBusNominalLineVoltage=35000`
- **`Pcs`**
  - `RatedPower=1250`、`MaxPower=1250`、`Efficiency=0.99`
  - `DcVoltageRangeMin=1000`、`DcVoltageRangeMax=1500`
  - `AcVoltageNominal=690`、`FrequencyNominal=50`
  - `MaxCurrent=1100`、`GridLossCoefficient=0.11`
  - 黑启动相关见 JSON（如 `BlackStartBusEnergizedFraction=0.99`）
- **`Transformer`**
  - `RatedPower=31500`、`PrimaryVoltage=220000`、`SecondaryVoltage=35000`
  - `NoLoadLoss=100`、`LoadLoss=200`、`ImpedancePercent=4`
  - `ReactiveVoltageInfluenceCoefficient=0.7`、`NoLoadCurrentPercent=2`
  - 励磁涌流参数均已启用（见 JSON）
- **`UnitTransformer`**
  - `RatedPower=6300`、`PrimaryVoltage=35000`、`SecondaryVoltage=690`
  - `ImpedancePercent=5`，其余损耗/涌流参数见 JSON
- **`Load`**
  - `ActivePowerPlan=-500`、`ReactivePowerPlan=0`

---

## Simulator

### Simulator.Runtime

- `Simulator.Runtime.IntegrationStepMultiplier`（double，无量纲，默认 `1.0`）
  - **作用**：积分步长倍数（SOC、电能等积分量的 dt 放大系数）。
  - **影响**：仅放大**积分量**的时间步长，不改变瞬时值动力学；基于真实回调间隔 × 本倍数。

- `Simulator.Runtime.AutoStartPcsOnStartup`（bool，默认 `true`）
  - **作用**：`ess` 与 `simEmu*` Modbus 就绪后，向各 PCS 启停控制点写入运行指令并驱动并网。
  - **影响**：`false` 时保持停机，须 EMS/mbpoll 手动写启停点。

- `Simulator.Runtime.PropagationIntervalMs`（int，ms，默认 `100`）
  - **作用**：电气传播主循环的真实休眠间隔（电压源激活性传播周期）。
  - **影响**：越小刷新越快、CPU 占用越高；主循环 `PeriodicTimer` 周期不低于 10 ms。

- `Simulator.Runtime.PropagationQuvMaxIterations`（int，≥1，默认 `3`）
  - **作用**：设备 Step 后 Q-U 与电压传播的最大迭代轮数。

- `Simulator.Runtime.PropagationVoltageTolerancePu`（double，pu，默认 `0.001`）
  - **作用**：Q-U/V 迭代相对电压收敛阈值（相对 PCC 额定线电压）。

- `Simulator.Runtime.PcsRamp`（对象，可选）
  - **作用**：全局默认 PCS 功率爬坡参数。
  - **影响**：当 `EssUnits[].Pcs[].PcsRamp` 未配置时回退到此默认值。
  - 子字段：`Slope`（double）、`IntervalMs`（int，ms）、`DelayMs`（int，ms）。

### Simulator.Protocol

- `Simulator.Protocol.BaseBmsModbusPort` / `BmsPortStep`（int）
  - **作用**：BMS Modbus TCP 起始端口与步长。BMS 路 N 端口 = `BaseBmsModbusPort + (N-1) × BmsPortStep`。

- `Simulator.Protocol.BaseEmuModbusPort` / `EmuPortStep`（int）
  - **作用**：EMU（每储能单元一个从站）起始端口与步长。

- `Simulator.Protocol.BaseEmuIec61850Port` / `EmuIec61850PortStep`（int，默认 `8102` / `1`）
  - **作用**：PCS IEC 61850 MMS 起始端口与步长。第 N 台 = `Base + (N-1) × Step`（也可由 `configs/protocol-bindings.json` 覆盖）。

- `Simulator.Protocol.EmuIec61850GooseInterface`（string?）
  - **作用**：入向 GOOSE 二层网卡名。空=按 OS 默认（macOS `en0` / Windows `0` / Linux `eth0`）；`none`/`off`=不收二层 GOOSE。须与外部发布端同一二层网段。

- `Simulator.Protocol.EmuIec61850GooseSubscribe`（bool，默认 `true`）
  - **作用**：是否订阅入向 GOOSE（外部 EMS/IED 遥控 yk/yt）。仿真器**不发布**设备侧 GOOSE。

- `Simulator.Protocol.EmuIec61850GooseSubscribeAppIdBase`（int，默认 `0x2000`）
  - **作用**：入向 AppID 基数；PCS N 使用 `base+N`（PCS1=`0x2001`=8193）。

- `Simulator.Protocol.EmuIec61850GooseSubscribeGoCbRef`（string?）
  - **作用**：入向 GoCbRef 过滤。空=按台使用默认 `EMS_PCSxxPCS/LLN0$GO$GoCB1`（与 `ems_goose.icd` 一致）。

- `Simulator.Protocol.EmModbusPort`（int）
  - **作用**：并网电表（`simEm`）Modbus TCP 端口。

- `Simulator.Protocol.EnableLocalControl`（bool，默认 `false`）
  - **作用**：是否启用 LocalControl 聚合 Modbus 服务（每路聚合多个 EMU/PCS）。

- `Simulator.Protocol.BaseLocalControlModbusPort` / `LocalControlPortStep`（int）
  - **作用**：LocalControl 从站端口起始与步长。

- `Simulator.Protocol.LocalControlEmuPerGroup`（int，默认 `4`）
  - **作用**：每路 LocalControl 聚合的 EMU 数量。

- `Simulator.Protocol.BasePvLoggerModbusPort` / `PvLoggerPortStep`（int，默认 `1801` / `1`）
  - **作用**：光伏 Logger（`simPvN`）起始端口与步长。仅当 `PvUnits` 非空时注册。

- `Simulator.Protocol.BasePvMeterModbusPort` / `PvMeterPortStep`（int，默认 `1901` / `1`）
  - **作用**：光伏低压电表（`simPvMeterN`）起始端口与步长。

独立文件 **`configs/modbus-allowlist.json`**（非 appsettings 字段）：Modbus TCP 来源 IP 白名单。空名单不限制；非空时仅放行精确 IP 或 CIDR，可选放行本机回环。Web「协议端口」页可改，**保存后须重启进程**才作用于 Accept。

### Simulator.PvUnits

绑定到 `SimulatorConfig.PvUnits`。根 `appsettings.json` 通常为空数组；工程模式由组态 overlay 覆盖。无储能仅有光伏时 `EffectiveEssUnitCount=0`，不伪造 BMS/PCS。

| 字段 | 默认 | 说明 |
|------|------|------|
| `Name` | `"PV"` | 展示名 |
| `InverterCount` | 16 | 箱变下组串逆变器台数 |
| `StringCount` / `ModulesPerString` | 16 / 30 | 单台逆变器簇数、每簇串联块数 |
| `InverterRatedPowerKw` / `InverterMaxPowerKw` | 320 / 352 | 单台额定/最大功率（kW） |
| `InverterEfficiency` | 0.99 | 逆变器效率 |
| `InverterAcVoltageV` | 690 | 逆变器交流线电压 |
| `UnitXfPrimaryV` / `UnitXfSecondaryV` / `UnitXfRatedKva` | 35000 / 690 / 5120 | 光伏箱变 |
| `DcVoltageMin` / `DcVoltageMax` | 500 / 1500 | 直流电压范围 |
| `ModuleModel` | `TSM-NEG21C.20Q` | 须在 `TrinaPvModuleCatalog` 内 |
| `GroundAlbedo` | 0 | 方阵 A/B 反照率初值；0 关闭双面增益 |
| `OperatingYears` | 0 | 投运年限；0 为新产品、无衰减 |

### Simulator.Web

- `HttpPort` / `HttpBaseUrl`：浏览器与 REST 监听（默认 5050；macOS 勿用 5000）
- `StaticFiles`：是否托管 `wwwroot/`
- `SnapshotIntervalMs`：SignalR 主接线推送周期
- `ApiKeyEnabled` / `ApiKey`：保护 `/api/*`（`/api/health` 豁免）；密钥可用环境变量 `Simulator__Web__ApiKey`
- `DroopSliceCaptureEnabled` / `DroopSliceMaxCount`：白盒切片采集（社区版会被档位关掉）

详见 [B/S 架构说明](./B-S架构说明.md)。

### Simulator.Edition

- `Name`：`Community`/`社区版`、`Commercial`/`商业版`、`Custom`/`定制版`
- `AllowDroopSlices` / `AllowMainline3d` / `AllowTopologyEditor`：高级 UI/API；社区版 `ApplyPresets` 强制关闭
- `LockTopology` / `MaxEssUnits`：社区版锁定单元上限

详见 [产品分档与交付边界](./产品分档与交付边界.md)。

### Simulator.License

- `Required`：是否校验 `license.txt`（商业/定制默认需要；社区/演示/开发根配置通常为 false）
- `FileName`：授权文件名，默认 `license.txt`

详见 [授权说明](./授权说明.md)。

---

## EssUnits（储能单元数组）

对应 `EssUnitsConfig.Section = "EssUnits"`，绑定到 `SimulatorConfig.Devices`。

约定：

- **每个 Unit 的 PCS/BMS 台数可配**；未配置时默认 2 路（向后兼容）
- Unit 数量决定 EMU 从站数、BMS 路数及通道扩展；纯光伏工程可以为 0

### EssUnits[i].Name

- **作用**：单元名称（展示/占位，不参与电气计算）。

### EssUnits[i].Pcs（数组；空则默认 2 项）

- `Name`：PCS 名称（占位）
- `PcsRamp`（可选）：覆盖该 PCS 的爬坡参数；为空时使用 `Simulator.Runtime.PcsRamp`
  - `Slope`：爬坡斜率（越大变化越快）
  - `IntervalMs`：每级更新间隔（ms）
  - `DelayMs`：新设定生效前延时（ms）

### EssUnits[i].Bms（数组；与 PCS 1:1，空则默认 2 项）

- `Name`：BMS 名称（占位）
- `ClusterCount` / `PackCount` / `CellSeriesCount` / `CellParallelCount`：电池拓扑
- `CellNominalVoltage`（V）/ `CellNominalCapacity`（Ah）：单体标称参数
- `CellInitialSoc`（0~1）：初始 SOC（精确取值）；`CellInitialSocRandomRange` 已废弃、忽略
- `PackInternalResistance` / `ClusterInternalResistance` / `RackInternalResistance`：等效内阻（简化模型）

---

## DataExchange（Modbus 与模型同步）

绑定到 `DataExchangeOptions`，用于 `simEmu*` / `simBms*` / `simEm` / `simPv*` / `simPvMeter*` 的 `DataExchangeSession`。

- `DataExchange.TelemetryIntervalMs`（int，ms，默认 `500`）
  - **作用**：BMS / rack 遥测管道周期：读模型 → 写 Modbus 寄存器。

- `DataExchange.EmuTelemetryIntervalMs`（int，ms，默认 `100`）
  - **作用**：EMU（PCS）遥测刷新周期，通常快于 BMS。

- `DataExchange.ControlEventDriven`（bool，默认 `true`）
  - **作用**：Modbus 外部写控制区后是否立即触发控制管道（轮询仍作兜底）。

- `DataExchange.ControlPollIntervalMs`（int，ms，默认 `100`）
  - **作用**：控制管道与反馈管道轮询周期：读 FC5/6 → 写模型 → 回写反馈。

- `DataExchange.ControlSemantics` / `ControlEffects`（字典，可选）
  - **作用**：按点名覆盖控制语义与副作用；通常由点表 CSV 注册，JSON 中可省略。

---

## Pcs（PCS 物理参数）

绑定到 `PcsPhysicalConfig`，经 `PcsDeviceFactory` 生成各通道 `PcsDevice`（爬坡另见 `EssUnits[].Pcs[].PcsRamp`）。

- `Pcs.RatedPower`（double，kW）
  - **作用**：额定功率标尺；映射到对外点表 `PCSRatePower` 及黑启动励磁比例基准。

- `Pcs.MaxPower`（double，kW）
  - **作用**：有功/无功指令限幅上限；并网/黑启动功率裁剪到 \([-MaxPower, +MaxPower]\)。

- `Pcs.Efficiency`（double，0~1）
  - **作用**：交流侧与直流侧功率换算效率。

- `Pcs.DcVoltageRangeMin` / `DcVoltageRangeMax`（double，V）
  - **作用**：直流侧电压允许范围；越限触发保护路径。

- `Pcs.AcVoltageNominal`（double，V）/ `FrequencyNominal`（double，Hz）
  - **作用**：交流额定线电压与频率基准。

- `Pcs.MaxCurrent`（double，A）
  - **作用**：最大交流电流门槛。

- `Pcs.GridLossCoefficient`（double，0~1）
  - **作用**：并网侧线损/压降简化系数；影响网侧功率折算与电压等效处理。

**离网 / 黑启动**（未写入 JSON 时使用代码默认值）：

| 字段 | 默认 | 说明 |
|------|------|------|
| `IslandVoltageRampDurationMs` | 100 | 离网 V/f 电压过渡最长仿真时间（ms） |
| `BlackStartActivePowerGainKwPerVolt` | 2.174 | 孤岛电压偏差每 1V 对应有功调节（kW） |
| `BlackStartMaxActivePowerKw` | 200 | 黑启动自动有功上限（kW） |
| `BlackStartMagnetizingPowerFraction` | 0.02 | 建压励磁有功占额定功率比例 |
| `BlackStartBusEnergizedFraction` | 0.85 | 判定 690V 母线已带电的电压比例 |
| `BlackStartPrechargeDelayMs` | 300 | 黑启动准备阶段（ms） |
| `BlackStartVoltageRampVs` | 120 | 软启动电压爬坡（V/s） |
| `BlackStartFrequencyStartHz` | 47 | 软启动起始频率（Hz） |
| `BlackStartFrequencyRampHzPerSec` | 12 | 频率爬升最大速率（Hz/s） |
| `BlackStartReactiveVoltageGainKvarPerV` | 4.0 | 建压期无功电压支撑（kvar/V） |
| `BlackStartCurrentLimitFraction` | 0.45 | 建压期电流限幅（相对 MaxCurrent） |
| `BlackStartSteadyLossShareMode` | `"AllOnBus"` | 稳态站用电分担：`AllOnBus` 或 `LeaderOnly` |

---

## Pcc（220kV 并网点）

绑定到 `PccConfig`。无功—电压闭环与并网电表电压均取 220kV PCC；35kV 母线由额定变比推导。

- `Pcc.NominalLineVoltage`（V，默认 220000）：PCC 额定线电压
- `Pcc.ShortCircuitMva`（MVA，默认 750）：等效短路容量，决定 Q 对电压灵敏度
- `Pcc.MaxVoltageShiftPercent`（±%，默认 5）：电压偏移限幅
- `Pcc.ReactiveVoltageInfluenceCoefficient`（默认 1.0）：影响系数
- `Pcc.StationBusNominalLineVoltage`（V，默认 35000）：站内 35kV 母线额定

---

## Meter（并网电表）

绑定到 `MeterConfig`，描述 PCC 侧电表 PT/CT 与上报量侧。

### Meter.PccMeter

- `MountDescription`（string）：安装位置说明
- `ReportedQuantity`（`Primary` / `Secondary`）：对外上报一次侧还是二次侧量
- `BurdenVa`（double）：仪表负担（VA）
- `AccuracyClass`（string）：精度等级（如 `0.2S`）
- `Pt.PrimaryLineVoltageV` / `Pt.SecondaryLineVoltageV`（V）：PT 一次/二次线电压
- `Pt.Connection`（`Star` 等）：三相接线方式
- `Ct.PrimaryCurrentA` / `Ct.SecondaryCurrentA`（A）：CT 一次/二次电流

PT/CT 变比由一次/二次值自动计算，用于电表读数换算。

---

## Transformer（主变 220kV/35kV）

绑定到 `TransformerConfig`，创建主变 `TransformerDevice`，与 `ElectricalNetwork.MainTransformer` 共用。

- `Transformer.RatedPower`（kVA）：额定容量，用于负载率与无功电压反馈
- `Transformer.PrimaryVoltage` / `SecondaryVoltage`（V）：一次/二次额定线电压
- `Transformer.NoLoadLoss` / `LoadLoss`（W）：空载/负载损耗
- `Transformer.ImpedancePercent`（%）：短路阻抗
- `Transformer.ReactiveVoltageInfluenceCoefficient`：无功对电压影响系数
- `Transformer.NoLoadCurrentPercent`（%）：空载电流百分比
- `Transformer.MagnetizingInrushEnabled`（bool）：是否叠加励磁涌流
- `Transformer.MagnetizingInrushDvDtThresholdPuPerSec`（1/s）：涌流触发电压上升率阈值
- `Transformer.MagnetizingInrushPeakExtraMultipleOfRatedPrimary` / `MagnetizingInrushMaxExtraMultipleOfRatedPrimary`：涌流峰值/上限（相对一次额定电流倍数）
- `Transformer.MagnetizingInrushDecayTimeConstantSec`（s）：涌流衰减时间常数

---

## UnitTransformer（单元变 35kV/690V）

绑定到 `UnitTransformerConfig`，为每个储能单元创建 `TransformerDevice`，与 `ElectricalNetwork.UnitTransformers[u]` 共用。字段含义与 `Transformer` 节相同，含全部 `MagnetizingInrush*` 字段。

- `UnitTransformer.RatedPower`（kVA）
- `UnitTransformer.PrimaryVoltage`（通常 35000）/ `SecondaryVoltage`（通常 690）
- `UnitTransformer.NoLoadLoss` / `LoadLoss` / `ImpedancePercent`
- `UnitTransformer.ReactiveVoltageInfluenceCoefficient` / `NoLoadCurrentPercent`

---

## Load（负载计划）

绑定到 `LoadConfig`。

- `Load.ActivePowerPlan`（double，kW）
  - **约定**：负值 = 用电/从电网取电；正值 = 发电/向外送电。

- `Load.ReactivePowerPlan`（double，kvar）
  - **作用**：负载无功计划。

---

## 已废弃字段（勿再使用）

以下字段在旧版文档或历史配置中出现过，**当前代码已不支持**：

| 旧字段 | 替代 |
|--------|------|
| `Simulator.Runtime.SimStepMs` | `PropagationIntervalMs` |
| `Simulator.Runtime.Speedup` | `IntegrationStepMultiplier` |
| `Simulator.Devices`（JSON 顶层） | `EssUnits` |

---

## 相关文件

- 配置类：`Configuration/SimulatorConfig.cs`、`EditionConfig.cs`、`WebConfig.cs`、`LicenseConfig.cs`
- 绑定逻辑：`Program.cs`（`EssUnits` → `SimulatorConfig.Devices`；组态 overlay 覆盖 `EssUnits` / `PvUnits` 等）
- 档位模板：`configs/社区版.appsettings.json`、`商业版`、`定制版`、`演示版`（旧名充值版仍可存在，发布映射为商业版）
- Modbus IP 白名单：`configs/modbus-allowlist.json`
