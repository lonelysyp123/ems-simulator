---
name: ess-simulator
description: >-
  Describes EssSimulator (对外名称「仿真模拟器」): large-scale energy-storage
  simulation for EMS integration testing, architecture, config, Modbus point
  maps, build/run workflow. Apply when the user discusses EssSimulator, 储能仿真,
  仿真模拟器, EMS/BMS/PCS 联调, 大储测试, or this repository.
disable-model-invocation: false
---

# 仿真模拟器（EssSimulator）

## 项目定位

| 项 | 说明 |
|----|------|
| **对外名称** | 仿真模拟器 |
| **行业** | 储能行业，**大储项目**测试联调 |
| **用户** | EMS 开发与测试人员 |
| **与真机** | **软件仿真**：模拟真实物理环境与设备行为，对外 Modbus 反馈与联调预期一致；非硬件在环孪生 |
| **拓扑** | 以 `appsettings.json` 为准，**随项目变化**；回答端口/单元数/电压等级前必须先读当前配置 |

## 回答规范（必须遵守）

1. **默认简体中文**；技术标识（路径、寄存器、类名）保留英文。
2. **先 actionable、后原理**：联调/测试类问题优先给端口、点表、命令；架构细节放后或链到 reference。
3. **Modbus 读写命令** → 使用 [.qoder/skills/ess-mbpoll/SKILL.md](../ess-mbpoll/SKILL.md)，禁止默认 `-t 4`。
4. **每答一问，先想「还缺什么信息」**：若拓扑、点表版本、单元号、现象（读值/写值/日志）未明确，**先简短列出需要用户补充的项**，再给有条件的答案；不要假设与当前 `appsettings.json` / CSV 不一致的配置。
5. **配置即真相**：单元数、端口、PCC/站用电压、`Devices[]` 以仓库内 **`appsettings.json`** 和 **`*.csv`** 为准，勿凭记忆写死数字。
6. **点表切换**：源文件在 `pointmaps/{common|lc|battery}/`；本地 `./scripts/sync-pointmaps-to-root.sh [版本]`；开发发布 `./scripts/publish-linux.sh lc`；商业发布固定 `common`。

### 常见需向用户确认的信息

| 场景 | 建议先问 |
|------|----------|
| Modbus 读不到/写无效 | 主机 IP、端口、单元号、点名/地址、用的哪份 CSV、FunctionCode |
| 功率/SOC/电压不对 | 哪一路 BMS/EMU、期望物理量、寄存器原始值、是否已并网/合闸 |
| 改拓扑或加单元 | 目标单元数、是否改 `Devices[]`、端口是否冲突 |
| 发布/部署 | 目标 OS/架构、是否需要 GUI、配置是否与联调环境一致 |
| 行为是否符合真机 | 具体工况（并网/离网/黑启动）、真机侧预期 vs 仿真读数 |

## 30 秒架构

```
EMS/测试工具 ──Modbus TCP──► ModbusSimServer + DataExchangeSession
                                    │ 点表 CSV (ModelSim → 对象路径)
                                    ▼
                         EssSimModelApi (BmsMapper / PcsMapper / EmMapper)
                                    │
                                    ▼
              EnergyStorageSystem 时钟 (PeriodicTimer)
                                    │ PlantEngine.Step
                                    ▼
              电气潮流 → PlantThermalSystem → PlantCouplingGraph（PCS↔BMS 边）
                                    │
              PcsDevice · TransformerDevice · Breaker · BmsRackDevice · …
```

- **主循环**：Host 托管 + 固定步长；每步 **`PlantEngine.Step`**（电气 → 热 → PCS/BMS；Mapper 在独立服务）。
- **热**：`Simulator.Runtime.Thermal`；柜体空调闭环；高温降额（PCS/堆限功率）；温度加速日历老化；多点探针 → 点表。
- **对外接口**：BMS `simBms{N}`、EMU `simEmu{N}`、电表 `simEm`、光伏 Logger `simPv{N}` / 电表 `simPvMeter{N}`；点位由 CSV + `Scale` 映射。
- **控制语义**：DataExchange 管道（遥测 / 控制 / 反馈）；PCS 启停 Hold、BMS 并网 Pulse 等见 `PointCatalogLoader`。

## 关键文件速查

| 用途 | 路径 |
|------|------|
| 运行配置 | `appsettings.json` |
| BMS 堆点表 | `bms_bank.csv`（源：`pointmaps/{版本}/`） |
| BMS 簇点表 | `bms_rack.csv` |
| PCS/EMU 点表 | `emu.csv` |
| 电表点表 | `em.csv` |
| 点位表目录 | `pointmaps/`（common / lc / battery） |
| 主循环 / 电站引擎 | `EssDeviceSimModel/EnergyStorageSys.cs`、`PlantEngine.cs` |
| 数据交换 | `DataExchange/` |
| Modbus 服务 | `Protocol/ModbusHostedService.cs` |
| 文档索引 | `docs/README.md` |
| 用户手册 | `docs/用户手册.md` |
| B/S 架构 | `docs/B-S架构说明.md` |
| 系统设计 | `docs/系统设计说明.md` |
| 编译发布 | `docs/项目编译说明.md` |
| 指令说明 | `docs/指令详细说明.md` |
| 发布（商业） | `scripts/commercial/publish-all.sh`、`publish-demo.sh` → `dist/{社区版,商业版,定制版,演示版}/` |
| 发布（开发） | `scripts/publish-windows.sh` / `publish-linux.sh` → `dist/win-x64`、`dist/linux-arm64` |

