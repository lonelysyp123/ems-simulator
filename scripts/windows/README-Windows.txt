EssSimulator - Windows 快速说明
================================

完整使用说明请阅读同目录下的 README.txt。

快速启动
--------
1. 解压整个 win-x64 文件夹（保持文件在同一目录）
2. 双击 start.bat，或运行 EssSimulator.exe
3. 等待协议服务就绪后使用 GUI 或 Modbus 联调

默认 Modbus 端口：电表 1500，BMS1 1501，EMU1 1601（详见 README.txt）
IEC 61850：需 iec61850.dll 与 EssSimulator.exe 同目录；默认 MMS 端口从 8102 起。
设备侧为 MMS 服务端 + GOOSE 订户（不发 GOOSE）。
Windows 默认 GOOSE 网卡索引 "0"（第一块以太网，见 appsettings EmuIec61850GooseInterface）；
与 IEDScout 选同一网卡，管理员运行，并安装 Npcap（WinPcap 兼容模式）。
若「GOOSE 订」一直关，多半是 iec61850.dll 未编入 GooseSubscriber，需换带订阅的原生库。

详细说明见 README.txt；技术手册见 docs/用户手册.md（若已随包提供）
