# EF Core 10 发布工作板

本文档是 `efcore/10.0` 的执行入口。功能定义仍以
[feature checklist](efcore-10-feature-checklist.md) 为准，本文档把当前状态、下一步和验收证据压缩成
可执行的工作板。每次只推进一个模块；模块完成后必须测试、走查、更新文档并提交，再开始下一模块。

## 当前基线

归属边界：本仓库开发 EF Provider 的翻译、模型/映射、物化/跟踪及操作编排；Connector 负责协议、
认证/TLS、连接池、参数/reader 传输和原生事务操作；OceanBase 服务端负责 SQL、类型及约束。
下文的连接池、传输、故障切换等验收缺口不代表要在 Provider 中实现这些驱动功能。
每个模块应列明归属和使用的现有驱动公共 API；缺失 API 单独记录依赖，不在 EF 内绕过驱动。

- 分支：`efcore/10.0`
- Target Framework：`net10.0`
- EF Core：`[10.0.0,11.0.0)`
- Connector：`OceanBaseConnector 0.3.0`
- 模式：MySQL、Oracle
- Required 行：53 行；两种模式均为 9 行 `Done`、44 行 `In progress`
- 已完成 Required 行：`CFG-01..03`、`CON-01`、`SQL-02`、`PKG-01..04`
- 当前本地模块：`DateTimeOffset.DateTime`（base `a7aec5d`）；Oracle 本地墙上时间 DateTime、MySQL 拒绝、测试、包和文档已完成，本轮未推送
- 真实 MySQL/Oracle 探针均在 Connector 0.3.0 预认证阶段断 packet；未执行 SQL/DDL，live 仍为 `Blocked-live`

最近一次完整本地证据：

