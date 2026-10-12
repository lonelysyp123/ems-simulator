# Implementation Plan: Modbus 连接改为异步收包

## Overview

多套 EMS 只做采集时，每个 Modbus TCP 连接都会在 NModbus 的 `ListenAsync` 里占住一条线程池线程。这些线程和网页的 HTTP、SignalR 共用。端口一多，`GET /api/mainline` 排不进队，一次图停在空快照上。本计划让 `ModbusPortHub` 自己接受连接，用异步读拼出一帧再调用从站处理。连接在等下一包时不占工作线程。点表、从站号、白名单和寄存器镜像保持不变。

上一份计划（一次图快照避开非有限数）已归档到 `tasks/finite-number-json.md` / `tasks/todo-finite-number-json.md`。

## Architecture Decisions

- **不再调用 `IModbusSlaveNetwork.ListenAsync`。** 从站网络仍然创建，`AddSlave` / `GetSlave` / `GetNetwork` 继续给设备挂载和进程内读写使用。收包改由本进程的异步会话完成。
- **白名单和普通监听走同一条接受循环。** 现在开白名单时会把外部连接泵到环回上的 NModbus 监听，每条连接仍然进 `ListenAsync`。改完后对外只有一个 `TcpListener`，拒绝发生在开始读报文之前，不再做字节转发。
- **一条连接一次只处理一帧。** 读完 MBAP、调用 `IModbusSlave.ApplyRequest`、写回响应，然后再读下一帧。不实现同一连接上的流水线。现场 EMS 是一问一答。
- **事务号原样回写。** 响应 MBAP 的事务标识等于请求。长度字段按响应 PDU 重算。
- **未知从站号不拆连接。** 同一端口上堆从站和簇从站共用一条 TCP。某个从站号不存在时回异常功能码，异常码用 NModbus 的 `SlaveDeviceFailure`（4），然后继续读下一帧。
- **报文长度只接受 1 到 260 字节。** 这是单元标识加 PDU 的上限。长度非法或读到半帧对端断开时，只关掉这一条连接。
- **不按请求打日志。** 接受、拒绝和会话异常各记一条。采集轮询不写流量日志。
- **不抬线程池最小线程数，不改一次图前端，不改 SEL 自己的 Modbus 监听。** 那些不解决「每条连接占一条池线程」。

## 依赖图

```
MBAP 组帧（Task 1）
        │
        └── 单连接异步会话（Task 2）
                    │
                    └── 挂进 ModbusPortHub，去掉 ListenAsync 和环回泵（Task 3）
                                │
                                └── 多条空闲连接不挡住正在读的主站（Task 4）
```

## Task List

### Phase 1: 一帧能收能回

- [x] Task 1: MBAP 组帧
- [x] Task 2: 单连接异步会话

### Checkpoint: 一帧能收能回

- [x] 不经过 `ModbusPortHub` 也能用主站完成一次读，事务号一致
- [x] 测试与 `dotnet build ./EssSimulator.csproj` 通过

### Phase 2: 接入现有端口

- [x] Task 3: `ModbusPortHub` 改用异步会话
- [x] Task 4: 空闲连接不挡住活动读

### Checkpoint: Complete

- [x] 现有共享传输、白名单、第三方 EMS 客户端测试仍通过
- [x] 多条只连接不发报文的客户端存在时，另一条读请求在 1 秒内返回
- [x] 相关测试与 `dotnet build ./EssSimulator.csproj` 通过

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| 响应没带回事务号，EMS 把包丢掉并重连 | High | Task 1 用非零事务号断言回写；Task 2 用 NModbus 主站做往返 |
| `ApplyRequest` 没把事务号抄到响应上 | Med | 会话在写 MBAP 前把请求的事务号赋给响应 |
| 未知从站号直接关连接，扫簇从站时把整条链路打掉 | High | Task 2 规定回异常码 4 并继续读 |
| 白名单路径去掉环回泵后，拒绝行为变了 | Med | Task 3 保留现有三个白名单测试 |
| 半包或超长长度把接受循环拖死 | Med | 非法长度只关当前连接；接受循环单独捕获取消和套接字错误 |
| 同一从站的并发读把寄存器镜像写乱 | Low | 仍用 NModbus 的 `SlaveDataStore`，它自己有锁 |

## Open Questions

