# EF Core 10 开发进度

本文档是开发队列和当前检查点。完整范围看 [功能矩阵](efcore-10-feature-matrix.md)，责任归属看 [功能缺失矩阵](efcore-10-gap-matrix.md)。

## 当前检查点：2026-11-17

| 项目 | 结果 |
| --- | --- |
| 分支 / 提交 | `efcore/10.0` / `f32349d` |
| 工作区 | 干净 |
| 最新模块 | `DateTimeOffset.DateTime` |
| Required 状态 | 每个模式 9 Done / 44 In progress |
| Local Release | Unit 2546、Functional 2469、Specification 1538；16 个既有映射 skip |
| Package | Consumer 2/2；EF10.0.0/10.0.14、MySQL/Oracle 两模式通过 |
| Native | EF 与 direct Connector 两模式均在握手阶段失败，未执行 SQL/DDL，reader 为 0 |

## 固定开发顺序

| 顺序 | 模块 | ID | 下一步交付物 |
| --- | --- | --- | --- |
| 0 | EF 连接路径和诊断 | `CFG-04`, `CON-02..05`, `DIA-01..02`, `SQL-01` | 对照 Connector 普通直连契约；只修有证据的 Provider 差异；补同步/异步 lease 回归 |
| 1 | 真实只读验收 | `TST-02`, `REL-01` | 两模式 SELECT、身份、版本和错误分类；成功后才扩展写入 |
| 2 | 映射剩余边界 | `MOD-01..03`, `MAP-01..07` | 精度/长度/LOB/Guid/时区/转换器边界和必要 live round-trip |
| 3 | 查询翻译和物化 | `QRY-01..11` | 剩余 overload、组合、runtime specifications 和真实结果 |
| 4 | 写入和并发 | `UPD-01..05` | 真实 DML、生成值回读、并发冲突和约束 |
| 5 | 事务恢复 | `TXN-01..03`, `DEF-09` | 真实 commit/rollback/savepoint/取消原子性 |
| 6 | Schema 生命周期 | `SCH-01..03` | 隔离目标上的 DDL、权限、删除、锁和清理 |
| 7 | 规格和发布 | `TST-01`, `REL-02`, `PKG-01..04` | 关系 runtime families、最终 revision 和包矩阵 |

按顺序完成各模块的本地实现、RED/green、同步/异步、两模式、代码走查、适用的 full/package 检查和本地 commit。外部连接不可用时，保留对应 live 验收缺口并继续下一模块的本地工作；不得将缺少 live 证据的行改为 Done，也不得因此冻结全部 Provider 开发。

## Connector 能力边界

当前 EF 使用 `OceanBaseConnector` 0.3.0 包的公共 ADO.NET API，验收基线是 MySQL/Oracle 普通协议直连。包声明的源码 revision 为 `7f0cf7e544bf8b944d584ddf9116bea03ec4c5df`；能力边界以该 revision 的 [Connector compatibility](https://github.com/cocosip/obconnector-dotnet/blob/7f0cf7e544bf8b944d584ddf9116bea03ec4c5df/docs/compatibility.md) 为准。

- Connector 历史直连证据针对 OceanBase 4.4.2.2、TLS Disabled；这不是本次 EF 成功连接或当前服务端版本的证据。
- 真实 OBProxy/ODP、多节点/OCP/TNS、backend migration、OB20/trace 和真实 TLS 等扩展部署尚未验收或暂不开发，不作为 EF10 普通直连的前置任务。
- compressed OB20、proxy reroute 和透明会话迁移等排除项不进入 EF Provider 的开发清单；存在本地协议代码或连接字符串选项，不表示实际部署已受支持。

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

2026-11-17 使用 User Secrets 对同一端点执行了 EF 和 direct Connector 的只读连接对比。两种模式的脱敏选项一致：TLS Disabled、OB20 关闭、Pooling 关闭、`UseAffectedRows=false`、连接/命令超时分别为 15/30 秒，并分别设置匹配的 `ExpectedMode`。TCP 端口可建立连接，但服务端未发送任何初始协议字节就关闭；direct Connector 在 `Use OB20=false/true` 下结果相同，EF 也收到 `Connection closed while reading the packet`。没有认证、服务端版本、SQL、DDL 或 reader 结果证据。这个事实把当前阻塞归类为端点服务/代理路由/访问策略或 Connector 待分类，不支持修改 Provider 连接逻辑。

2026-11-17 再次使用同一份 User Secrets，分别调用 `new OceanBaseConnection(原字符串).OpenAsync()`、带 EF 等价参数的 Connector `OpenAsync()`、EF `OpenConnectionAsync()`。两模式共六次均约 5 秒后失败：外层 `OceanBaseException`、内层 `ProtocolException`、`ServerErrorCode=0`、packet closure。EF 相比原配置仅增加 `ExpectedMode`，未修改 TLS/OB20/压缩/Pooling/超时。实际加载的 Connector DLL 与还原包的 SHA256 一致（`F4105BDC9D8B33A77F7223AC09265FAA497267ADD542812FC1763C9BF8AB0C55`）。临时 OB20 开关对照不属于生产配置修改；Connector 在认证后才启用 OB20 framing，不能据此解决初始 greeting 读取失败。

真实连接验收恢复条件：

1. 在 Connector 已支持的普通直连配置下，确认 `172.16.2.45:2881` 的 greeting 关闭原因；当前证据只能定位到 Connector 初始握手读取边界，不能判定具体服务端/网络原因或 Connector 缺陷，不要求新增 proxy/OB20 功能。
2. 在端点或 transport 修正后重新执行两模式只读探针；成功后记录身份/版本并进入 `TST-02`。
3. 此期间继续文档范围内的本地 Provider 开发；连接失败只阻塞依赖真实数据库的验收，不替代功能实现、测试和代码走查。

## 模块完成规则

- 先在功能矩阵确认 ID、范围和拒绝边界。
- 先写 RED，再做最小实现；覆盖 MySQL/Oracle、同步/异步、正常/NULL/边界/失败。
- 做调用者、服务注册、SQL、参数、reader、ownership 和异常恢复走查。
- 更新功能矩阵和缺失矩阵，记录提交、命令、通过数、skip 和风险。
- `Local`、`Package`、`Specification` 不能写成 `Live`；未执行 SQL/DDL 必须明确写出。
- 明确延期项不进入当前开发顺序。
