# EF Core 10 发布工作板

本文档是 `efcore/10.0` 的执行入口。功能定义仍以
[feature checklist](efcore-10-feature-checklist.md) 为准，本文档把当前状态、下一步和验收证据压缩成
可执行的工作板。每次只推进一个模块；模块完成后必须测试、走查、更新文档并提交，再开始下一模块。

## 当前基线

- 分支：`efcore/10.0`
- Target Framework：`net10.0`
- EF Core：`[10.0.0,11.0.0)`
- Connector：`OceanBaseConnector 0.3.0`
- 模式：MySQL、Oracle
- Required 行：53 行；两种模式均为 9 行 `Done`、44 行 `In progress`
- 已完成 Required 行：`CFG-01..03`、`CON-01`、`SQL-02`、`PKG-01..04`
- 当前本地模块：relationship-change writes（base `12b44e7`，提交见 `git log`），未推送

最近一次完整本地证据：

| 检查 | 结果 | 证据边界 |
| --- | --- | --- |
| Release Unit | 2421 通过 | Provider 单元和本地 doubles |
| Release Functional | 2121 通过 | 拦截式同步/异步命令管线 |
| Release Specification | 1538 通过，16 个明确跳过 | 跳过已映射到 `MAP-01/03/05/06`；本轮无新增跳过 |
| Debug Unit / Functional | 2421 / 2121 通过 | 本地源代码；Specification 1538 + 16 mapped skips，ConsumerTests 2；合计 6082 通过 |
| EF10.0.14 isolated Release | Unit 2421、Functional 2121、Specification 1538、ConsumerTests 2 通过 | 16 mapped skips；Connector 0.3.0；最终走查断言后，三个 Functional 组合均重跑 2121/2121 |
| Relationship-change writes | 聚焦 36/36、关联 460/460 | 可选 shadow FK 断开、ClientSetNull、必需关系重挂接/孤儿时机、一对一替换顺序、接受和后续失败/取消/savepoint 恢复；仅测试和文档变更；走查的开连接前拒绝断言已补强；[模块记录](efcore-10-relationship-writes-plan.md) |
| TPH runtime verification | 聚焦 78/78、关联 362/362；最终断言纳入全量门禁 | 现有单表继承查询/物化/CRUD/图保存/恢复链路；仅测试和文档变更；SQL/绑定断言经独立走查补强；[模块记录](efcore-10-tph-runtime-plan.md) |
| SaveChanges concurrency interception | 聚焦 52/52、关联 186/186；初始 RED 28 失败/8 通过 | 标准诊断、同步/异步 hook 和显式抑制；保留异常/entries/token，跳过旧输出并继续后续 batch；独立走查无待修问题；[模块记录](efcore-10-save-concurrency-interception-plan.md) |
| Current package/runtime | 复用 `10.0.0-save-concurrency-interception-20261116` 通过 | 新关系替换/断开 smoke、包内容和两补丁/两模式 consumer 通过；graph 和 archive/consumer DLL hash 匹配。当前源代码重编译因提交版本戳不同而 hash 不同；复用来源见 relationship-write 记录。无新 CLI/bundle/hosted/Linux/native evidence |
| Concurrency-protection specifications | 56/56；临时关闭检查时 20/20 敏感性用例失败，恢复后通过 | 真实 Provider 服务，开连接前拒绝；两处本地适配已记录；走查修正显式 true 掩盖默认值问题；仅测试变更，无新包或 native evidence |
| Write SQL specifications/package | Specification 30/30、关联 runtime 134/134；`10.0.0-update-sql-specification-20261116` 通过 | 官方写入 SQL 家族，两模式三处明确适配；sequence 入口一致拒绝；独立走查、包内容和两补丁/两模式 consumer、graph/hash 校验通过 |
| DateOnly calendar correction/package | Unit 7、Functional 8 通过；`10.0.0-date-only-calendar-fix-20261116` 通过 | 修复 Oracle 月末规则和年数优先级；独立走查无阻塞；包内容、两补丁/两模式 consumer、graph/hash 校验通过；native acceptance 未完成 |
| DateTime calendar-add module/package | Unit 11、Functional 8 通过；`10.0.0-date-time-calendar-add-20261116` 通过 | 走查修复年数优先级和 Oracle DATE 结果类型；包内容、两 EF 补丁/两模式 consumer、graph/hash 校验通过；Oracle 需要 Gregorian session；native acceptance 未完成 |
| server clock package | `10.0.0-date-time-clocks-20261116` 通过 | 包内容、EF10.0.0/10.0.14、两模式 consumer |
| DateTimeOffset member focused tests | Unit 8/8、Functional 8/8 通过 | 两模式、同步/异步、MySQL TimeSpan/Oracle interval reader；包内容校验通过 |
| DateTimeOffset.UtcDateTime focused tests | Unit 4/4、Functional 4/4 通过 | 谓词服务器翻译、两模式同步/异步 reader、carrier rejection；EF10.0.0 packed consumer 两模式通过（隔离 NuGet 缓存） |
| DateTimeOffset.UtcNow focused tests | Unit 2/2、Functional 4/4 通过 | 两模式同步/异步、server clock predicate/materialization、零 offset；包内容和 EF10.0.0/10.0.14 packed consumer 通过 |
| DateOnly.AddDays focused tests | Unit 4/4、Functional 4/4 通过 | 两模式同步/异步、MySQL/Oracle SQL、负向 carrier 边界；包内容和 EF10.0.0/10.0.14 packed consumer 通过 |
| DateOnly.AddMonths focused tests | Unit 4/4、Functional 4/4 通过 | 两模式同步/异步、MySQL/Oracle SQL、负向 carrier 边界；包内容和 EF10.0.0/10.0.14 packed consumer 通过 |
| DateOnly.AddYears focused tests | Unit 4/4、Functional 4/4 通过 | 两模式同步/异步、MySQL/Oracle SQL、负向 carrier 边界；包内容和 EF10.0.0/10.0.14 packed consumer 通过 |
| `MinNumber`/`MaxNumber` focused tests | Unit 47/47、Functional 4/4 通过 | `float`/`double` exact overloads, both modes, sync/async intercepted SQL/materialization; native non-finite semantics remain open |
| `DateTimeOffset.UtcTicks` focused tests | Unit 4/4、Functional 4/4 通过 | 两模式、同步/异步、UTC Gregorian ticks、NULL 和 carrier rejection；native timezone/session semantics remain open |

