---
name: ess-mbpoll
description: >-
  Generates mbpoll Modbus TCP commands for EssSimulator from point-map CSV
  (emu.csv, em.csv, bms_bank.csv, bms_rack.csv) and appsettings.json. Always
  apply when the user mentions mbpoll, Modbus read/write, or simulator control.
  Selects mbpoll -t from each point's FunctionCode column (never assume -t 4).
disable-model-invocation: false
---

# EssSimulator mbpoll 指令

## 核心规则

**禁止**默认 `-t 4`。每次生成命令前必须：

1. 在对应 CSV 查到该点的 `FunctionCode`、`Address`、`Type`、`Size`、`Scale`
2. 按下表将 `FunctionCode` 映射为 mbpoll 的 `-t`
3. 再套入统一命令骨架

点表 `FunctionCode` 列 = 本模拟器对该点使用的 Modbus 功能码（与 `ModbusSlave` 一致）。

## FunctionCode → mbpoll `-t`

| 点表 FC | Modbus 含义 | 模拟器数据区 | mbpoll `-t`（读） | mbpoll `-t`（写） |
|--------|-------------|-------------|-------------------|-------------------|
| **1** | 读/写线圈 | CoilDiscretes | `-t 0` | `-t 0`，值 `0`/`1` |
| **2** | 离散输入 | — | `-t 1` | 不支持写 |
| **3** | 保持寄存器 | HoldingRegisters | `-t 4` | `-t 4` |
| **4** | 输入寄存器 | InputRegisters | `-t 3` | **只读**，勿生成写命令 |
| **5** | 写单线圈 | CoilDiscretes | `-t 0` | `-t 0`，值 `0`/`1` |
| **6** | 写单寄存器 | HoldingRegisters | `-t 4` | `-t 4` |

说明：mbpoll 的 `-t` 表示**数据区类型**，不是点表里的数字本身；上表已完成二者对齐。

### `Size` 与 32 位类型

| Size | Type 含 int32/u32 | 读/写 `-t` 后缀 | 读寄存器个数 `-c` |
|------|-------------------|-----------------|-------------------|
| 16 | — | 默认（如 `-t 4`） | `1` |
| 32 | int32/u32 | FC3/6 用 `-t 4:int`；FC4 用 `-t 3:int` | `2` |

`int16`/`u16` 的 16 位点用默认 `-t`，**不要**加 `:int`。

## 命令骨架

```bash
# 读
mbpoll -0 -t {t} -a {slave} -r {addr} -c {count} -p {port} {host}

# 写（末尾为原始值；线圈写 0/1）
mbpoll -0 -t {t} -a {slave} -r {addr} -p {port} [-1] {host} {raw}
```

| 占位符 | 来源 |
|--------|------|
| `{t}` | 上表，由该点 `FunctionCode` + `Size`/`Type` 决定 |
| `{addr}` | CSV `Address`（配合 `-0`，与点表一致） |
| `{count}` | 读：`Size/16`，32 位为 `2`，线圈为 `1` |
| `{port}` | 见下方端口 |
| `{raw}` | 写：`物理量 × Scale`（线圈为 `0`/`1`） |

固定默认：`127.0.0.1`、`-a 1`、**必须** `-0`。用户只要写一次时加 `-1`。

## 端口

读 `appsettings.json` → `Simulator.Protocol`：

| 设备 | 点表 | 端口 |
|------|------|------|
| EM | em.csv | `EmModbusPort`（默认 1500） |
| BMS Unit-N | bms_bank.csv + bms_rack.csv | `BaseBmsModbusPort + (N-1) * BmsPortStep`（默认 1501+N-1） |
| EMU Unit-N | emu.csv | `BaseEmuModbusPort + (N-1) * EmuPortStep`（默认 1601+N-1） |

BMS rack 点：从站 `slaveId = 主站 + rackIndex`（通常主站 1，第 k 个 rack 为 `-a (1+k)`，以 `ModbusTCPSlave` 为准）。

## 生成步骤（ checklist ）

1. 确定设备（EM / BMS / EMU）与单元号 → 点表文件 + 端口
2. CSV 查点 → 记录 `FunctionCode, Address, Type, Size, Scale`
3. 查表得 `-t`；FC4 且用户要「写」→ 说明只读，改点或改 FC
4. 计算 `{raw}` 或读回后 ÷ `Scale`
5. 输出命令，并注释：点表 FC、mbpoll `-t`、物理量

## 示例（均来自点表查表）

**FC=6**，PCS1 有功 100 kW（`param55`，5300，Scale 10）→ `-t 4`：

```bash
mbpoll -0 -t 4 -a 1 -r 5300 -p 1601 -1 127.0.0.1 1000
```

**FC=5**，PCS1 启停（`pcs1_startstop`，5303）→ `-t 0`：

```bash
mbpoll -0 -t 0 -a 1 -r 5303 -p 1601 -1 127.0.0.1 1
```

**FC=4**，读 EM 总有功（`yc12`，地址 24，Scale 1000，int32）→ `-t 3:int`，`-c 2`：

```bash
mbpoll -0 -t 3:int -a 1 -r 24 -c 2 -p 1500 127.0.0.1
```

## 禁止

- 未查 `FunctionCode` 就写死 `-t 4` 或 `-t 0`
- FC4 点生成写命令
- FC5/1 点用 `-t 4` 写寄存器
- FC6/3 点用 `-t 3` 写（`-t 3` 是输入寄存器区）
- 省略 `-0`

## 速查表

EMU/BMS 常用控制点见 [points.md](points.md)（含每点 FC）。
