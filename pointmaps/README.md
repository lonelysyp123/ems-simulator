# 点位表（Point Maps）

运行时按逻辑文件名（`emu.csv`、`em.csv`、`bms_bank.csv`、`bms_rack.csv`、`lc.csv`、
`pv_logger.csv`、`pv_apm810.csv`）解析，**只读取** `pointmaps/models/{类型}/{型号}/`。
仓库根目录同名 CSV 不是源文件，开发也不再执行 `sync-pointmaps-to-root.sh`。

解析优先级（见 `Protocol/Modbus/PointMapPathResolver.cs`）：

1. **设备型号选型**（本目录 `models/`，运行期可在 Web「系统配置」页切换）
2. `pointmaps/models/{类型}/standard/`（未选型时的兜底）

## 设备型号（models/）

按 **设备类型 → 设备型号 → 点表文件** 组织，运行期在系统配置界面选型，
持久化到 `configs/topology/device-models.json`，重启后生效：

LC 默认由 `models/lc/` 下互补片段按组拼装（`system` / `group` / `bms` / `mv` /
`unit_5.5MW` / `unit_10MW`），不是按支路数互斥选型。两段 unit 窗口**始终共存**：
`n` 是 EMU 内 PCS 组号；`unit_10MW` 展开 n=1,2（基址 2600）；`unit_5.5MW` 只展开 n=1
（基址 5000，只覆盖第一个 PCS 组）。模块 1/2 是该组第一台 PCS 的两条支路。
组态画布 `pcs` 节点是 PCS 变流器支路（两条支路组成一台 PCS）。
EMU 始终使用单元直控点表（`emu/standard`）。

```
pointmaps/models/
  bms/          type.json + standard/ g2_pro/ g2_4mwh/（bms_bank.csv + bms_rack.csv）
  emu/          type.json + standard/（emu.csv，单元 PCS 直控）
                + iec61850/（拼装片段：pcs.icd / ems_goose.icd / mapping.csv，不进选型）
  em/           type.json + standard/（em.csv）
  lc/           type.json + 拼装片段（system / group / bms / mv / unit_5.5MW / unit_10MW）+ 互斥 emu/
  pv/           type.json + standard/（pv_logger.csv + pv_apm810.csv）
```

- `type.json`：`{ id, name, description, files }`，`files` 声明该设备类型使用的点表文件名。
- `model.json`：`{ id, name, description }`，型号展示名与说明。
- 新增型号：在对应设备类型目录下新建子目录，放入点表文件与 `model.json` 即可被自动识别，无需改代码。

### EMU IEC 61850 片段（`emu/iec61850/`）

拼装型片段（`role=fragment`），不进入设备选型；运行时由 `Protocol/Iec61850` 加载：

| 文件 | 用途 |
|------|------|
| `pcs.icd` | 仿真器 MMS IED 模型（设备侧，无 GSE 发布） |
| `ems_goose.icd` | **外部** GOOSE 发布端 ICD（IEDScout 等导入后发布） |
| `mapping.csv` | ParamName ↔ ObjectRef；`Transport=goose` 行为入向遥控点 |