这些结果不能关闭真实 OceanBase 验收。当前提供的端点在认证前报
`Connection closed while reading the packet`，没有执行 SQL 或 DDL。

## 已完成和部分完成的功能

以下模块已经有本地代码、测试和包证据，但除非 checklist 行变为 `Done`，不得对外宣称完整支持：

| 模块 | 当前本地结果 | 尚未关闭的边界 |
| --- | --- | --- |
| 配置、模式和缓存 | `CFG-01..03` 完成 | `CFG-04` 的完整 facade/schema/native 生命周期 |
| Connector 连接边界 | `CON-01` 完成；`CON-02..05` 有清理、所有权、取消和重用回归 | 真实 wire、池化、超时和物理连接行为 |
| 标量映射 | 整数、Boolean、Decimal、浮点、文本、Binary/Guid、五类 temporal 已有大量本地覆盖 | 完整范围、BYTE/字符容量、LOB、时区/精度、真实 round-trip |
| 查询翻译 | 字符串、集合根、聚合、分页、Include、split、布尔、数学、时间成员已有多个切片 | 完整 overload、转换器组合、原生排序/UNION/结果语义 |
| 写入和事务 | 图写入、关系断开/重挂接/孤儿/一对一替换、生成值、并发、Savepoint、取消、retry 有本地 doubles | 真实提交/回滚/生成值/并发、约束、引用动作和 failover |
| Schema 和继承 | 迁移核心/tooling、TPH/TPT/TPC、拆分、HasData 有本地及 packed 证据 | 真实 DDL、权限、锁、删除、继承运行结果和部分失败恢复 |
| Lazy loading/compiled model/split query | 普通 EF/Proxies/ILazyLoader/compiled model/split query 已覆盖 | 原生 session/isolation、组合场景、AOT、上游 runtime fixtures |
| Specification | 非关系模型 330、ModelBuilding101 248、服务注册 6、write SQL 30、并发保护 profile/controls 56 通过，16 个排除已解释 | 更广 store-backed 关系查询/写入/事务/runtime 家族；写入 SQL 三处、并发保护两处适配见 exclusions |

## Required 缺口清单

### 连接、服务和诊断：`CFG-04`、`CON-02..05`、`DIA-01..02`、`SQL-01`

需要补齐服务 facade 的完整生命周期、真实打开连接后的模式冲突清理、调用方/Provider 所有权、同步/异步
取消和超时、日志脱敏及异常内层原因。离线测试必须覆盖成功、失败、取消、重试、替换连接和 cleanup
failure；真实连接仍由 `TST-02` 单独验收。

### 模型和映射：`MOD-01..03`、`MAP-01..07`

需要完成以下具体边界：

- 八种整数类型、Boolean 三值逻辑、最小/最大值、溢出和 native reader carrier。
- Decimal/Single/Double 的精度、scale、`REAL`/别名提升、非有限值和转换器组合。
- Unicode、BYTE、fixed/variable、字符/字节长度、索引容量、普通 LOB 和 Oracle 空字符串。
- Binary/Guid 的空值、长度、CHAR/RAW/BINARY/BLOB、字节序和 generated output。
- DateTime/DateTimeOffset/DateOnly/TimeOnly/TimeSpan 的精度、时区、范围、interval、NULL、literal、reader 和生成值。
- converter、clone、property facet 和 direct mapping 的一致性；实际写入容量检查不能误伤查询参数。

