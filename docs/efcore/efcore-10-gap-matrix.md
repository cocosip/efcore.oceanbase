# EF Core 10 功能缺失矩阵

本文档只回答两个问题：还缺什么、由谁负责。Provider 功能和 Connector/服务端能力分开记录；Connector 能力由驱动项目实现，EF Provider 只使用其公共 ADO.NET API，不在这里重复实现驱动。

状态说明：`Provider` = EF 代码或 EF 测试仍需开发；`Driver evidence` = Connector 公共 API 需要补齐或验证；`Server/live` = 需要 OceanBase 环境、权限或版本证据；`Release` = 代码基本完成但还缺规格/包/版本审核；`Deferred` = 当前版本不做。

## A. 当前必须先完成

| ID | 缺失项 | 责任 | 当前事实 | 完成条件 |
| --- | --- | --- | --- | --- |
| `CFG-04`, `CON-02..05` | EF 连接对象、所有权、mode recheck、超时/取消后的租约恢复 | Provider + Driver evidence | 2026-11-17 服务器恢复后，同一 User Secrets 下两模式的原 Connector、EF 等价 Connector 和 EF 六条路径 SELECT 1 均通过；握手阻塞已解除 | 继续物理 close/reuse、真实 timeout/cancel 和恢复验收；不新增 proxy/OB20 要求 |
| `DIA-01..02`, `SQL-01` | 错误保留、日志脱敏、完整标识符/服务 facade 生命周期 | Provider | 本地命令和异常测试已有；live correlation/DDL 未完成 | 两模式同步/异步测试、走查、真实错误分类和 release 记录 |
| `TST-02` | 真实查询、写入、事务、Schema、标量 round-trip | Server/live + Provider | 两模式身份探针和现有只读查询 profile 各 72 条 EF reader 命令通过；national 字面量 UTF8MB4/Oracle latin1 同步/异步往返通过 | 已补两模式唯一表的 bounded text/default/seed/tracked DML/清理；更宽标量/查询、事务失败恢复和 Schema 生命周期仍待验收 |
| `REL-01` | 普通直连支持环境、tenant、server、TLS/transport 记录 | Server/live | 同一 User Secrets 普通直连成功；两模式服务端均报告 4.4.2.2，当前库/Schema 与配置一致；TLS Disabled、OB20/压缩/Pooling 关闭 | 当前连接/只读和唯一文本表 DML/DDL 证据已有；更宽事务/Schema profile 仍需各自权限和部署证据；扩展部署不作为前置条件 |
| `REL-02`, `TST-01` | 关系型 runtime specification 和最终 revision 审核 | Provider + Release | 已有模型/非关系/服务/写 SQL/并发 profile；16 个 skip 已解释 | 补剩余关系型 families，在最终 revision 重跑全套 package/consumer/review |

## B. Provider 仍有开发或本地验收缺口

