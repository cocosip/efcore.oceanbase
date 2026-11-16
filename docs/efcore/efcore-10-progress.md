# EF Core 10 开发进度

本文档是开发队列和当前检查点。完整范围看 [功能矩阵](efcore-10-feature-matrix.md)，责任归属看 [功能缺失矩阵](efcore-10-gap-matrix.md)。

## 当前检查点：2026-11-16

| 项目 | 结果 |
| --- | --- |
| 分支 / 提交 | `efcore/10.0` / `c44f008` |
| 工作区 | 干净 |
| 最新模块 | `DateTimeOffset.DateTime` |
| Required 状态 | 每个模式 9 Done / 44 In progress |
| Local Release | Unit 2546、Functional 2469、Specification 1538；16 个既有映射 skip |
| Package | Consumer 2/2；EF10.0.0/10.0.14、MySQL/Oracle 两模式通过 |
| Native | 两模式均在 EF `OpenAsync` 阶段失败，未执行 SQL/DDL，reader 为 0 |

## 固定开发顺序

| 顺序 | 模块 | ID | 下一步交付物 |
| --- | --- | --- | --- |
| 0 | EF 连接路径和诊断 | `CFG-04`, `CON-02..05`, `DIA-01..02`, `SQL-01` | 对比 direct Connector 与 EF 创建连接的脱敏选项；修 Provider 差异；补同步/异步 lease 回归 |
| 1 | 真实只读验收 | `TST-02`, `REL-01` | 两模式 SELECT、身份、版本和错误分类；成功后才扩展写入 |
| 2 | 映射剩余边界 | `MOD-01..03`, `MAP-01..07` | 精度/长度/LOB/Guid/时区/转换器边界和必要 live round-trip |
| 3 | 查询翻译和物化 | `QRY-01..11` | 剩余 overload、组合、runtime specifications 和真实结果 |
| 4 | 写入和并发 | `UPD-01..05` | 真实 DML、生成值回读、并发冲突和约束 |
| 5 | 事务恢复 | `TXN-01..03`, `DEF-09` | 真实 commit/rollback/savepoint/取消原子性 |
| 6 | Schema 生命周期 | `SCH-01..03` | 隔离目标上的 DDL、权限、删除、锁和清理 |
| 7 | 规格和发布 | `TST-01`, `REL-02`, `PKG-01..04` | 关系 runtime families、最终 revision 和包矩阵 |

没有完成当前行的 RED/green、同步/异步、两模式、代码走查、适用的 full/package 检查和本地 commit，不开始下一行。

## 已完成检查点

| 日期 | 模块 | 提交 / 证据 | 未关闭边界 |
| --- | --- | --- | --- |
| 2026-11-16 | `DateTimeOffset.DateTime` | `c44f008`；Unit 3/3、Functional 3/3、Package 2/2 | Oracle native timezone/session 和 MySQL live rejection |
| 2026-11-16 | `DateTimeOffset.Ticks` | `a7aec5d` | Oracle native timezone/session |
| 2026-11-16 | `DateTimeOffset.Offset` | `a2c8e2c` | Oracle native interval/timezone |
| 2026-11-16 | `DateTimeOffset.TotalOffsetMinutes` | `1b0b295` | Oracle native extract/timezone |
| 2026-11-16 | Grouped results、DefaultIfEmpty、相关查询、Raw SQL、集合运算 | 对应提交和本地测试 | native query/result、完整 runtime families |
| 2026-11-15 及以前 | 关系写入、TPH/TPT/TPC、迁移、scaffolding、split、compiled/lazy、retry、HasData | 对应提交和本地/包证据 | native DDL/catalog/lock/failover/AOT 等 |

## 连接阻塞的事实

User Secrets 中的两个模式都指向 `172.16.2.45:2881`。MySQL 和 Oracle 都在打开连接阶段收到 packet closure；没有认证、服务端版本、SQL、DDL 或结果证据。这个事实只说明 EF Provider 连接路径尚未完成验证，不足以判断 Connector 缺陷。

下一次必须先完成：

1. 记录 EF `OceanBaseRelationalConnection` 最终创建连接的脱敏选项。
2. 用同一组 User Secrets 对比直接 Connector 与 EF 创建连接。
3. direct 成功而 EF 失败时修 Provider 并加回归；两者都失败时记录为环境/服务端或 Connector 待分类。
4. 重新执行两模式只读探针；成功后进入 `TST-02`。

## 模块完成规则

- 先在功能矩阵确认 ID、范围和拒绝边界。
- 先写 RED，再做最小实现；覆盖 MySQL/Oracle、同步/异步、正常/NULL/边界/失败。
- 做调用者、服务注册、SQL、参数、reader、ownership 和异常恢复走查。
- 更新功能矩阵和缺失矩阵，记录提交、命令、通过数、skip 和风险。
- `Local`、`Package`、`Specification` 不能写成 `Live`；未执行 SQL/DDL 必须明确写出。
- 明确延期项不进入当前开发顺序。
