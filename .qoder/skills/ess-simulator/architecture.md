# 仿真模拟器 — 架构说明

## 分层

| 层 | 目录 | 职责 |
|----|------|------|
| 协议层 | `Protocol/` | Modbus TCP 从站、`ModbusHostedService` 启动多路服务 |
| 数据交换 | `DataExchange/` | 点目录编译、遥测/控制/反馈管道、`ControlEffect` |
| 映射层 | `EssSimModelApi/Mappers/` | 物理状态 → BMS/EMU/EM DTO（`BmsMapper`、`PcsMapper`） |
| 编排层 | `EssDeviceSimModel/EnergyStorageSys.cs` | Host 时钟；持有拓扑与设备实例 |
| 电站引擎门面 | `EssDeviceSimModel/PlantEngine.cs` | **唯一物理步进入口** `Step(dt)`：电气 → 热网络 → **耦合图** |
| 耦合图 | `EssDeviceSimModel/Plant/` | `PlantCouplingGraph` / `PcsBmsDcCouplingLink`（PCS↔BMS 直流边）；设备实现 `IElectricalLossSource` / `ITemperatureAware` |
| 热网络 | `EssDeviceSimModel/Thermal/` | `ClimateModel`、`ThermalNetwork`、`BmsCabinetThermalZone`、`PlantThermalSystem` |
| 求解层 | `EssDeviceSimModel/Solver/`、`Propagation/` | `ElectricalNetwork`、`NetworkSolver`、径向潮流 |
| 设备层 | `EssDeviceSimModel/Devices/` | PCS、变压器、断路器、电网、负载、BMS Rack |
| 电池细模型 | `EssDeviceSimModel/Battery/` | 电芯/簇/堆积分，经 `BmsRackDevice` 接入 |

## 运行模型（相对老版本）

- **老方式**：多线程各自轮询（PCS 爬坡、Modbus 同步等分散）。
- **现方式**：
  - `.NET Host` 注册多个 `IHostedService` / `BackgroundService`
  - **单一仿真时钟**：`EnergyStorageSystem.ExecuteAsync` + `PeriodicTimer`（`PropagationIntervalMs`）
  - 每 tick：`PlantEngine.Step` → 电气 → **热（外温+柜体）** → PCS/BMS（环境温=柜内空气）→ 同步；Mapper 另周期刷新 DTO
  - Modbus 侧独立周期读 DTO 写寄存器（`DataExchange` 的 `TelemetryIntervalMs` 等）

## 主循环一步（简化）

Host 只做：`AdvanceCycleClock` → `PlantEngine.Step`。引擎内顺序：

1. `RadialPowerSweepEngine.SolveCycle` 或 `NetworkStepOrchestrator.SolverPrimaryStep`
2. `PlantThermalSystem.Step`：日变化外温 + 各 BMS 柜体热网络（损耗来自上一步）
3. `PlantCouplingGraph.StepCouplings`：每条 `PcsBmsDcCouplingLink` 施加环境温 → 交换 V/I → `IElectricalLossSource` 登记热注入
4. `SyncUnitTransformerAfterPcsUpdate`：离网/黑启动单元变与站用电
5. `RefreshAllUnitBlackStartBusContexts`
6. `NetworkControlBridge.SyncBmsLinksFromRacks`

热配置：`Simulator.Runtime.Thermal`（`Enabled`、`Climate`、`Cabinet`、`ProbeBiases`、`Feedback`）。
多点探针经 `BmsThermalProbeMapper` 写入 `AirConditioners` / `LiquidCoolingSystems` / `TempHumiditySensors`，点表 yc56/57、yc69–71、yc93/102/111。
阶段 5：空调回差闭环（`HvacEnabled`/`HvacCoolingPowerW`）；`TemperatureDerating` 降额 PCS 实发功率与堆 MaxCharge/Discharge；`ThermalAgingContext` Arrhenius 日历老化。

## 数据交换三管道

| 管道 | 方向 | 说明 |
|------|------|------|
| Telemetry | 仿真 → Modbus | 如 `bms1.BatteryStacks[0].SOC` → `yc11` |
| Control | Modbus → 仿真 | EMS 写寄存器 → `ControlEffect`（启停、并网、断路器） |
| Feedback | 仿真 → Modbus | Hold 类控制点状态回写 |

绑定路径：`CSV ModelSim` → `ModbusPointMap` 替换 `bmsdeviceId`/`emuDeviceId` → `PointCatalogLoader` → `ObjectPathResolver` / `SimServer.GetExtIfVariableVal`。

部分 DTO 字段为**计算属性**（如 `ChargeDischargeStatus`）或由 **Mapper 从物理量写入**（如 `SOC` ← `RackState.MinClusterSOC`）。

## 电气拓扑（配置驱动）

电压等级示例（见 `Pcc`、`StationBus` 配置）：

- PCC：如 220kV 线电压
- 站用/集电：如 35kV
- 单元 AC 母线：690V 级

断路器、主变、单元变、负载、PCS、BMS DC 端口在 `ElectricalTopologyFactory` / `NetworkTopologyBuilder` 中连成一张网；**设备实例唯一**，ESS 与 Network 共用同一 `PcsDevice` 等对象。

## 设计原则

- **配置即拓扑**：单元数、变压器参数、负载计划 → `appsettings.json`
- **点表即协议**：外部可见寄存器仅由 CSV 定义
- **控制面 SSOT**：断路器、BMS 链路等经 `NetworkControlBridge`

## 仿真边界（回答用户时宜说明）

- 面向 **EMS 联调与逻辑验证**，非电芯级标定或保护证书级仿真
- 电池为 aggregated 模型；保护/告警有简化规则
- Modbus 为 **TCP 仿真从站**，非现场真实 BMS/PCS 协议栈复刻
