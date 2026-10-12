# 点表速查（生成 mbpoll 前须查 CSV 确认 FC）

**`-t` 由 FunctionCode 决定，见 SKILL.md 映射表。**

## emu.csv 控制点（Unit-1，端口 1601）

| ParamName | FC | 地址 | Type | Scale | mbpoll 写 `-t` | 说明 |
|---|---|---:|---|---:|---|---|
| highvoltagebreakeronoff | 5 | 4 | bool | 1 | 0 | 高压断路器 |
| lowvoltagebreakeronoff | 5 | 5 | bool | 1 | 0 | 低压断路器 |
| param55 | 6 | 5300 | int16 | 10 | 4 | PCS1 有功 kW |
| param56 | 6 | 5301 | int16 | 10 | 4 | PCS1 无功 kvar |
| param57 | 6 | 5302 | int16 | 1000 | 4 | PCS1 功率因数 |
| param64 | 6 | 5304 | u16 | 1 | 4 | PCS1 孤岛电压V 0-690 |
| pcs1_blackstart_enable | 5 | 5305 | bool | 1 | 0 | PCS1 黑启动 |
| pcs1_startstop | 5 | 5303 | bool | 1 | 0 | PCS1 启停 |
| param59 | 6 | 5600 | int16 | 10 | 4 | PCS2 有功 kW |
| param60 | 6 | 5601 | int16 | 10 | 4 | PCS2 无功 kvar |
| param61 | 6 | 5602 | int16 | 1000 | 4 | PCS2 功率因数 |
| param65 | 6 | 5604 | u16 | 1 | 4 | PCS2 孤岛电压V 0-690 |
| pcs2_blackstart_enable | 5 | 5605 | bool | 1 | 0 | PCS2 黑启动 |
| pcs2_startstop | 5 | 5603 | bool | 1 | 0 | PCS2 启停 |

## emu.csv 数据点（只读示例）

| 类型 | FC | mbpoll 读 `-t` | 例 |
|---|---|---|---|
| 遥测/状态 | 4 | 3（32 位用 `3:int`，`-c 2`） | `param1` 地址 25300 |

## 其它点表

| 文件 | 典型 FC | 端口（默认） |
|------|---------|-------------|
| em.csv | 4（读） | 1500 |
| bms_bank.csv | 4（读） | 1501 + N - 1 |
| bms_rack.csv | 6（写门限等） | 同上，从站 2+ |

Unit-N 端口：`1601/1501 + N - 1`（EMU/BMS，以 appsettings 为准）。
