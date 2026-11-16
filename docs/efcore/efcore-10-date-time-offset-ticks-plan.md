# EF Core 10 `DateTimeOffset.Ticks` 模块

**状态：** Provider、测试和包门禁已完成；真实 OceanBase 验收仍受 Connector 握手阻塞。

## 目标与边界

实现 `DateTimeOffset.Ticks` 的本地墙上时间语义：返回值是该值的本地日期时间部分相对于
Gregorian `0001-01-01T00:00:00` 的 100ns tick 数。Oracle 的 `TIMESTAMP WITH TIME ZONE`
载体保留本地墙上时间，可以转换为现有 `DateTime`/`TIMESTAMP` 查询载体并复用 `DateTime.Ticks`
算术；MySQL 的 `DateTimeOffset` 使用 UTC 归一化 `DATETIME`，原始偏移和本地墙上时间已丢失，
必须在 SQL 生成前拒绝。

## 方案与取舍

1. **Oracle-only composition（采用）**：将合法 Oracle `DateTimeOffset` 表达式转换为现有
   `DateTime`/`TIMESTAMP` 查询表达式，再复用 `DateTimeTicksMarker`。代码最少，Gregorian 算术
   只有一个实现，Connector 不需要变化。
2. **两种模式返回 UTC ticks（拒绝）**：会把 `Ticks` 与 `UtcTicks` 混淆，并在 MySQL 上静默产生错误。
3. **新增 Connector 读取/转换 API（拒绝）**：没有已证明的公开 API 缺口，超出 Provider 查询边界。

## 验收标准

| 层级 | 通过条件 |
| --- | --- |
| Unit | Oracle SQL 复用 DateTime tick 算术且不调用 UTC 提取；MySQL、错误 carrier 和 null-converting converter 拒绝。 |
| Functional | Oracle 正/负偏移和 NULL 的本地 tick reader 值在同步/异步下正确物化；MySQL 不开连接即拒绝。 |
| Package | 新包内容校验、EF10.0.0/10.0.14 和两模式 PackageConsumer 通过。 |
| Review | 代码走查无 Critical/Important 问题，`git diff --check` 通过，结果不会声称 native server 语义。 |
| Live | 端点握手恢复后再运行；当前 `172.16.2.45:2881` 的 pre-auth packet closure 继续标为 `Blocked-live`。 |

## 已完成证据

- Base commit: `a2c8e2c`; implementation changes are ready for the local commit after final review.
- Unit: `DateTimeOffset_Ticks_has_local_wall_clock_boundary` 2 cases and
  `DateTimeOffset_Ticks_rejects_unowned_and_null_converting_carriers` 1 case passed in Release.
- Functional: `Oracle_DateTimeOffset_Ticks_materialize_local_wall_clock_values_and_null` 2 cases and
  `MySql_DateTimeOffset_Ticks_is_rejected_before_command_execution` 1 case passed in Release.
- Full Release: Unit 2543, Functional 2466, Specification 1538 with 16 existing mapped skips, and
  ConsumerTests 2/2 passed.
- Package `10.0.0-date-time-offset-ticks-20261116` passed `Verify-Package.ps1`; its PackageConsumer passed
  on EF10.0.0 and EF10.0.14 in both MySQL and Oracle modes.
- Code review found no Critical/Important issue and `git diff --check` passed. Connector source and dependency
  files were unchanged.

These are Provider translation, mapping, intercepted reader and package-consumer results. They do not establish
native OceanBase timezone/session semantics, timestamp precision or server execution.

## 不在范围内

- `DateTimeOffset.LocalDateTime`、`Now` 或其他时区转换 API。
- Connector 协议、认证、TLS、reader、依赖和连接池代码。
- EF Core 8/9、真实 DDL/写入、推送和发布。
