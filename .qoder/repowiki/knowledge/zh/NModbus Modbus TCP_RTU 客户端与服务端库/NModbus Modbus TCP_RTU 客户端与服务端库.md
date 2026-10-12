---
kind: external_dependency
name: NModbus Modbus TCP/RTU 客户端与服务端库
slug: nmodbus
category: external_dependency
category_hints:
    - sdk_real_api
scope:
    - '**'
---

本项目通过 NuGet 包 `NModbus`（v3.0.81）实现 Modbus 协议通信：
- 作为 Modbus TCP 服务端对外暴露 BMS、EMU、PCS、LC 等设备的寄存器映射，端口由 `Protocol.BaseBmsModbusPort` / `BaseEmuModbusPort` / `BaseLocalControlModbusPort` 配置。
- 同时作为客户端访问外部 PV 设备（如 APM810 电表）的 Modbus 寄存器。
- 点表 CSV（`bms_bank.csv`、`bms_rack.csv`、`emu.csv`、`em.csv`、`lc.csv`、`pv_apm810.csv`、`pv_logger.csv`）经 `DataExchange/Catalog` 与 `Pv` 模块解析后映射到 NModbus 寄存器地址；修改点表需同步更新对应 CSV 及测试断言。
- 发布时这些 CSV 文件通过 `<None Update="...">` 复制到输出目录，运行时从工作目录读取固定文件名。