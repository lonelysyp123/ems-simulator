# 仿真模拟器 — 构建、运行与联调

## 开发环境

- .NET SDK 8.0+
- 仓库根目录即项目根

```bash
dotnet build
dotnet run --project EssSimulator.csproj
dotnet test EssSimulator.Tests/EssSimulator.Tests.csproj
```

## 配置文件

| 文件 | 作用 |
|------|------|
| `appsettings.json` | 单元拓扑、`Devices[]`、端口、PCC/变压器/负载、DataExchange 周期 |
| `bms_bank.csv` | BMS 堆 Modbus 点（源在 `pointmaps/{版本}/`，`sync-pointmaps-to-root.sh` 同步） |
| `bms_rack.csv` | BMS 簇 Modbus 点 |
| `emu.csv` | PCS/EMU 点 |
| `em.csv` | 站用/PCC 电表点 |
| `log4net.config` | 日志 |

修改拓扑后重启进程；Modbus 端口冲突时需改 `Simulator.Protocol`。

## 启动流程

`Program.cs` → 加载配置（可选组态 overlay）→ 创建 `EnergyStorageSystem` → 启动 BMS/PCS/EM 数据服务与 `ModbusHostedService` → **Kestrel Web**（默认 5050）。

- 操作界面为浏览器，不是控制台菜单
- `Simulator.Runtime.NoGui` 仅兼容旧配置，不再关闭 Web
- 开发一键：`./scripts/dev-up.sh`

## 控制台命令（运行时）

`help`、`esscmd`、`breaker`、`dpc` 等，详见 `docs/指令详细说明.md`、`docs/用户手册.md`。

## Modbus 联调

1. 确认 `appsettings.json` 中端口与防火墙
2. 确认使用的点表文件（尤其 `bms_bank.csv` 版本）
3. 生成 mbpoll → **ess-mbpoll** skill（按 CSV `FunctionCode` 选 `-t`）
4. BMS rack 点：从站 ID 通常 `1 + rackIndex`（见 `ModbusTCPSlave`）

## 发布

**发布目录**（见 `docs/项目编译说明.md`）：

| 版本 | 配置源 |
|------|--------|
| 社区版 | `configs/社区版.appsettings.json`（2 单元，高级 UI 关） |
| 商业版 | `configs/商业版.appsettings.json`（需 license） |
| 定制版 | `configs/定制版.appsettings.json` |
| 演示版 | `configs/演示版.appsettings.json`（`publish-demo.sh`） |

```bash
# 商业多版本
EDITION=社区版 ./scripts/commercial/publish-windows.sh
EDITION=商业版 ./scripts/commercial/publish-linux.sh
./scripts/commercial/publish-all.sh
./scripts/commercial/publish-demo.sh
./scripts/commercial/sync-runtime.sh

# 开发联调（单配置）
./scripts/publish-windows.sh
./scripts/publish-linux.sh
```

发布包需含：`EssSimulator`、`appsettings.json`、`*.csv`、`log4net.config`、`start.sh` / `start.bat`。

## 联调排错清单

- [ ] 仿真进程已启动且 `serverListenInfo` 显示端口
- [ ] mbpoll/主站 IP、端口、Slave ID 正确
- [ ] 点表 `Address` + mbpoll `-0` 与 CSV 一致
- [ ] 写控制点：确认 Semantics（Pulse/Hold/Edge）与 `Scale`
- [ ] SOC/功率：BMS 是否已 link、PCS 是否运行、断路器是否合闸

## 文档

见 `docs/README.md`：

- 用户手册：`docs/用户手册.md`
- B/S 架构：`docs/B-S架构说明.md`
- 系统设计：`docs/系统设计说明.md`
- 编译发布：`docs/项目编译说明.md`
- 指令说明：`docs/指令详细说明.md`
- 产品分档：`docs/产品分档与交付边界.md`
