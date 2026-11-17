# EF Core 10 开发进度

本文档是开发队列和当前检查点。完整范围看 [功能矩阵](efcore-10-feature-matrix.md)，责任归属看 [功能缺失矩阵](efcore-10-gap-matrix.md)。

## 当前检查点：2026-11-17

| 项目 | 结果 |
| --- | --- |
| 分支 / 提交 | `efcore/10.0`；真实 bounded text storage 模块，基线 `a47602c` |
| 工作区 | 本模块源码、测试和文档一并提交；凭据仅从 User Secrets 读取 |
| 最新模块 | 两模式真实文本列、默认值/HasData 和 tracked DML；修复 MySQL 字面量 DEFAULT 与生成值回读；见 [模块记录](efcore-10-text-storage-plan.md) |
| Required 状态 | 每个模式 9 Done / 44 In progress |
| Local Release | EF10.0.0 和隔离 EF10.0.14 各通过 Unit 2613、Functional 2487、Specification 1538、Consumer 2；16 个既有映射 skip |
| Package | text-storage 包内容检查和两个 patch 的两模式消费者通过；消费 DLL 与已测试源码 DLL 哈希相同 |
| Native | 4.4.2.2 普通直连；此前的两模式连接/只读查询/字面量通过；本模块已执行唯一自建表的 DDL、HasData、默认值、参数写入/读取、tracked update/delete 和清理；Oracle 生成值测试显式开启 Connector PrepareExecute |

## 固定开发顺序

| 顺序 | 模块 | ID | 下一步交付物 |
| --- | --- | --- | --- |
| 0 | EF 连接路径和诊断 | `CFG-04`, `CON-02..05`, `DIA-01..02`, `SQL-01` | 对照 Connector 普通直连契约；只修有证据的 Provider 差异；补同步/异步 lease 回归 |
| 1 | 真实只读验收 | `TST-02`, `REL-01` | 两模式 SELECT、身份、版本和现有查询 profile 已通过；更宽错误分类和写入/事务/Schema 待验收 |
| 2 | 映射剩余边界 | `MOD-01..03`, `MAP-01..07` | 精度/长度/LOB/Guid/时区/转换器边界和必要 live round-trip |
| 3 | 查询翻译和物化 | `QRY-01..11` | 剩余 overload、组合、runtime specifications 和真实结果 |
| 4 | 写入和并发 | `UPD-01..05` | 真实 DML、生成值回读、并发冲突和约束 |
| 5 | 事务恢复 | `TXN-01..03`, `DEF-09` | 真实 commit/rollback/savepoint/取消原子性 |
| 6 | Schema 生命周期 | `SCH-01..03` | 隔离目标上的 DDL、权限、删除、锁和清理 |
| 7 | 规格和发布 | `TST-01`, `REL-02`, `PKG-01..04` | 关系 runtime families、最终 revision 和包矩阵 |

按顺序完成各模块的本地实现、RED/green、同步/异步、两模式、代码走查、适用的 full/package 检查和本地 commit。外部连接不可用时，保留对应 live 验收缺口并继续下一模块的本地工作；不得将缺少 live 证据的行改为 Done，也不得因此冻结全部 Provider 开发。

## Connector 能力边界