AppID：PCS N = `0x2000+N`（PCS1=`0x2001`）。联调见 [用户手册 §5.1](../docs/用户手册.md#51-iec-61850mms--入向-goose)。
## LC 中压系统级点位语义（trina_5.5MW / trina_10MW 的 lc.csv SYSTEM 段）

中压 LC 挂在 `simLc*`，系统级点位绑定到所属机组 `emuN.Emu.*` 后由均分派发/批量语义生效：

| 点位 | 绑定字段 | 语义 |
|------|----------|------|
| `syst4` | `Emu.RemoteControlEnable` | 远程&本地控制使能：0 禁止 / 1 使能 |
| `syst5` | `Emu.RemoteControlMode` | 控制模式：0 本地 / 1 远程（syst4=1 且 syst5=1 时均分生效） |
| `syst6` | `Emu.SystemOperation` | 系统操作（边沿生效）：3 启动 / 4 停止 / 5 待机（目标清零）/ 6 重置（停机清故障锁存） |
| `syst7` | `Emu.BlackStartModeWrite` | 黑启动模式批量写入：0 关闭 / 1 开启（下发所属全部 PCS） |
| `syst1010` | `Emu.TargetActivePower` | EMU 级目标有功（kW，正放负充）；远程使能时按台数均分 |
| `syst1011` | `Emu.TargetReactivePower` | EMU 级目标无功（kvar）；远程使能时按台数均分 |
| `sysyc200~203` | `Emu.TotalPcsCount` 等统计字段 | PCS 总数/运行/告警/故障台数（`MapEmuState` 同步刷新） |

- 单元 EMU（`emu/standard` 的 yt/yx 系列，绑定 `emuN.PcsList[i].*`）在本地模式下照旧生效；
  远程均分生效时以均分值覆盖功率设定。
- 默认拼装 LC 含 ModelSim，由 DataExchange 采集进本机 LC Modbus；
  选型互斥 `lc/emu` 时走 EMU 直控整表。

## EMU 设备树路径语法（分层构成）

EMU 支持「EMU → group → PCS 支路」分层构成（组态编辑器中用「EMU 分组」模板建模）。
EMU 不参与电气求解，分层仅为协议聚合视图：组内 PCS 与扁平视图共享同一仿真实例，
两条路径读写同一对象。点表 ModelSim 绑定支持以下路径（`N` 从 1 计，`g`/`i`/`k` 从 0 计）：

| 路径 | 目标 | 门控条件 |
|------|------|----------|
| `emuN.PcsList[i].*` | 扁平视图（保持） | i < 单元 PCS 总数（分组时为各组之和） |
| `emuN.Groups[g].PcsList[i].*` | 组内 PCS（与扁平路径同启停/功率控制语义） | g < 分组数且 i < 组内 PCS 数 |
| `emuN.Groups[g].Breaker.*` | 组断路器协议镜像（恒合闸，纯遥测） | g < 分组数且该组绑定断路器 |
| `emuN.Groups[g].Total*` 等聚合字段 | 组聚合遥测（组内 PCS 求和/台数统计） | g < 分组数 |
| `emuN.Transformers[k].*` | 单元变镜像（负载率/功率/油温，纯遥测） | k=0（本期仅 1 台） |
| `emuN.Breaker.*` | EMU 级断路器镜像（开合跟随单元投退） | 机组绑定单元断路器 |

- 无分组的机组：`Groups[*]` 路径在目录编译期自动剔除（未绑定点位维持默认值），
  既有扁平路径不受影响。
- 组级断路器/电表/变压器镜像不产生电气动作，值为合成（断路器恒合闸、
  变压器优先抄电气层单元变真实值，缺失时按组内 PCS 求和兜底）。
- 控制派发范围不变：EMU 级目标仍按全机组 PCS 均分；组级目标派发为后续扩展。

## 版本目录（legacy 兜底）

| 目录 | 说明 | 主要差异 |
|------|------|----------|
| [common](./common/) | 默认联调 / **商业发布** | develop 命名 `bms_bank`；精简 `bms_rack` |
| [lc](./lc/) | LocalControl | param 命名 `bms_bank` + `lc.csv` |
| [battery](./battery/) | 电芯级遥测 | 全量 `bms_rack`（Pack/电芯数据点） |

各目录含 `version.json`（`id` 与目录名一致）。

## 本地联调

点表源文件即 `pointmaps/models/`，`dotnet run` / `./scripts/dev-up.sh` 直接解析型号目录。
`./scripts/sync-pointmaps-to-root.sh` 仅校验 models 完整性，不再把 CSV 复制到仓库根。

## 发布（开发脚本）

```bash
./scripts/publish-linux.sh battery
./scripts/publish-linux.sh lc
./scripts/publish-windows.sh common
```

或环境变量：`POINTMAP_VERSION=lc ./scripts/publish-linux.sh`

**商业发布**（`scripts/commercial/`）固定 `common`，不可覆盖。

可选值：`common`、`lc`、`battery`（与目录名一致）。
发布脚本会同时将 `pointmaps/models/` 整目录复制到输出目录，供运行期型号选型使用。
