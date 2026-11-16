# EF Core 10 `DateTimeOffset.Offset` 模块

**状态：** 本地 Provider/包闭环完成，等待最终走查和提交；真实 OceanBase 验收仍受 Connector 握手阻塞。

## 目标

为 `DateTimeOffset.Offset` 提供一个可验证的 EF Core 查询边界，同时保持 Provider 与
OceanBaseConnector 0.3.0 的职责分离。该属性返回 `TimeSpan`，因此 Oracle 模式复用已有
`TimeSpan`/`INTERVAL DAY TO SECOND` 映射；MySQL 模式的 `DateTimeOffset` 存储为 UTC 归一化
`DATETIME`，原始偏移已经丢失，必须在 SQL 生成前拒绝。

## 方案与取舍

1. **Oracle-only translation（采用）**：将 `EXTRACT(TIMEZONE_HOUR/MINUTE)` 组合为分钟数，
   再通过 `NUMTODSINTERVAL(..., 'MINUTE')` 生成 `TimeSpan` 结果。它复用现有映射和 SQL 生成器，
   不需要 Connector 改动。
2. **两种模式都返回零偏移（拒绝）**：会把不可恢复的数据静默变成错误结果。
3. **修改 Connector 增加偏移读取 API（拒绝）**：当前没有证据表明公开 API 缺失，超出本模块
   的 Provider 查询职责。

## 范围

- Oracle `DateTimeOffset.Offset`：正常值、负偏移、NULL、同步/异步 reader materialization、
  predicate/order-by SQL 形状。
- MySQL `DateTimeOffset.Offset`：在 SQL 生成前抛出清晰的翻译异常，不打开连接。
- 只接受现有 Oracle `TIMESTAMP WITH TIME ZONE` 载体和不转换 NULL 的映射；无关 store type、
  null-converting converter 以及错误结果载体拒绝。

## 不在范围内

- `DateTimeOffset.Now`、`LocalDateTime`、Connector 协议/认证/TLS/reader 改动。
- 真实 OceanBase timezone/session/coercion 结果；当前端点在认证前报
  `Connection closed while reading the packet`，继续标记为 `Blocked-live`。
- EF Core 8/9、推送、发布或 Required 行晋级。

## 验收标准

| 层级 | 通过条件 |
| --- | --- |
| Unit | Oracle marker/映射校验和 MySQL/错误载体边界覆盖；正负分钟和 NULL SQL 形状可断言。 |
| Functional | 两模式、同步/异步、reader 返回 `TimeSpan`；Oracle predicate/order-by 可执行于拦截管线；MySQL 在开连接前失败。 |
| Package | nupkg 内容、EF10.0.0/10.0.14、MySQL/Oracle consumer smoke 通过。 |
| Review | 独立代码走查无 Critical/Important 问题；`git diff --check` 通过。 |
| Live | 仅在端点握手恢复后运行；未恢复前不得把本地拦截结果当作服务器验收。 |

## 已完成证据

- RED：Oracle `Offset` 查询在实现前不可翻译；MySQL rejection control 保持通过。
- Unit：`TemporalMemberQueryTests` 时态全套 110/110；本模块新增 3/3。
- Functional：相关时态过滤 26/26；本模块新增 3/3，覆盖正/负/NULL、同步/异步和 MySQL 预开连接拒绝。
- Full Release：Unit 2540、Functional 2463、Specification 1538 通过，Specification 保留 16 个既有映射跳过；ConsumerTests 2/2。
- Package：`10.0.0-date-time-offset-offset-20261116` 通过内容校验；PackageConsumer 在 EF10.0.0/10.0.14 与 MySQL/Oracle 两模式通过。
- Live：沿用已记录的 `172.16.2.45:2881` Connector 0.3.0 预认证 packet closure；没有 SQL/DDL 执行，EF reader 为 0。

## 预期变更

- `src/OceanBase.EntityFrameworkCore/Query/OceanBaseTemporalMemberTranslator.cs`
- `src/OceanBase.EntityFrameworkCore/Query/OceanBaseQuerySqlGenerator.cs`
- `tests/OceanBase.EntityFrameworkCore.UnitTests/TemporalMemberQueryTests.cs`
- `tests/OceanBase.EntityFrameworkCore.FunctionalTests/TimeSpanTicksFunctionalTests.cs`
- `tests/OceanBase.EntityFrameworkCore.PackageConsumer/Program.cs`
- 本模块记录、进度、清单和 roadmap。
