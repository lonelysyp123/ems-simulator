# 仿真模拟器 — 术语

| 术语 | 含义 |
|------|------|
| **仿真模拟器** | 本项目对外名称（仓库名 EssSimulator） |
| **大储** | 大型储能电站场景；本项目主要服务此类 EMS 联调 |
| **EMS** | 能量管理系统；主站/上位机，通过 Modbus 读写仿真器 |
| **ESS** | 储能系统整体仿真对象 `EnergyStorageSystem` / `ess` |
| **储能单元** | `appsettings.json` 中 `Devices[]` 一项；含 2 路 PCS + 对应 BMS |
| **EMU** | 能量管理单元 Modbus 从站，承载一个单元内 2 路 PCS 点表 |
| **BMS** | 电池管理系统 Modbus 从站；堆表 + 簇表（`bms_bank` + `bms_rack`） |
| **PCS** | 储能变流器；仿真核心为 `PcsDevice` |
| **PCC** | 公共连接点；站级电表/电网接入点 |
| **点表 / CSV** | Modbus 寄存器与内部 `ModelSim` 路径映射 |
| **ModelSim** | CSV 列，如 `model=4\|arg1=bmsdeviceId.BatteryStacks[0].SOC` |
| **DataExchange** | 点目录驱动的遥测/控制/反馈管道层 |
| **并网 / 黑启动 / 离网** | PCS/BMS 联调常见工况；控制点见 emu.csv / bms_bank.csv |
| **PropagationIntervalMs** | 电气主循环步长（ms） |
| **Scale** | 寄存器值与物理量换算系数（写 Modbus 时 often × Scale） |

对象命名惯例：

- `bms{N}` — 第 N 路 BMS 数据对象（加载点表时 `bmsdeviceId` → `bmsN`）
- `emu{N}` — 第 N 单元 EMU 数据对象
- `simBms{N}` / `simEmu{N}` — Modbus TCP 服务名
