# Modbus 连接改为异步收包

> 已确认：`ModbusPortHub` 自己异步收 Modbus TCP 帧，不调用 NModbus `ListenAsync`。白名单不再经环回转发。一条连接串行处理帧，事务号原样返回。未知从站回异常码 4 且不拆连接。不抬线程池最小线程数，不改一次图前端，不改 SEL 监听。
> 非有限数计划已归档：`tasks/finite-number-json.md` / `tasks/todo-finite-number-json.md`。

## 已确认
- [x] 收包放在 `ModbusPortHub`，空闲连接不占线程池工作线程
- [x] 从站挂载、`GetNetwork`、寄存器镜像和白名单语义保留
- [x] 不实现同一条连接上的请求流水线
- [x] 不按轮询请求写日志

## Phase 1: 一帧能收能回
- [x] Task 1: MBAP 组帧
- [x] Task 2: 单连接异步会话（依赖 Task 1）

## Checkpoint: 一帧能收能回
- [x] 会话测试里的主站读到预先写入的寄存器
- [x] 非零事务号能对上响应
- [x] 未知从站号返回异常后，同一连接的下一次合法读仍成功
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~ModbusTcpFrame|FullyQualifiedName~ModbusTcpSession"`
- [x] `dotnet build ./EssSimulator.csproj`

## Phase 2: 接入现有端口
- [x] Task 3: `ModbusPortHub` 改用异步会话，去掉 `ListenAsync` 和环回泵（依赖 Task 2）
- [x] Task 4: 40 条空闲连接不挡住活动读（依赖 Task 3）

## Checkpoint: Complete
- [x] 共享传输、白名单、第三方 EMS 客户端测试通过
- [x] 空闲连接存在时，活动读在 1 秒内返回
- [x] 两个主站同时读到同一个值
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~ModbusPortHub|FullyQualifiedName~ModbusTcpSlaveSharedTransport|FullyQualifiedName~ThirdPartyEmsModbusClient|FullyQualifiedName~ModbusTcpFrame|FullyQualifiedName~ModbusTcpSession"`
- [x] `dotnet build ./EssSimulator.csproj`
