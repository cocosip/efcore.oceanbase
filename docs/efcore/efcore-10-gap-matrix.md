# EF Core 10 功能缺失矩阵

本文档只回答两个问题：还缺什么、由谁负责。Provider 功能和 Connector/服务端能力分开记录；Connector 能力由驱动项目实现，EF Provider 只使用其公共 ADO.NET API，不在这里重复实现驱动。

状态说明：`Provider` = EF 代码或 EF 测试仍需开发；`Driver evidence` = Connector 公共 API 需要补齐或验证；`Server/live` = 需要 OceanBase 环境、权限或版本证据；`Release` = 代码基本完成但还缺规格/包/版本审核；`Deferred` = 当前版本不做。

## A. 当前必须先完成

| ID | 缺失项 | 责任 | 当前事实 | 完成条件 |
| --- | --- | --- | --- | --- |
| `CFG-04`, `CON-02..05` | EF 连接对象、所有权、mode recheck、超时/取消后的租约恢复 | Provider + Driver evidence | Provider 有本地回归；2026-11-17 TCP 可达但端点未发送初始协议字节即关闭，direct Connector 与 EF 使用同一 User Secrets 和等价脱敏选项，`Use OB20` 两种取值均失败 | direct 成功而 EF 失败时修 Provider；当前两者都失败，先确认端点服务/代理路由/访问策略，再验证 wire/pool/timeout |
| `DIA-01..02`, `SQL-01` | 错误保留、日志脱敏、完整标识符/服务 facade 生命周期 | Provider | 本地命令和异常测试已有；live correlation/DDL 未完成 | 两模式同步/异步测试、走查、真实错误分类和 release 记录 |
| `TST-02` | 真实查询、写入、事务、Schema、标量 round-trip | Server/live + Provider | 2026-11-17 TCP 端口可达但 EF 与 direct Connector 两模式均在初始握手前失败，SQL/DDL/reader 均为 0 | 先完成只读 SELECT/身份/版本，再扩展写入、事务和 Schema；两模式各有结果 |
| `REL-01` | 支持环境、tenant、server、direct/proxy、TLS/transport 记录 | Server/live | User Secrets、endpoint、TLS Disabled、OB20/Pooling 关闭已记录；direct/proxy、server version 未确认 | 成功的非破坏探针 + 脱敏环境记录 |
| `REL-02`, `TST-01` | 关系型 runtime specification 和最终 revision 审核 | Provider + Release | 已有模型/非关系/服务/写 SQL/并发 profile；16 个 skip 已解释 | 补剩余关系型 families，在最终 revision 重跑全套 package/consumer/review |

## B. Provider 仍有开发或本地验收缺口

| ID | 功能 | 缺口 | 责任 |
| --- | --- | --- | --- |
| `MOD-01..03` | 模型约定、关系、生成/默认/计算值 | 更宽模型、生成值和 Schema 组合仍需覆盖 | Provider |
| `MAP-01..07` | 整数/Boolean、Decimal/Float、文本、Binary/Guid、Temporal、facet/converter | 精度、长度、LOB、Guid 字节序、时区、reader carrier、转换器组合仍需补齐 | Provider；native 结果另需 Server/live |
| `QRY-01..11` | 查询翻译和物化 | 剩余 overload、复杂组合、官方 runtime specification、真实排序/聚合/集合结果仍需补齐 | Provider + Server/live |
| `UPD-01..05` | INSERT/UPDATE/DELETE、生成值、并发、图保存 | 本地 SQL/double 已有，真实生成值、约束、冲突和提交结果未完成 | Provider + Server/live |
| `TXN-01..03` | transaction、savepoint、原子性、取消恢复 | 本地恢复路径已有，真实 commit/rollback/savepoint 未完成 | Provider + Server/live；句柄由 Connector 提供 |
| `SCH-01..03` | EnsureCreated/Deleted、权限、命名空间、DDL 清理 | 本地脚本和 ownership profile 已有，真实权限/DDL/锁/删除未完成 | Provider + Server/live |

## C. Connector 责任，不在 EF Provider 内实现

| 能力 | EF Provider 的边界 | 需要的 Connector 证据/能力 |
| --- | --- | --- |
| 协议、认证、TLS、Proxy/OB20、压缩 | Provider 只传递连接字符串和调用 `DbConnection.Open/OpenAsync` | Connector direct/proxy/TLS/认证矩阵和错误保留 |
| 连接池、物理连接关闭、wire timeout/cancellation | Provider 负责 EF lease、调用方/Provider ownership 和取消 token 传递 | Connector 证明物理 close/reuse、wire cancel、timeout 和 pool cleanup |
| 参数 codec、DbType、OceanBaseDecimal、时间/interval、LOB reader | Provider 选择 EF mapping、参数 metadata 和 reader 适配 | Connector 提供公共 parameter/reader 类型和无损 codec |
| 原生 transaction 句柄 | Provider 负责 EF transaction/savepoint 编排 | Connector 负责 `DbTransaction` 的 commit/rollback/dispose 行为 |
| 服务端 SQL、类型、权限、DDL、生成值 | Provider 生成/绑定 SQL 并记录预期；不模拟服务端 | OceanBase 版本/模式/权限/执行结果的真实矩阵 |

当前 packet closure 不足以判断 Connector 缺陷。先做 direct Connector 与 EF Provider 的同参数对比：direct 成功而 EF 失败，归 Provider；两者都失败，记录为环境/服务端或 Connector 待分类，不在 EF 内绕过驱动。

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