### 查询和物化：`QRY-01..11`

需要把每个 translator 家族的支持 overload 固化为正面、负面和同步/异步结果测试，重点包括：

- Boolean/NULL 三值语义、join/subquery/Any/All/Contains 和集合 root 的所有参数策略。
- OrderBy/Distinct/Skip/Take、First/Single/default、Include/ThenInclude、tracking 和 identity resolution。
- GroupBy、Count、Sum、Average、Min/Max 的 empty、all-null、nullable、converter 和 grouping 结果。
- 字符串、时间、数学、conditional/coalesce 以及 `DateTimeOffset` 尚未定义的成员语义。
- Raw SQL/ExecuteSql 的参数绑定和明确的 unsupported translation 错误。

### 写入、事务和 Schema：`UPD-01..05`、`TXN-01..03`、`SCH-01..03`

需要完成 generated/default/computed readback、复合并发、图保存顺序、事务加入、Savepoint、取消恢复、
retry 禁止条件、错误分类、拥有边界、EnsureCreated/EnsureDeleted/Migrate/HasData 的真实行为。DDL 不是
原子的事实、部分创建后的恢复责任和 destructive 测试清理范围必须写入验收记录。

### 规格和发布：`TST-01`、`TST-02`、`REL-01..02`

需要继续加入关系型 specification query/write/runtime 家族，并把每个排除项映射到 checklist ID；随后在
声明的 MySQL/Oracle tenant 上运行非破坏 scalar/query/write/transaction/schema 矩阵，补齐支持环境记录，
最后在确切 release revision 上重跑 Debug/Release、EF patch、package、consumer、tooling/bundle 和
`git diff --check`。

## 固定开发顺序

本轮补齐关系变更的本地写入/顺序/接受/恢复证据，参见
[module plan](efcore-10-relationship-writes-plan.md)。现有 Provider/EF 实现通过完整 profile，无生产代码变更。
剩余 store-backed runtime 家族仍需正确的存储夹具；后续按完整模块推进 Required 查询/写入/runtime 缺口，
避免重复已接入家族和零散新增 temporal API。真实环境和发布门槛保持开放。

1. **一个本地模块**：先写该模块的行为表、RED 测试和负面边界，再改 Provider 代码。
2. **本地闭环**：两种模式、同步/异步、正常/NULL/空值/边界/失败路径全部通过；需要时补 packed consumer。
3. **代码走查**：检查调用者、服务注册、mapping/converter、SQL 生成、资源所有权和错误恢复；确认没有把
   intercepted evidence 写成 live claim。
4. **全量门禁**：Debug/Release、Specification、选定 EF10 patch、包内容和 consumer；设计时工具受影响时
   还要运行 ToolingConsumer。
5. **提交并更新工作板**：记录提交、命令、通过数、跳过数、证据边界和剩余风险，再开始下一个模块。
6. **真实环境**：先解决端点握手分类，再运行只读/非破坏矩阵；没有 test-owned namespace 和清理范围时不做
   destructive DDL。
7. **发布审查**：所有 Required 行必须是 `Done`，`TST-02` 必须有两模式证据，`REL-01/02` 必须完成。

## 本轮不开发的功能

以下功能保持 `Deferred`，必须保留清晰拒绝行为和限制说明：

- `DEF-04`：JSON、primitive collections/arrays、complex types
- `DEF-05`：Spatial、XML、native enum、OBJECT、LOB locator、REF CURSOR
- `DEF-06`：stored-procedure modification mappings、sequence/HiLo
- `DEF-10`：full-text/vector/search、advanced batching

`DEF-01/02/03/07/08/09` 已有部分实现，但 native、specification 或组合证据不完整，不能当作发布支持。
普通 enum converter 仍属于 Required 的 `MAP-07`。

## 连接器变更规则

Provider 只使用 Connector 0.3.0 公共 API。只有在确认 Provider 无法通过现有公共 API 满足某个 Required
契约时，才提交 Connector 变更提案；提案必须包含调用位置、公共 API 缺口、最小 API 设计、Provider/Connector
双方测试和兼容性影响。在用户确认前不修改 Connector、不改变协议/codec/TLS/认证实现。

## 权威文档

- [Feature checklist](efcore-10-feature-checklist.md)：逐行决定是否可发布。
- [Development acceptance plan](efcore-10-development-acceptance-plan.md)：规定证据等级和固定顺序。
- [Roadmap](efcore-10-roadmap.md)：发布阻塞和模块级状态。
- [Local development plan](efcore-10-local-development-plan.md)：详细波次、命令和历史证据。
- [Progress handoff](efcore-10-progress.md)：逐次提交的历史记录。
