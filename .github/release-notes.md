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
- **Modbus TCP**：并网点电表 `1500`、BMS `1501+`、EMU/PCS `1601+`、LC `1701+`；
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