当前 EF 使用 `OceanBaseConnector` 0.3.0 包的公共 ADO.NET API，验收基线是 MySQL/Oracle 普通协议直连。包声明的源码 revision 为 `7f0cf7e544bf8b944d584ddf9116bea03ec4c5df`；能力边界以该 revision 的 [Connector compatibility](https://github.com/cocosip/obconnector-dotnet/blob/7f0cf7e544bf8b944d584ddf9116bea03ec4c5df/docs/compatibility.md) 为准。

- Connector 历史直连证据不能替代 EF 验收；本次恢复后另外验证了两模式 EF 普通直连，服务端均报告 4.4.2.2，TLS Disabled。
- 真实 OBProxy/ODP、多节点/OCP/TNS、backend migration、OB20/trace 和真实 TLS 等扩展部署尚未验收或暂不开发，不作为 EF10 普通直连的前置任务。
- compressed OB20、proxy reroute 和透明会话迁移等排除项不进入 EF Provider 的开发清单；存在本地协议代码或连接字符串选项，不表示实际部署已受支持。

## 已完成检查点

| 日期 | 模块 | 提交 / 证据 | 未关闭边界 |
| --- | --- | --- | --- |
| 2026-11-17 | 真实 bounded text storage、MySQL DEFAULT 和生成值回读 | 本模块；两 patch 本地各 6640 通过/16 skip；两 patch 串行 LiveTests 各 25 通过（15 live、10 offline）；包消费者通过 | 32 scalar variable text、8 字符 fixed、NULL/空串/converter/default/seed/DML 已验收；LOB/BYTE/更宽容量、索引、事务失败恢复仍待补齐 |
| 2026-11-17 | Oracle 普通字符串 CR/LF/CRLF 字面量 | 本模块；Unit 定向 108、Functional 12；完整两 patch 各 6628 通过/16 skip；LiveTests 两 patch 各 19 通过（9 live、10 offline）；r2 打包消费者通过 | 4000 字符值含全换行/重复 CRLF 和 CHAR 空格补齐比较已有证据；真实列写入、LOB、Schema 默认值/HasData 待验收 |
| 2026-11-17 | Oracle national literals、连接恢复 | 本模块；Unit 定向 74、Functional 6；完整两 patch 各 6588 通过/16 skip；打包消费者通过；LiveTests 两 patch 各 17 通过（7 个真实测试、10 个离线 profile 测试） | national SQL/literal 往返仅验证只读表达式；真实列容量、padding、LOB 和 Schema/DML 尚未验收 |
| 2026-11-16 | `DateTimeOffset.DateTime` | `c44f008`；Unit 3/3、Functional 3/3、Package 2/2 | Oracle native timezone/session 和 MySQL live rejection |
| 2026-11-16 | `DateTimeOffset.Ticks` | `a7aec5d` | Oracle native timezone/session |
| 2026-11-16 | `DateTimeOffset.Offset` | `a2c8e2c` | Oracle native interval/timezone |
| 2026-11-16 | `DateTimeOffset.TotalOffsetMinutes` | `1b0b295` | Oracle native extract/timezone |
| 2026-11-16 | Grouped results、DefaultIfEmpty、相关查询、Raw SQL、集合运算 | 对应提交和本地测试 | native query/result、完整 runtime families |
| 2026-11-15 及以前 | 关系写入、TPH/TPT/TPC、迁移、scaffolding、split、compiled/lazy、retry、HasData | 对应提交和本地/包证据 | native DDL/catalog/lock/failover/AOT 等 |

## 历史连接故障与恢复

2026-11-17 使用 User Secrets 对同一端点执行了 EF 和 direct Connector 的只读连接对比。两种模式的脱敏选项一致：TLS Disabled、OB20 关闭、Pooling 关闭、`UseAffectedRows=false`、连接/命令超时分别为 15/30 秒，并分别设置匹配的 `ExpectedMode`。TCP 端口可建立连接，但服务端未发送任何初始协议字节就关闭；direct Connector 在 `Use OB20=false/true` 下结果相同，EF 也收到 `Connection closed while reading the packet`。没有认证、服务端版本、SQL、DDL 或 reader 结果证据。这个事实把当前阻塞归类为端点服务/代理路由/访问策略或 Connector 待分类，不支持修改 Provider 连接逻辑。

2026-11-17 再次使用同一份 User Secrets，分别调用 `new OceanBaseConnection(原字符串).OpenAsync()`、带 EF 等价参数的 Connector `OpenAsync()`、EF `OpenConnectionAsync()`。两模式共六次均约 5 秒后失败：外层 `OceanBaseException`、内层 `ProtocolException`、`ServerErrorCode=0`、packet closure。EF 相比原配置仅增加 `ExpectedMode`，未修改 TLS/OB20/压缩/Pooling/超时。实际加载的 Connector DLL 与还原包的 SHA256 一致（`F4105BDC9D8B33A77F7223AC09265FAA497267ADD542812FC1763C9BF8AB0C55`）。临时 OB20 开关对照不属于生产配置修改；Connector 在认证后才启用 OB20 framing，不能据此解决初始 greeting 读取失败。

以上是恢复前的历史结果。2026-11-17 用户确认服务器故障并修复后，未更改 User Secrets，原 Connector、EF 等价 Connector 和 EF 的两模式六条路径均执行 SELECT 1 成功，握手阻塞已解除。官方连接探针修正了 Oracle `OceanBaseDecimal` 的测试端转换；它不是 Provider/驱动连接故障。

`LiveConnectionTests` 验证当前数据库/Schema 与配置匹配；`LiveReadOnlyQueryTests` 在两模式同步/异步执行过滤、参数刷新、NULL、Boolean、排序分页、聚合、分组、集合运算、相关 lateral 与 DefaultIfEmpty，每模式 72 条 EF reader 命令通过，服务端均报告 4.4.2.2。`LiveStringLiteralTests` 另验证 MySQL UTF8MB4 和 Oracle UTF8MB4/latin1 下的生成字面量、LINQ 常量比较与空串语义。测试未创建对象或执行业务写入，也未使用管理员账号；这些只读结果不关闭 DML/事务/DDL、完整映射或广泛 runtime specification 的缺口。

普通 Oracle CR/LF 字面量还复现了 EF 格式化把 LF 改成 CRLF 的数据变化。现在使用显式 CHAR(1) 的 CHR 片段拼接，保持配置的普通客户端编码和 CHAR 比较语义；裸 CHR 拼接会改变固定字段的空格补齐比较，未采用。UTF8MB4/latin1 只读测试涵盖 CR/LF/CRLF、Unicode/引号/反斜杠、4000 字符查询值及 CHAR(20) 比较；latin1 无法编码的普通 Unicode 值仍明确失败。

独立走查后补充的 4000 个 LF / 2000 组 CRLF 测试发现线性拼接触发服务端 600；direct Connector 对照证明同样的 CHAR 片段改成平衡分组即可成功。最终实现按相邻片段成对合并，避免线性表达式深度，并覆盖奇数片段保序。上述版本/包记录均指补充修复后的完整回归结果。

真实列验收使用普通账号和唯一 `EFT_` 表，仅在观察到 CREATE 成功后清理对应表；未使用管理员账号或创建/删除 namespace。MySQL 的 CONVERT 文本 DEFAULT 返回 5023，改为 `_utf8mb4 X'...'` 字符字面量后，默认值、HasData 和查询可共享同一表示。MySQL 生成值 SELECT 的 reader 现在消费至最终结果再释放，避免提前 Close 使 Connector 会话 Broken 后无法 commit。两处问题均有 RED/GREEN 和 direct Connector 对照，未修改 Connector 源码。

Oracle 的 RETURNING INTO 依赖 Connector 明确支持、默认关闭的 `Use Oracle Prepare Execute=true`；测试 profile 显式开启，未更改 User Secrets 或 Provider 全局连接策略。MySQL 同步/异步另覆盖 NO_BACKSLASH_ESCAPES 会话。第一次并行 live run 为 23/25，两项在握手阶段中断，其中一次是清理连接；已通过独立连接清理该自建表，随后串行运行 25/25。该瞬时连接结果不被归为 Provider 功能缺陷。

## 模块完成规则

- 先在功能矩阵确认 ID、范围和拒绝边界。
- 先写 RED，再做最小实现；覆盖 MySQL/Oracle、同步/异步、正常/NULL/边界/失败。
- 做调用者、服务注册、SQL、参数、reader、ownership 和异常恢复走查。
- 更新功能矩阵和缺失矩阵，记录提交、命令、通过数、skip 和风险。
- `Local`、`Package`、`Specification` 不能写成 `Live`；未执行 SQL/DDL 必须明确写出。
- 明确延期项不进入当前开发顺序。
