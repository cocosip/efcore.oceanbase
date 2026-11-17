# EF Core 10 功能缺失矩阵

本文档只回答两个问题：还缺什么、由谁负责。Provider 功能和 Connector/服务端能力分开记录；Connector 能力由驱动项目实现，EF Provider 只使用其公共 ADO.NET API，不在这里重复实现驱动。

状态说明：`Provider` = EF 代码或 EF 测试仍需开发；`Driver evidence` = Connector 公共 API 需要补齐或验证；`Server/live` = 需要 OceanBase 环境、权限或版本证据；`Release` = 代码基本完成但还缺规格/包/版本审核；`Deferred` = 当前版本不做。

## A. 当前必须先完成

| ID | 缺失项 | 责任 | 当前事实 | 完成条件 |
| --- | --- | --- | --- | --- |
| `CFG-04`, `CON-02..05` | EF 连接对象、所有权、mode recheck、超时/取消后的租约恢复 | Provider + Driver evidence | 2026-11-17 服务器恢复后，同一 User Secrets 下两模式的原 Connector、EF 等价 Connector 和 EF 六条路径 SELECT 1 均通过；握手阻塞已解除 | 继续物理 close/reuse、真实 timeout/cancel 和恢复验收；不新增 proxy/OB20 要求 |
| `DIA-01..02`, `SQL-01` | 错误保留、日志脱敏、完整标识符/服务 facade 生命周期 | Provider | 本地命令和异常测试已有；live correlation/DDL 未完成 | 两模式同步/异步测试、走查、真实错误分类和 release 记录 |
| `TST-02` | 真实查询、写入、事务、Schema、标量 round-trip | Server/live + Provider | 两模式身份探针和现有只读查询 profile 各 72 条 EF reader 命令通过；national 字面量 UTF8MB4/Oracle latin1 同步/异步往返通过 | 更宽标量/查询及隔离目标上的 DML、事务、Schema 仍待真实验收 |
| `REL-01` | 普通直连支持环境、tenant、server、TLS/transport 记录 | Server/live | 同一 User Secrets 普通直连成功；两模式服务端均报告 4.4.2.2，当前库/Schema 与配置一致；TLS Disabled、OB20/压缩/Pooling 关闭 | 当前只读环境证据已有，后续 DML/事务/DDL 需记录各自权限和部署证据；扩展部署不作为前置条件 |
| `REL-02`, `TST-01` | 关系型 runtime specification 和最终 revision 审核 | Provider + Release | 已有模型/非关系/服务/写 SQL/并发 profile；16 个 skip 已解释 | 补剩余关系型 families，在最终 revision 重跑全套 package/consumer/review |

## B. Provider 仍有开发或本地验收缺口

| ID | 功能 | 缺口 | 责任 |
| --- | --- | --- | --- |
| `MOD-01..03` | 模型约定、关系、生成/默认/计算值 | 更宽模型、生成值和 Schema 组合仍需覆盖 | Provider |
| `MAP-01..07` | 整数/Boolean、Decimal/Float、文本、Binary/Guid、Temporal、facet/converter | Oracle national 字面量已用 ASCII UTF-16 escapes 修复，默认/HasData/converter 本地测试及 UTF8MB4/latin1 只读往返通过；精度、列容量/padding/LOB、Guid 字节序、时区及更宽转换组合仍需补齐 | Provider + Server/live；见 [national literals](efcore-10-national-literals-plan.md) |
| `QRY-01..11` | 查询翻译和物化 | 剩余 overload、复杂组合、官方 runtime specification、真实排序/聚合/集合结果仍需补齐 | Provider + Server/live |
| `UPD-01..05` | INSERT/UPDATE/DELETE、生成值、并发、图保存 | 本地 SQL/double 已有，真实生成值、约束、冲突和提交结果未完成 | Provider + Server/live |
| `TXN-01..03` | transaction、savepoint、原子性、取消恢复 | 本地恢复路径已有，真实 commit/rollback/savepoint 未完成 | Provider + Server/live；句柄由 Connector 提供 |
| `SCH-01..03` | EnsureCreated/Deleted、权限、命名空间、DDL 清理 | 本地脚本和 ownership profile 已有，真实权限/DDL/锁/删除未完成 | Provider + Server/live |

## C. Connector 责任，不在 EF Provider 内实现

| 能力 | EF Provider 的边界 | 需要的 Connector 证据/能力 |
| --- | --- | --- |
| 普通直连协议、认证及错误保留 | Provider 只传递连接字符串、设置 ExpectedMode 并调用 `DbConnection.Open/OpenAsync` | 使用 Connector 0.3.0 已有公共 API，不在 EF 内新增协议 |
| Proxy/ODP、多节点/OCP/TNS、TLS、OB20/trace、压缩 | 以 Connector 对应 revision 的支持与验收范围为上限；不作为 EF10 普通直连前置任务 | 真实扩展部署暂不开发或未验收；compressed OB20/proxy reroute 明确排除；详见进度文档中的 Connector 能力边界 |
| 连接池、物理连接关闭、wire timeout/cancellation | Provider 负责 EF lease、调用方/Provider ownership 和取消 token 传递 | Connector 证明物理 close/reuse、wire cancel、timeout 和 pool cleanup |
| 参数 codec、DbType、OceanBaseDecimal、时间/interval、LOB reader | Provider 选择 EF mapping、参数 metadata 和 reader 适配 | Connector 提供公共 parameter/reader 类型和无损 codec |
| 原生 transaction 句柄 | Provider 负责 EF transaction/savepoint 编排 | Connector 负责 `DbTransaction` 的 commit/rollback/dispose 行为 |
| 服务端 SQL、类型、权限、DDL、生成值 | Provider 生成/绑定 SQL 并记录预期；不模拟服务端 | OceanBase 版本/模式/权限/执行结果的真实矩阵 |

先前 packet closure 在用户修复服务器后已解除，不是已证实的 Connector 缺陷。后续仍按同参数对比归责：direct 成功而 EF 失败才查 Provider 差异；两者都失败先保留分类证据，不在 EF 内绕过驱动。

连接不可用只保留依赖 live 的验收缺口；按开发队列继续本地实现和测试。Connector 未支持、排除或冻结的能力不转化为 EF 缺陷，也不成为所有 Provider 开发的停止条件。

## D. 明确延期

| ID | 功能 | 原因/边界 |
| --- | --- | --- |
| `DEF-04` | JSON、primitive collections/arrays、complex types | 当前版本不做 mapping/query/update profile |
| `DEF-05` | Spatial、XML、native enum、OBJECT、LOB locator、REF CURSOR | 需要独立 Connector capability 和 native mapping 设计 |
| `DEF-06` | stored-procedure modification mappings、sequence/HiLo | 参数/生成值契约未纳入当前 release |
| `DEF-10` | full-text/vector/search、advanced batching | 需要服务端版本契约和独立 benchmark |
| `DEF-01/02/03/07/08/09` | migrations/tooling、bulk、inheritance、split/compiled/lazy、seeding/retry | 有部分本地实现，native/组合/规格证据未完，不得宣传完整支持 |

## E. 缺失项关闭规则

每关闭一项，必须在 [功能矩阵](efcore-10-feature-matrix.md) 更新 MySQL/Oracle 状态，在 [开发进度](efcore-10-progress.md) 写提交和验证，在本矩阵删除或改写缺口；Connector 责任只能记录公共 API/证据，不把驱动代码混入 Provider commit。
