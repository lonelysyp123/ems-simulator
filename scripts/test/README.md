# scripts/test — 手工验证与点表生成工具

这些脚本**不在 CI 里跑**，都是本地手工用的：验证 DataExchange 链路、生成点表、跑性能用例。
需要时先启动仿真器（`./scripts/dev-up.sh` 或直接跑 `EssSimulator.exe`），再按下面的说明执行。

## DataExchange 验证

| 脚本 | 用途 | 前置条件 |
|---|---|---|
| `run-bms-dataexchange-validation.sh` | 单单元 BMS/EMU 链路本地验证（自己起停仿真器） | 无 |
| `run-emu-dataexchange-validation.sh` | 单单元 EMU 链路验证；会临时替换 `appsettings.json`，结束后恢复 | 无 |
| `validate-bms-dataexchange.sh` | BMS 点读写校验 | 仿真器已启动，`simBms1` 的 1501 端口可连 |
| `validate-emu-dataexchange.sh` | EMU 点读写校验 | 仿真器已启动，`simEmu1` 的 1601 端口可连 |

## 点表生成

| 脚本 | 用途 |
|---|---|
| `gen_g2_4mwh_bms_bank.py` | 从 G2 4MWh 的 BMS-EMS Modbus IO List（Excel）生成 `bms_bank.csv` |
| `gen_pcs_pointmaps.py` | 从 TRINA MV-EMS Modbus TCP 协议 V0.15 生成 10MW / 5.5MW 两份中压 LC 点表 |

生成结果要人工比对后再覆盖 `pointmaps/models/` 下的文件；仓库里的 CSV 才是真源。

## 性能用例（`perf/`）

`run_tc_r01.py` ~ `run_tc_r05_pcs.py` 对应 `docs/archive/测试报告.md` 里的 TC-R01…R06：
用 `mbpoll`/Modbus 写点触发，记录"触发→监控看到变化"的 ΔT。跑之前确认仿真器在跑、端口对得上。

```bash
python3 scripts/test/perf/run_tc_r01.py
```

## 相关

- 点表目录与选型：[pointmaps/README.md](../../pointmaps/README.md)
- 性能测试报告（历史）：[docs/archive/测试报告.md](../../docs/archive/测试报告.md)
