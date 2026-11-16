# EF Core 10 `DateTimeOffset.TotalOffsetMinutes` 模块

**状态：** 本地 Provider/包闭环完成；真实 OceanBase 验收仍为 `Blocked-live`

**范围：** `MAP-05`、`QRY-07`、`QRY-11`。Oracle 模式从现有 `TIMESTAMP WITH TIME ZONE`
carrier 翻译 `DateTimeOffset.TotalOffsetMinutes`；MySQL 模式因 UTC 归一化 `DATETIME` 不保存原始偏移，
在 SQL 生成前保持标准未翻译错误。只使用 Connector 0.3.0 公共映射、命令和 reader API。

## 实现边界

- `OceanBaseTemporalMemberTranslator` 只接受 Oracle、非 null-converting、Provider-owned
  `DateTimeOffset`/`TIMESTAMP` mapping，并生成私有 marker。
- `OceanBaseQuerySqlGenerator` 校验 marker 的类型、可空性、参数数量、输入/结果 mapping 和模式，输出
  `EXTRACT(TIMEZONE_HOUR FROM value) * 60 + EXTRACT(TIMEZONE_MINUTE FROM value)`。
- MySQL 不生成 marker；要求服务器端翻译的谓词/组合查询在打开连接前由 EF 抛出标准翻译错误。EF
  允许的顶层客户端投影仍遵循 EF 自身的客户端求值边界。
- 不实现 `Offset`、`DateTime`、`LocalDateTime`、`Now`，不改 Connector，不声称 OceanBase 服务器语法/时区
  行为或 wire cancellation。

## 测试与证据

| 层级 | 结果 |
| --- | --- |
| RED | 当前实现 Oracle 正向 Unit 用例未翻译；MySQL/负向边界保持拒绝 |
| Unit | `DateTimeOffset_total_offset_minutes*`：4/4 Release 通过 |
| Functional | Oracle sync/async 物化 `480` 与 `NULL`、MySQL 谓词拒绝：3/3 Release 通过 |
| Full Release | Unit 2537、Functional 2460、Specification 1538 通过；Specification 保留 16 个既有 mapped skips |
| Package | `10.0.0-total-offset-minutes-20261116` 内容检查通过；PackageConsumer 在 EF10.0.0/10.0.14、两模式通过 |
| Live | MySQL/Oracle `LiveConnectionTests` 各 1 次失败于预认证；未执行 SQL/DDL，EF reader=0 |

## 验收结论

本模块证明了 Provider 的双模式 SQL 边界、映射准入、NULL 物化、同步/异步命令管线和打包消费者兼容性。
拦截式 reader 不代表真实服务器结果。真实端点 `172.16.2.45:2881` 在 Connector 0.3.0 握手期间关闭 packet，
异常为 `OceanBaseException` 内层 `ProtocolException: Connection closed while reading the packet`；因此
服务器 `EXTRACT(TIMEZONE_*)`、租户时区、精度和 native 结果保持 `Blocked-live`，不能将 Required 行标记为 Done。

**Connector 判定：** 当前 Provider 不需要 Connector 变更；若要关闭 live gate，需要先解决端点/传输/预认证握手
问题，再复跑同一只读探针。

**本地提交：** 已在最终走查后创建；最终 hash 以 `git log` 为准。
