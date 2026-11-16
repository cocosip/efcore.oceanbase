# EF Core 10 `DateTimeOffset.DateTime` 模块

**状态：** Provider、测试和包门禁已完成；真实 OceanBase 验收仍受 Connector 握手阻塞。

## 目标与边界

实现 `DateTimeOffset.DateTime` 的本地墙上时间语义：返回值是输入值的日期和时钟部分，
`DateTimeKind.Unspecified`。Oracle 的 `TIMESTAMP WITH TIME ZONE` 载体保留该值，可以转换为现有
`DateTime`/`TIMESTAMP` 查询载体；MySQL 的 `DateTimeOffset` 以 UTC 归一化 `DATETIME` 存储，
原始墙上时间已丢失，因此在 SQL 生成前拒绝。

## 方案与取舍

1. **Oracle-only composition（采用）**：将合法 Oracle `DateTimeOffset` 表达式转换为现有
   `DateTime` mapping，复用 SQL generator 的安全日期转换路径。代码最少并保持墙上时间语义。
2. **两种模式返回存储值（拒绝）**：MySQL 会静默返回 UTC 时间，违反 .NET `DateTimeOffset.DateTime`
   语义并与 `UtcDateTime` 混淆。
3. **实现 `LocalDateTime`（拒绝）**：该成员依赖执行机本地时区，不能由 Provider SQL 在两个模式中
   稳定重建；它保持单独的未定义 contract。

## 验收标准

| 层级 | 通过条件 |
| --- | --- |
| Unit | Oracle 生成 `CAST` 到现有 `DateTime` carrier；MySQL、错误 carrier 和 null-converting converter 拒绝。 |
| Functional | Oracle 正/负 offset、NULL、同步/异步 reader 返回本地墙上 `DateTime`；MySQL 不开连接即拒绝。 |
| Package | 新包内容校验、EF10.0.0/10.0.14 和两模式 PackageConsumer 通过。 |
| Review | 代码走查无 Critical/Important 问题，`git diff --check` 通过，未改变 Connector。 |
| Live | 当前 `172.16.2.45:2881` pre-auth packet closure 继续标为 `Blocked-live`，握手恢复后再运行。 |

## 已完成证据

- Base commit: `a7aec5d`; implementation uses one Oracle translator branch and no Connector change.
- RED: before implementation, the Oracle Unit and Functional positive queries failed with EF's standard
  translation error; MySQL and unsupported-carrier controls remained rejected.
- Unit: `DateTimeOffset_DateTime_has_local_wall_clock_boundary` 2 cases and
  `DateTimeOffset_DateTime_rejects_unowned_and_null_converting_carriers` 1 case passed in Release.
- Functional: `Oracle_DateTimeOffset_DateTime_materializes_local_wall_clock_values_and_null` 2 cases and
  `MySql_DateTimeOffset_DateTime_is_rejected_before_command_execution` 1 case passed in Release.
- Full Release: Unit 2546, Functional 2469, Specification 1538 with 16 existing mapped skips, and
  ConsumerTests 2/2 passed.
- Package `10.0.0-date-time-offset-date-time-20261116` passed `Verify-Package.ps1`; PackageConsumer passed
  on EF10.0.0 and EF10.0.14 in both MySQL and Oracle modes.
- Oracle SQL uses the existing timestamp cast and contains no `SYS_EXTRACT_UTC`; reader results retain
  `DateTimeKind.Unspecified` and NULL propagation.

These are Provider translation, mapping, intercepted reader and package-consumer results. They do not establish
native OceanBase timezone/session semantics, timestamp precision or server execution.

## 不在范围内

- `DateTimeOffset.LocalDateTime`、`DateTimeOffset.Now`、`UtcDateTime` 或其他时区转换 API。
- Connector 协议、认证、TLS、reader、依赖和连接池代码。
- EF Core 8/9、真实 DDL/写入、推送和发布。
