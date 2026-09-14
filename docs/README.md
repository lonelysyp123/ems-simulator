# 仿真模拟器（EssSimulator）文档索引

对外产品名：**仿真模拟器**。工程名：EssSimulator。  
仓库：[github.com/lonelysyp123/simulator](https://github.com/lonelysyp123/simulator)（公开）。远程长期分支：**`master`**（发布）、**`develop`**（集成）。

| 文档 | 读者 | 说明 |
|------|------|------|
| [系统设计说明](./系统设计说明.md) | 开发、架构 | 技术栈、分层、数据流、储能/光伏设备、设计原则 |
| [B/S 架构说明](./B-S架构说明.md) | 开发 | Web 后端、Vue 前端、HTTP/SignalR |
| [用户手册](./用户手册.md) | EMS/BMS/光伏联调、测试 | 安装启动、浏览器界面、组态/3D、Modbus（含 IP 白名单）、IEC 61850 GOOSE、光伏单元、联调流程 |
| [项目编译说明](./项目编译说明.md) | 构建、发布 | 开发/商业脚本、dist、档位差异 |
| [产品分档与交付边界](./产品分档与交付边界.md) | 销售、交付、发布 | 社区版 / 商业版 / 定制版 / 演示版 |
| [授权说明](./授权说明.md) | 交付、运维 | 机器码、`license.txt` 签发与校验 |
| [充值版授权网关约定](./充值版授权网关约定.md) | 运维、接入方 | 仓外计费/鉴权（旧名「充值版」现映射为商业版托管） |
| [测试报告](./测试报告.md) | 测试、QA | 性能与 DataExchange 手动测试记录 |
| [指令详细说明](./指令详细说明.md) | 联调、测试 | esscmd / dpc / breaker / dpctest；含光伏直控 `setpv` |
| [appsettings 字段说明](./appsettings.explained.md) | 开发、集成 | `appsettings.json` 各配置段（含 IEC 61850 / GOOSE、`PvUnits`、Modbus IP 白名单） |
| [点位表版本说明](../pointmaps/README.md) | 联调、发布 | models 选型；含 `emu/iec61850` 与 `pv` |

## 产品与演示

| 文档 | 说明 |
|------|------|
| [产品介绍](./marketing/产品介绍.md) | 对外短文案与能力清单 |
| [演示录屏脚本](./marketing/演示录屏脚本.md) | 演示版录屏分镜 |

## 补充材料

- 随包 `scripts/commercial/editions/{版本}/README.txt`：各档压缩包内的简明说明
- 商业发布脚本说明：[scripts/commercial/README.md](../scripts/commercial/README.md)
- 分支约定：`.qoder/skills/gitflow-branching/SKILL.md`（`feature_*` / `fix_*`，合入后删除）

## 文档约定

- 点表、端口、单元数以仓库根目录 **`appsettings.json`** 与 **`*.csv`** 为准，文档中的数字仅为示例。
- 操作界面为 **浏览器 Web**（默认 `http://127.0.0.1:5050`），不是控制台 TUI。
- Modbus 命令生成规则见仓库技能 `.qoder/skills/ess-mbpoll`（按 CSV 的 FunctionCode 选 `-t`，禁止默认 `-t 4`）。
- 标准产品档位为 **社区版 / 商业版 / 定制版**；**演示版**为免授权评估包（`publish-demo.sh`）。旧名「充值版」发布时映射为商业版。