| ID | 功能 | 缺口 | 责任 |
| --- | --- | --- | --- |
| `MOD-01..03` | 模型约定、关系、生成/默认/计算值 | 更宽模型、生成值和 Schema 组合仍需覆盖 | Provider |
| `MAP-01..07` | 整数/Boolean、Decimal/Float、文本、Binary/Guid、Temporal、facet/converter | Oracle national 字面量及普通字面量 CR/LF 保真已修复，default/HasData/converter 本地测试和 UTF8MB4/latin1 只读往返通过；普通值 4000 字符、CHAR 比较及不可编码值拒绝已有证据。真实 bounded text 参数写入、NULL/空串、padding/converter/default/seed 已验收，MySQL DEFAULT 字面量与生成值 reader 消费已修复；已补 AL32UTF8 BYTE 边界/12899 与 ordinary CLOB/MySQL LONGTEXT 大值/converter；NCLOB DDL 在目标 4.4.2.2 返回 902（direct 对照一致），不归为驱动/Provider 缺陷。数值模块已补两模式整数八类型端点/enum、Boolean 九组逻辑、默认 Decimal 极值/scale-28、显式精度/拒绝和 Single 端点；MySQL Double 端点通过。Oracle Single 字面量 1426 已修复；Double 参数极值 1426、微小值变零在 direct Connector 同样复现，完整 Double 绑定仍未验收。新增 [声明/精度回归](efcore-10-oracle-double-declarations-plan.md) 已核实默认/显式 BINARY_DOUBLE 的目录与实际存储：D 字面量可保真，相同参数极值失败/微小值丢失；Precision17 无改善。NUMBER(18,6) 正常按 scale 舍入，decimal converter 仅接受受限精确域，不能解决完整 Double 范围。更宽精度、full capacity/national LOB/其他 charset、时区和转换组合仍需补齐；Binary/Guid 模块已补最大 RAW/VARBINARY、padding、大 BLOB、mixed-endian Guid 服务端字节、default/seed/converter。Oracle 空 BLOB 原有保守拒绝已修复为参数保真及 EMPTY_BLOB() 字面量，RAW 空值仍拒绝；MySQL LOB DEFAULT 在目标上返回 1067。Temporal 模块已补五 CLR 类型/default/seed/generated/converter/update、精度拒绝、fractional comparands/CASE/原值冲突、Oracle exact wrapper 拒绝与 numeric-offset 会话验收；MySQL TIME 上界修正为精确的 ±838:59:59，避免原先额外小数导致的丢值。更宽日期运算、命名时区/DST、其他声明和部署仍开放；见 [Temporal storage](efcore-10-temporal-storage-plan.md) | Provider + Connector/server boundary；见 [binary/Guid storage](efcore-10-binary-guid-storage-plan.md) / [numeric storage](efcore-10-numeric-storage-plan.md) / [national](efcore-10-national-literals-plan.md) / [ordinary](efcore-10-ordinary-literals-plan.md) literals |
| `QRY-01`, `QRY-03`, `QRY-06..11` | 查询翻译和物化 | `QRY-02/05` 已关闭。新增 [组合实库验收](efcore-10-relational-query-plan.md) 覆盖 Join/相关分页与集合 shaping、Any/All、NULL membership、20 类 typed root 和四种集合运算；Oracle UNION 公共 carrier/charset、TO_BLOB 及窄 CAST 截断已修复。普通 BLOB 比较/IN（含 mixed RAW item/BLOB subquery）执行前明确拒绝，RAW 有正向证据；Oracle contextual CHAR/NCHAR roots 暂明确拒绝，避免改变定长空格比较。MySQL 与 bounded/fixed 属性关联的 text root 在当前环境返回 1267，仍需独立 collation 方案及回归；`QRY-03` 不因此全量完成。更宽 overload/转换、图/raw、官方 runtime families 和其他部署仍待验收 | Provider + Server/live |
| `QRY-04`（MySQL） | 默认 Decimal 聚合结果 | [聚合实库模块](efcore-10-aggregate-query-plan.md) 两 patch 全套 LiveTests 各 267/267（257 live、10 offline），Oracle QRY-04 已关闭。可空 Single 转换器的内部 marker 类型校验已有 RED/GREEN 修复。MySQL 默认 DECIMAL(65,28) 的 SUM/AVG 在 Connector 0.3.0 公开 reader 中被 metadata 校验拒绝，SUM 的空集/全 NULL 补零结果也受影响；相同 SQL 的文本诊断投影能读取正确服务端值，显式 (18,4) 仅证明该声明域。默认 mapping 未缩窄、未改为文本传输；内部根因未归责。独立走查的一项复合键 fixture 次要缺口见模块记录 | Connector/server result boundary + Provider regression |
| `UPD-01..05` | INSERT/UPDATE/DELETE、生成值、并发、图保存 | 两模式 bounded text 的 tracked insert/update/delete、默认值回读已通过；MySQL 回读完整消费后 commit 成功，Oracle 显式开启 Connector PrepareExecute。更宽生成值/约束/冲突/图保存未完成 | Provider + Server/live |
| `TXN-01..03` | transaction、savepoint、原子性、取消恢复 | 本地恢复路径已有，MySQL 文本默认值回读的 owned commit 已通过；更宽 commit、rollback/savepoint/失败恢复仍待验收 | Provider + Server/live；句柄由 Connector 提供 |
| `SCH-01..03` | EnsureCreated/Deleted、权限、命名空间、DDL 清理 | 唯一自建表的 CreateTables、HasData 和精确清理已有 native 证据；更宽权限/namespace/锁/删除仍未完成 | Provider + Server/live |

## C. Connector 责任，不在 EF Provider 内实现

| 能力 | EF Provider 的边界 | 需要的 Connector 证据/能力 |
| --- | --- | --- |
| 普通直连协议、认证及错误保留 | Provider 只传递连接字符串、设置 ExpectedMode 并调用 `DbConnection.Open/OpenAsync` | 使用 Connector 0.3.0 已有公共 API，不在 EF 内新增协议 |
| Proxy/ODP、多节点/OCP/TNS、TLS、OB20/trace、压缩 | 以 Connector 对应 revision 的支持与验收范围为上限；不作为 EF10 普通直连前置任务 | 真实扩展部署暂不开发或未验收；compressed OB20/proxy reroute 明确排除；详见进度文档中的 Connector 能力边界 |
| 连接池、物理连接关闭、wire timeout/cancellation | Provider 负责 EF lease、调用方/Provider ownership 和取消 token 传递 | Connector 证明物理 close/reuse、wire cancel、timeout 和 pool cleanup |
| Oracle Double 参数范围/无损绑定 | Provider 仍绑定公开 Double carrier，不在 EF 内改协议或静默换成文本；MAP-02 保持未完成 | 4.4.2.2 + Connector 0.3.0 direct/显式 Prepare 对照：极值 1426，测试微小值读写变零；需继续区分 codec/服务端行为，不能宣称完整 Double 支持 |
| MySQL 默认 Decimal SUM/AVG 结果 | Provider 保留默认精度与 native aggregate，不静默缩窄声明或修改协议 | 4.4.2.2 + Connector 0.3.0：DECIMAL(65,28) 的 SUM/AVG 在 GetValue/GetProviderSpecificValue/GetFieldValue<OceanBaseDecimal> 均拒绝 metadata；MIN/MAX、显式 (18,4) 和文本诊断对照通过。QRY-04/MySQL 保持未完成 |
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