无。收包在 `ModbusPortHub` 内用异步读完成，不调用 `ListenAsync`。一条连接串行处理帧，未知从站回异常码 4。

## Task 1: MBAP 组帧

**Description:** 抽出不依赖套接字的 Modbus TCP 帧处理：从 6 字节头解析长度，判断长度是否在 1 到 260，并用请求里的事务号给响应加上 MBAP 头。本任务不读网络，也不调用从站。

**Acceptance criteria:**
- [ ] 事务号 `0x1234`、协议标识 0、后续 6 字节的头，解析出的长度是 6
- [ ] 长度为 0 或大于 260 时判定为非法
- [ ] 用该事务号包装一段响应体后，前两字节仍是 `0x1234`，长度字段等于响应体字节数

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~ModbusTcpFrame"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 无

**Dependencies:** None

**Files likely touched:**
- `Protocol/Modbus/ModbusTcpFrame.cs`
- `EssSimulator.Tests/Protocol/ModbusTcpFrameTests.cs`

**Estimated scope:** Small: 1–2 files

## Task 2: 单连接异步会话

**Description:** 新增异步会话：从 `NetworkStream` 读满一帧，用 `IModbusFactory` 的 `CreateModbusRequest` 解码，再对 `GetSlave(unitId).ApplyRequest` 的结果写回。未知从站号回功能码加 `0x80`、异常码 4，连接保持。长度非法或对端关闭时结束这一个会话。测试自己起一个只含该会话的监听，不改 `ModbusPortHub`。

**Acceptance criteria:**
- [ ] NModbus 主站对已注册从站做一次保持寄存器读，读到预先写入的值，且能在超时内返回
- [ ] 请求事务号不是 0 时，主站仍能匹配到响应
- [ ] 读取未注册的从站号得到从站异常，同一连接上随后的合法读仍然成功

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~ModbusTcpSession"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 无

**Dependencies:** Task 1

**Files likely touched:**
- `Protocol/Modbus/ModbusTcpSession.cs`
- `EssSimulator.Tests/Protocol/ModbusTcpSessionTests.cs`
- `Protocol/Modbus/ModbusTcpFrame.cs`

**Estimated scope:** Medium: 3 files

## Task 3: ModbusPortHub 改用异步会话

**Description:** `PortListenerHost.Start` 无论是否启用白名单，都只对外监听，并为每条通过白名单的连接启动 `ModbusTcpSession`。不再调用 `ListenAsync`，也不再把连接泵到环回端口。`GetNetwork`、从站挂载、地址查重和断开时释放端口保持原样。

**Acceptance criteria:**
- [ ] 现有共享传输读写、换端口重绑、簇从站号展开测试通过
- [ ] 空白名单、允许环回、拒绝环回三个测试的结果与现在一致
- [ ] 端口上的代码路径不再调用 `ListenAsync`，也不再创建环回 `TcpListener`

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~ModbusPortHub|FullyQualifiedName~ModbusTcpSlaveSharedTransport|FullyQualifiedName~ThirdPartyEmsModbusClient|FullyQualifiedName~ModbusTcpSession"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 在 `ModbusPortHub.cs` 中确认没有 `ListenAsync` 和环回转发

**Dependencies:** Task 2

**Files likely touched:**
- `Protocol/Modbus/ModbusPortHub.cs`
- `Protocol/Modbus/ModbusTcpSession.cs`

**Estimated scope:** Medium: 2 files

## Task 4: 空闲连接不挡住活动读

**Description:** 在已经改完的端口上验证多主站。先挂上从站并写入一个保持寄存器，再连入多条只完成 TCP 握手、不发送 Modbus 报文的客户端，然后用另一条主站读取。读必须在 1 秒内返回原值。再补两条同时在读的主站，两条都拿到同一个值。监听关闭后这些连接都被断开。

**Acceptance criteria:**
- [ ] 40 条空闲连接存在时，活动主站的保持寄存器读在 1 秒内返回写入值
- [ ] 两个主站同时读同一个从站，都返回该值
- [ ] `ShutdownAll` 之后活动主站的下一次读失败，端口不再处于监听

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~ModbusPortHub"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 无

**Dependencies:** Task 3

**Files likely touched:**
- `EssSimulator.Tests/Protocol/ModbusPortHubTests.cs`

**Estimated scope:** Small: 1 file
