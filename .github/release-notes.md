### 本次更新（v0.1.3）

- **光伏区拆成三层**：组态里一个光伏单元（`pvu`）下挂 N 条光伏 PCS（`pv_pcs`），每条支路对应一个光伏方阵（`pv_array`）；一支路必须对一方阵，同单元内型号/额定必须一致。旧工程打开时自动展开，运行时仍按单元汇总，协议侧不受影响。
- **光伏可以按台控制**：`esscmd setpvN pcs M power|reactive|run`，以及 `setpvN maxpower` / `setpvN pcs M maxpower` 给单元或单台设最大放电上限（关掉后回到原来记住的设定）。
- **一次图按绕组组织**：布局不再按画布坐标"贴最近母线"，改为沿端口绕组往下走（两绕组 `pri`/`sec`，双耳变压器 `pri`/`ear_l`/`ear_r`）；光伏链画成 PCS 在上、方阵在下，方阵卡显示实时读数。
- **中压断路器协议**：组态里每台三相断路器一路 Modbus（`2301+`），支持 `AA`/`EE` 遥控与按角色的遥测。
- 修掉协议层一处并发写字典导致的崩溃（并行启动从站时）。

### 发布包

解压即用，**无需安装 .NET 运行时**。启动后用浏览器打开 **http://127.0.0.1:5050**。

| 平台 | 文件 |
|---|---|
| Windows x64 | `EssSimulator-win-x64.zip` |

#### 快速开始

```bash
# 解压后双击 start.bat（或直接运行 EssSimulator.exe）
```

#### 这个包里有什么

- **完整功能**：主接线 2D / 3D、电池堆簇下钻、告警门限、组态编辑器、EMS 策略、白盒切片；
- **预设电站工程**，可直接在「组态编辑」里改拓扑、存成新工程；
- **Modbus TCP**：并网点电表 `1500`、BMS `1501+`、EMU/PCS `1601+`、LC `1701+`、光伏 Logger `1801+`、光伏电表 `1901+`、逆变器 `2101+`、中压断路器 `2301+`；
- **IEC 61850 MMS** 从 `8102` 起（GOOSE 订阅需网卡与 BPF 权限）。

#### 校验下载完整性

```powershell
Get-FileHash .\EssSimulator-win-x64.zip -Algorithm SHA256
# 与 EssSimulator-win-x64.zip.sha256 里的值比对
```

#### 已知限制

- **暂不启用 IEC 61850 GOOSE 订阅**：随包原生库未编译 GOOSE 订户，运行期会降级为"不启用"并给出原因；**MMS 服务端不受影响**。
  详见 [项目编译说明](https://github.com/lonelysyp123/ems-simulator/blob/master/docs/项目编译说明.md) 的「已知限制」。

#### 许可证

本项目以 **GPL-3.0** 发布，详见仓库 [LICENSE](https://github.com/lonelysyp123/ems-simulator/blob/master/LICENSE)。
IEC 61850 功能依赖 GPLv3 的 libiec61850，相关原生库随包分发。

#### 下一步

- 文档：仓库 [docs/](https://github.com/lonelysyp123/ems-simulator/tree/master/docs)（用户手册、指令说明、系统设计）
- 遇到问题：开 [Issue](https://github.com/lonelysyp123/ems-simulator/issues)，点表相关问题请附 CSV 片段 + 端口 + 日志