## 默认 Modbus 端口（以配置为准）

读 `Simulator.Protocol`：

- EM：`EmModbusPort`（示例 1500）
- BMS 路 N：`BaseBmsModbusPort + (N-1) * BmsPortStep`（示例 1501 起）
- EMU 单元 N：`BaseEmuModbusPort + (N-1) * EmuPortStep`（示例 1601 起）
- 光伏 Logger N：`BasePvLoggerModbusPort + (N-1) * PvLoggerPortStep`（示例 1801 起）
- 光伏低压电表 N：`BasePvMeterModbusPort + (N-1) * PvMeterPortStep`（示例 1901 起）

## 单元与对象对应（固定规则）

每个 `Devices[]` 项 = **1 个储能单元 = 2 路 PCS + 2 路 BMS**：

- 单元 U 的 EMU：`emuU`，Modbus `simEmuU`
- 该单元 PCS：`emuU.PcsList[0..1]` ↔ `ess._pcsList[...]`
- BMS 通道按全局序号 `bms1…bmsN`，每路独立 `simBms{i}`

每个组态 `pv_unit` = **1 个光伏单元**（箱变 + 逆变器组）：

- 对象 `pvN`（`PvUnitDevice`），点表路径 `pvN.Logger.*` / `pvN.MeterLv.*`
- Logger Modbus `simPvN`（`pv_logger.csv`），低压电表 `simPvMeterN`（`pv_apm810.csv`）
- 纯光伏工程不伪造储能单元；有功只向电网送电，挂在 35 kV 母线
- 方阵出力由**温度 + 光照入射角**决定（90° 正对 1000 W/㎡，0°/180° 为 0），不再跟仿真时刻正弦变化
- 温度：升温按组件负温度系数降额；**-10℃ 以下**逆变器开始线性降额，**-40℃ 可发为 0**（直流过压闭锁同向）
- 联调设定：`esscmd setpvN array A|B temperature <℃>` / `esscmd setpvN array A|B angle <度>`；主接线方阵框内可改

## 开发与验证

```bash
dotnet build
dotnet test EssSimulator.Tests/EssSimulator.Tests.csproj
```

- 点表绑定/SOC 等：`EssSimulator.Tests/DataExchange/BmsTelemetryBindingTests.cs`
- 改模型/ Solver / 管道后应跑相关测试

## 组态编辑要点

- 连线：点任一相口即可成组连接（AC 同侧 A/B/C，DC 同侧正/负）；`POST /api/topology/connect` 默认 `expandBundle: true`。
- 标准径向骨架：`POST /api/topology/scaffold`（EMU 1–20）；编辑页「标准拓扑向导」。
- 画布：20px 网格吸附；Ctrl+Z / Ctrl+Shift+Z 撤销重做；保存校验失败返回 `problemNodeIds` 供高亮。

## 电气主接线绘制规则

组态驱动的单线图（`topologyMainLineLayout.js` + `TopologyMainLineSvg.vue`）。**按当前工程的节点与连线递归推导**，不要为某一份组态写死「几台主变、几段母线」。

1. **图即真相**：从电网（或最高电压母线）沿连线走；变压器按一次/二次电压接到上下游母线；电表、负载、EMU、光伏单元挂在实际连接的母线上。没有的设备不画，也不要补虚拟主变。
2. **禁止贴连**：设备与母线、设备与设备之间必须有可见黑线引线段（`LINK_STUB`，默认 18px），不得直接邻接；连线只用横竖段。
3. **单挂省略母线**：某段母线下方只挂 1 台设备时省略该母线，上下用黑线直连（含仅 1 路 PCS 时省略 690 母线、仅 1 路 BMS 时省略直流母线）。
4. **级联与并列**：同一母线下的多台变压器按列展开；220/110/35 这类级联按电压层次向下堆叠，不要压成一层。
5. **单元内部**才按设备类型展开（不是按工程定制）：EMU 为单元断 — 单元变 — [690 母线?] — PCS（DC 并联母线同理）；光伏为单元断 — **箱变模块** — 光伏方阵A/B（两路方阵保留，不再拆 A/B 逆变器组）。
6. **EMU 虚线框**仅为遮罩（无说明文字），不参与电气坐标与连通，灰色只罩到 PCS。**光伏单元不画外框虚线**；箱变框体内显示实时有功/无功，并提供逆变器启停与有功/无功设定；**并网数量只计逆变器台数**（组态 `inverterCount`）。**光伏方阵 A/B** 标题按组态显示 `每串块数×单机簇数×本路逆变器台数`；紧凑显示有功、可发、直流电压/电流、辐照，并提供温度、光照入射角设定。

## 详细资料

- 架构与数据流：[architecture.md](architecture.md)
- 构建、运行、发布、联调：[operations.md](operations.md)
- 术语：[glossary.md](glossary.md)