| 检查 | 结果 | 证据边界 |
| --- | --- | --- |
| Release Unit | 2543 通过 | Provider 单元和本地 doubles |
| Release Functional | 2466 通过 | 拦截式同步/异步命令管线 |
| Release Specification | 1538 通过，16 个明确跳过 | 跳过已映射到 `MAP-01/03/05/06`；本轮无新增跳过 |
| Debug Unit / Functional | 历史基线 2533 / 2457 通过 | 本轮未重跑 Debug；Specification 1538 + 16 mapped skips，ConsumerTests 2 |
| EF10.0.14 isolated Release | 本轮包消费者 EF10.0.14 通过；完整隔离套件未重跑 | 本轮完整 Release 使用当前源码，Connector 0.3.0 |
| TotalOffsetMinutes module | Unit 4/4、Functional 3/3；Full Release Unit 2537、Functional 2460、Specification 1538 | Oracle timestamp-with-time-zone offset extraction; MySQL pre-open rejection; package `10.0.0-total-offset-minutes-20261116` and EF10.0.0/10.0.14 both-mode consumers pass; live handshake remains blocked; [module record](efcore-10-total-offset-minutes-plan.md) |
| DateTimeOffset.Ticks module | Unit 3/3、Functional 2/2；Full Release Unit 2543、Functional 2466、Specification 1538、ConsumerTests 2 | Oracle local wall-clock Gregorian ticks; MySQL pre-open rejection; package `10.0.0-date-time-offset-ticks-20261116` and EF10.0.0/10.0.14 both-mode consumers pass; live handshake remains blocked; [module record](efcore-10-date-time-offset-ticks-plan.md) |
| DateTimeOffset.DateTime module | Unit 3/3、Functional 3/3；Full Release Unit 2546、Functional 2469、Specification 1538、ConsumerTests 2 | Oracle local wall-clock DateTime cast with `Kind.Unspecified`; MySQL pre-open rejection; package `10.0.0-date-time-offset-date-time-20261116` and EF10.0.0/10.0.14 both-mode consumers pass; live handshake remains blocked; [module record](efcore-10-date-time-offset-date-time-plan.md) |
| Grouped aggregate results | SQL24 + correlated16、runtime64 通过；初始 NULL 关联 SQL 8 失败/8 对照通过 | NULL/composite/converted key、组内集合/首实体、两种 result/null 配置、跟踪、分页绑定及恢复；普通 Join 不变；可能保留 LATERAL，继承 OceanBase 版本要求；native/performance 未验证；[模块记录](efcore-10-grouped-results-plan.md) |
| Grouped-result new package | `10.0.0-grouped-results-20261116.1` 通过 | 包内容、两补丁/两模式公开 consumer、package-only graph 和 tested baseline source/archive/consumer hash 一致；走查的 consumer namespace 问题已修复验证；无新 CLI/bundle/native 结果 |
| DefaultIfEmpty reference/presence | Unit 30/30、Functional 72/72；初始新增 SQL 6 失败、runtime 12 失败/4 通过 | 整体 null、装箱、真实 NULL 异常、混合实体、显式用户投影、绑定及恢复；走查补齐 nullable 算术；每补丁 10 项离线检查；[模块记录](efcore-10-default-if-empty-reference-plan.md) |
| DefaultIfEmpty projected collections | 新增 32/32、完整 default runtime 104/104；移除集合绑定遍历时新增 32 项全部失败 | 仅测试/文档；缺失父行、空/多子项、NULL/converter、身份范围、分页绑定、显式构造、失败恢复；独立走查无实质问题；取消仅命令阶段，物化错误另证 reader 清理；[同模块记录](efcore-10-default-if-empty-reference-plan.md) |
| Query enumeration lifecycle | 新增 34 项，完整命令 profile 48/48、关联策略 112/112；关闭 base buffering 时 8/48 失败 | 原实现通过提前释放、二次读取失败/取消、缓冲、显式调用方租约、身份范围及复用；独立走查无实质问题；仅 EF 管线与本地连接/reader，非物理 wire/pool 证据；[模块记录](efcore-10-query-enumeration-plan.md) |
| Historical DefaultIfEmpty scalar semantics | Unit 24/24、Functional 40/40；前期移除修复 12/20 失败，走查问题另有 4 项 RED | nullable/转换器/默认行/计算源、实体跟踪、参数刷新、失败恢复；8 项只读夹具离线检查；匿名成员按 EF 补丁分别记录，完整 reference/boxing 语义仍开放；[模块记录](efcore-10-default-if-empty-plan.md) |
| Correlated lateral queries | Unit 16/16、Functional 36/36；移除修复时 14/32 失败 | 两模式的 lateral/普通表分支、关联/嵌套/分页、NULL/集合/identity 物化、绑定刷新及恢复；独立走查无待修问题；只读夹具增加 pair 断言，离线总计每补丁 6/6；仅 EF 翻译，原生/版本验收仍开放；[模块记录](efcore-10-correlated-queries-plan.md) |
| Read-only native EF query fixture | 离线 4/4，EF10.0.0 / 10.0.14 各通过；原生 0/2 通过 | 内联四行，无建表或写入；真实 EF 同步/异步查询断言；两模式开连接失败，OceanBaseException/code 0/EF readers 0；未取得认证、服务端版本或 SQL 结果；独立走查无待修问题；[模块记录](efcore-10-read-only-live-query-plan.md) |
| Raw query materialization/recovery | 聚焦 36/36，完整 Functional 2219/2219 | 按列名/大小写/顺序物化、converter/NULL/identity、组合/绑定、缺列/错误值、失败/取消/恢复；现有生产代码通过；修正夹具全局缓存污染及走查的投影/排序断言；[模块记录](efcore-10-raw-query-runtime-plan.md) |
| Relational set operations | Unit 42/42、Functional 62/62；16 syntax/LOB + 2 ALL RED | 四种集合运算、嵌套/组合/绑定/物化/恢复；OceanBase Oracle MINUS、LOB/非 distinct 差集交集拒绝；基于 OceanBase 4.3.5 文档；NCLOB 是保守 Provider 边界；独立走查无代码问题，证据措辞已修正；[模块记录](efcore-10-set-operations-plan.md) |
| Relationship-change writes | 聚焦 36/36、关联 460/460 | 可选 shadow FK 断开、ClientSetNull、必需关系重挂接/孤儿时机、一对一替换顺序、接受和后续失败/取消/savepoint 恢复；仅测试和文档变更；走查的开连接前拒绝断言已补强；[模块记录](efcore-10-relationship-writes-plan.md) |
| TPH runtime verification | 聚焦 78/78、关联 362/362；最终断言纳入全量门禁 | 现有单表继承查询/物化/CRUD/图保存/恢复链路；仅测试和文档变更；SQL/绑定断言经独立走查补强；[模块记录](efcore-10-tph-runtime-plan.md) |
| SaveChanges concurrency interception | 聚焦 52/52、关联 186/186；初始 RED 28 失败/8 通过 | 标准诊断、同步/异步 hook 和显式抑制；保留异常/entries/token，跳过旧输出并继续后续 batch；独立走查无待修问题；[模块记录](efcore-10-save-concurrency-interception-plan.md) |
| Original package replay | 复用 `10.0.0-default-reference-20261116.1` 并重验通过 | 包内容及两补丁/两模式原有公开 consumer 通过；package-only graph、原 archive/consumer DLL hash 一致；新增枚举测试运行于重编译源代码，不宣称当前 source DLL 与旧包 hash 一致；无新包/CLI/bundle/hosted/Linux/native evidence，来源见枚举模块记录 |
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

