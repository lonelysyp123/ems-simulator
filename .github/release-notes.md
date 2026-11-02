### 发布包

解压即用，**无需安装 .NET 运行时**。启动后用浏览器打开 **http://127.0.0.1:5050**。

| 平台 | 文件 |
|---|---|
| Windows x64 | `EssSimulator-win-x64.zip` |
| Linux x64 | `EssSimulator-linux-x64.tar.gz` |
| macOS（Apple Silicon） | `EssSimulator-osx-arm64.tar.gz` |

#### 快速开始

```bash
# Windows：解压后双击 start.bat

# Linux
tar -xzf EssSimulator-linux-x64.tar.gz
cd linux-x64 && ./start.sh

# macOS（Apple Silicon）：首次运行需解除隔离
tar -xzf EssSimulator-osx-arm64.tar.gz
cd osx-arm64 && ./解除隔离.sh && ./start.sh
```

#### 这个包里有什么

- **完整功能**：主接线 2D / 3D、电池堆簇下钻、告警门限、组态编辑器、EMS 策略、白盒切片；
- **预设电站工程**，可直接在「组态编辑」里改拓扑、存成新工程；
- **Modbus TCP**：并网点电表 `1500`、BMS `1501+`、EMU/PCS `1601+`、LC `1701+`；
- **IEC 61850 MMS** 从 `8102` 起（GOOSE 订阅需网卡与 BPF 权限）。

#### 校验下载完整性

```bash
sha256sum -c ./*.sha256     # Linux
shasum -a 256 -c ./*.sha256 # macOS
```

#### 许可证

本项目以 **GPL-3.0** 发布，详见仓库 [LICENSE](https://github.com/lonelysyp123/ems-simulator/blob/master/LICENSE)。
IEC 61850 功能依赖 GPLv3 的 libiec61850，相关原生库随包分发。

#### 下一步

- 文档：仓库 [docs/](https://github.com/lonelysyp123/ems-simulator/tree/master/docs)（用户手册、指令说明、系统设计）
- 遇到问题：开 [Issue](https://github.com/lonelysyp123/ems-simulator/issues)，点表相关问题请附 CSV 片段 + 端口 + 日志