这些结果不能关闭真实 OceanBase 验收。历史探针在认证前报
`Connection closed while reading the packet`；本轮脱敏 EF 探针也在开连接阶段失败，reader 命令为 0，
但不据此推断更具体的原因。本轮未执行 EF 查询，未取得服务端 SQL 执行证据，未尝试 DDL。

## 已完成和部分完成的功能

以下模块已经有本地代码、测试和包证据，但除非 checklist 行变为 `Done`，不得对外宣称完整支持：

| 模块 | 当前本地结果 | 尚未关闭的边界 |
| --- | --- | --- |
| 配置、模式和缓存 | `CFG-01..03` 完成 | `CFG-04` 的完整 facade/schema/native 生命周期 |
| Connector 连接边界 | `CON-01` 完成；`CON-02..05` 有清理、所有权、取消和重用回归 | 真实 wire、池化、超时和物理连接行为 |
| 标量映射 | 整数、Boolean、Decimal、浮点、文本、Binary/Guid、五类 temporal 已有大量本地覆盖 | 完整范围、BYTE/字符容量、LOB、时区/精度、真实 round-trip |
| 查询翻译 | 关系集合运算、字符串、集合根、聚合、分页、Include、split、布尔、数学、时间成员已有本地覆盖 | 完整 overload、转换器组合、原生集合运算/排序/结果语义 |
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

当前 GroupBy 聚合结果组合已完成本地闭环，见
[模块记录](efcore-10-grouped-results-plan.md)。用户要求本轮收尾、更新文档并本地提交；停在此检查点，
不继续开发下一模块。下列顺序供后续恢复使用，真实 OceanBase 与发布验收仍未关闭。

此前沿着相关查询模块补齐 DefaultIfEmpty 引用投影及行存在语义，参见
[module plan](efcore-10-default-if-empty-reference-plan.md)。只使用已有驱动公共命令/reader API，无 Connector 改动。
两模式本地 SQL/物化/恢复和全量门禁通过；只读验收夹具补充默认值断言，离线每补丁 10 项通过。
两补丁均保留匿名/DTO 整体 null、装箱及真实 NULL 异常；任意 CLR 客户端投影仍未声称支持。
原生结果及部署版本仍未验证；本轮未重复此前失败的开连接尝试。
本轮补齐 reader 创建后的查询枚举生命周期，参见
[模块计划](efcore-10-query-enumeration-plan.md)。验证提前结束、读取失败/取消、retry 缓冲、调用方 EF
连接租约及身份范围；已有实现通过，仅补测试和证据，不开发 Connector 的物理读取或取消机制。
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
