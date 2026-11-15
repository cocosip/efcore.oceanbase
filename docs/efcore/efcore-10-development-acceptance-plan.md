# EF Core 10 开发与验收执行计划

## 目的和当前边界

本文档是 EF Core 10 Provider 的执行控制文档。它把功能矩阵转换为有顺序、可停机、可复核的开发任务。
每次开发只允许推进一个波次；完成条件未满足时，不得跳到另一个功能族，也不得把本地拦截测试写成真实 OceanBase 支持。

当前支持线：

- 分支：`efcore/10.0`
- Target Framework：`net10.0`
- EF Core 依赖：`[10.0.0, 11.0.0)`
- Connector：OceanBaseConnector 0.3.0
- 模式：MySQL、Oracle，所有 Required 功能必须分别验证
- EF Core 8/9：暂不开发，等 EF10 达到发布门槛后再从稳定基线分别移植

验收权威仍是 [EF10 feature checklist](efcore-10-feature-checklist.md)；本文档负责规定“先做什么”和“什么证据才算完成”。

## 当前检查点

创建本文档时的仓库状态：

- `HEAD` 的最新功能提交是 `747c61a`，内容为 TimeSpan component-member translation；TimeSpan total-member 验收记录在 `537eede`。
- Batch3ba 提交 `df67ff8` 是前一个显式 Binary/Guid `EF.Parameter` 集合根检查点。
- HasData schema bootstrap 是前一个已提交检查点 `1f6046f`。
- 当前工作树的 Release 本地测试为：Unit 2329、Functional 1869、Specification 868、Consumer 2，共 5068 个测试，0 失败、0 跳过。
- 文档中原有的 5028 测试数字是 HasData 已提交检查点的基线；Batch3ba 已在 `df67ff8` 中新增 8 个单元测试。
- 当前没有真实 OceanBase SQL/DDL 验收。已有 live 探针在握手阶段报 `Connection closed while reading the packet`，没有执行 SQL 或 DDL。

因此，Batch3ba 已收口；下一步固定为：统一进度文档、完成一次独立审查，然后进入连接边界波次。不得再开新的查询函数或参数载体来绕过连接边界工作。

## 完成状态的定义

一个功能只能在以下条件全部满足后标记为 `Done`：

1. 代码已经进入正确的 Provider 层，调用者、服务注册、测试 fixture 和公开包消费者均已同步。
2. MySQL 和 Oracle 都有同步、异步以及负面/边界测试；不适用的模式必须记录明确拒绝原因。
3. 测试证据被分层记录：单元、拦截/离线 Provider、包消费者、EF specification、真实 OceanBase。
4. 真实数据库要求的功能必须通过 live 矩阵；SQL 生成、参数元数据、模拟 reader 或拦截器不能关闭 live 门槛。
5. `Debug`、`Release`、选定 EF10 patch、包内容和包消费者检查通过；改动影响设计时工具时还要通过 tooling consumer。
6. 更新本文档、`efcore-10-feature-checklist.md`、`efcore-10-progress.md` 和对应 design/plan 文档，并在 `git diff --check` 后本地提交。

以下状态含义固定：

| 状态 | 含义 |
| --- | --- |
| `Done` | 代码、两种模式、要求的测试层和发布证据全部完成 |
| `Local complete` | 本地代码和测试完成，但 live/specification/release 门槛仍开放 |
| `In progress` | 还有代码、测试或证据缺口 |
| `Blocked-live` | 本地工作完成，真实环境/权限/传输阻塞，必须保留阻塞证据 |
| `Deferred` | 本轮明确不开发，必须保留拒绝或未支持行为 |

## 固定开发顺序

### 波次 0：Batch3ba 已完成基线

**功能 ID：** `QRY-03`、`QRY-11`、`MAP-04`、`MAP-06`、`MAP-07`

**代码范围：**

- `src/OceanBase.EntityFrameworkCore/Query/OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.cs`
- `src/OceanBase.EntityFrameworkCore/Query/OceanBaseCollectionParameterExpandingVisitor.cs`
- `tests/OceanBase.EntityFrameworkCore.UnitTests/BinaryCollectionRootQueryTests.cs`
- `tests/OceanBase.EntityFrameworkCore.UnitTests/GuidCollectionRootQueryTests.cs`
- 对应 intercepted functional tests、包消费者和查询设计文档

**必须完成：**

- `EF.Parameter(byte[]?[])`、`EF.Parameter(Guid?[])` 在 Constant/MultipleParameters 和两种模式下可翻译。
- NULL、重复值、重新绑定、空集合 typed metadata 都有断言。
- null-converting converter、未知布局、Oracle 空非 NULL BLOB 仍明确拒绝。
- 当前缓存修正不改变已有 split-query/retry 行为。

**已记录验收：**

- 提交：`df67ff8`。
- Batch3ba focused tests 和当前 Release 本地矩阵已通过；当前矩阵为 5036 通过、0 失败、0 跳过。
- Binary/Guid native equality、carrier coercion、Oracle empty-BLOB 和 live 结果仍未关闭，继续由 `MAP-04`/`QRY-03`/`TST-02` 管理。
- 后续不得从新的 collection carrier 开始；直接进入波次 1。

### 波次 1：连接、服务和诊断边界

**功能 ID：** `CFG-04`、`CON-02..05`、`DIA-01..02`、`SQL-01`

**主要代码：**

- `src/OceanBase.EntityFrameworkCore/Internal/OceanBaseOptionsExtension.cs`
- `src/OceanBase.EntityFrameworkCore/OceanBaseDbContextOptionsBuilderExtensions.cs`
- `src/OceanBase.EntityFrameworkCore/Storage/OceanBaseRelationalConnection.cs`
- `src/OceanBase.EntityFrameworkCore/Storage/OceanBaseConnectionOpenState.cs`
- `src/OceanBase.EntityFrameworkCore/Storage/OceanBaseSqlGenerationHelper.cs`
- `src/OceanBase.EntityFrameworkCore/Internal/OceanBaseLoggingDefinitions.cs`

**必须实现：**

- 已配置连接和已打开连接的 MySQL/Oracle 模式冲突在执行前拒绝。
- Provider 创建连接、调用方连接、外层 open lease 在同步/异步成功、失败、取消、重试后都保持正确计数和所有权。
- 超时和取消传到 Connector；失败后 context 可以按文档允许的边界复用。
- DatabaseFacade 所需服务全部解析；日志默认脱敏，敏感日志才允许参数值出现。
- 标识符、schema、空名称、嵌入分隔符、Oracle alias/DUAL 规则完整覆盖。

**必须新增/补齐测试：**

- `ConnectionLifecycleTests`：opened connection、mode conflict、caller ownership、close failure、replacement connection。
- `CommandExecutionBoundaryTests`：sync/async timeout、in-flight cancellation、reopen/reuse。
- `CommandLoggingTests`：成功、失败、敏感日志和异常内层原因。
- `OptionsTests`/`ConsumerTests`：service graph、facade 和 cache identity。
- `SqlGenerationHelperTests`：两种模式所有 identifier/parameter 边界。

**完成门槛：**

- 本地两模式 sync/async 全通过。
- 所有 cleanup failure 都保留原始异常和 cleanup 信息。
- 没有新的 `NotSupportedException` 用来掩盖已声明的 Required 行。
- 真实连接矩阵仍单独记录，不能用模拟连接关闭 `TST-02`。

### 波次 2：模型和标量映射

**功能 ID：** `MOD-01..03`、`MAP-01..07`

**主要代码：**

- `src/OceanBase.EntityFrameworkCore/Metadata/Conventions/OceanBaseConventionSetBuilder.cs`
- `src/OceanBase.EntityFrameworkCore/Storage/OceanBaseTypeMappingSource.cs`
- `OceanBaseIntegerTypeMapping.cs`
- `OceanBaseDecimalTypeMapping.cs`
- `OceanBaseFloatingPointTypeMapping.cs`
- `OceanBaseStringTypeMapping.cs`
- `OceanBaseByteArrayTypeMapping.cs`
- `OceanBaseTemporalTypeMapping.cs`
- `OceanBaseCompiledModelTypeMappings.cs`

**必须实现：**

- 主键、复合键、alternate key、外键、索引、owned/shadow 属性和生成值元数据在两种模式一致可解释。
- 整数/Boolean/decimal/float/double 的范围、精度、NULL、reader carrier 和写入校验完整。
- 文本的 Unicode、fixed/variable、长度、BYTE/字符语义、LOB key/index 限制明确。
- Binary/Guid 的 CHAR/RAW/BINARY/BLOB 表示、空值、长度、converter 和 clone 行为明确。
- 五种 temporal CLR 类型的精度、时区、范围、Oracle interval carrier 和 generated output 完整。
- converter 先转换再做实际写入容量检查；查询 operand 不受列容量错误限制。

**完成门槛：**

- 每种 CLR/store 组合都有 positive、NULL、最小/最大、非法声明和 clone 测试。
- 真实 EF query、tracked write、generated output 都经过 provider mapping，而不是只测 mapping 类。
- 不支持的组合在执行前失败，并写入 checklist 的限制栏。

### 波次 3：查询和物化

**功能 ID：** `QRY-01..11`

**主要代码：**

- `src/OceanBase.EntityFrameworkCore/Query/OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.cs`
- `OceanBaseSqlTranslatingExpressionVisitorFactory.cs`
- `OceanBaseMethodCallTranslatorProvider.cs`
- `OceanBaseMemberTranslatorProvider.cs`
- `OceanBaseAggregateMethodCallTranslatorProvider.cs`
- `OceanBaseCollectionParameterExpandingVisitor.cs`
- `OceanBaseQuerySqlGenerator*.cs`

**必须实现：**

- projection、comparison、Boolean 三值语义、NULL、join、subquery、Any/All、Contains。
- OrderBy/Distinct/Skip/Take、First/Single/default、Include/ThenInclude、tracking/identity resolution。
- GroupBy、Count、Sum、Average、Min/Max 的 empty/all-null/nullable 规则。
- 字符串、日期时间、数学、conditional/coalesce 的明确 overload matrix。
- Raw SQL 和 ExecuteSql 参数绑定不改变 SQL 结构；不支持的表达式有可诊断错误。
- primitive collection 的 captured、inline、explicit `EF.Parameter` 载体边界分别记录。

**完成门槛：**

- 每个 translator 家族有 SQL shape 单测和同步/异步 intercepted 结果测试。
- 负面测试证明不支持的 overload 在执行前拒绝。
- graph/split/filter 测试断言排序、join key、窗口和关联顺序，不能只依赖 canned rows。
- native 查询结果、排序、collation、UNION coercion 仍需 live 证据。

### 波次 4：跟踪写入和事务

**功能 ID：** `UPD-01..05`、`TXN-01..03`

**主要代码：**

- `src/OceanBase.EntityFrameworkCore/Update/OceanBaseUpdateSqlGenerator.cs`
- `OceanBaseModificationCommandBatch.cs`
- `OceanBaseBatchExecutor.cs`
- `src/OceanBase.EntityFrameworkCore/Storage/OceanBaseRelationalTransactionFactory.cs`
- `OceanBaseRetryingExecutionStrategy.cs`

**必须实现：**

- insert/update/delete 的参数顺序、显式 key、default-only insert 和 generated key readback。
- MySQL `LAST_INSERT_ID`、Oracle `RETURNING`、NULL/converted/composite generated outputs。
- 原始 key/concurrency token、零行并发异常、图保存依赖顺序和父子 key 传播。
- 自动事务、已有事务加入、savepoint、取消恢复和 cleanup failure 的所有权规则。
- retry 只重放已确认且可恢复的错误；未知结果、commit/cleanup/cancel/XA 不得重放。

**完成门槛：**

- sync/async、单实体/图、成功/失败/取消都有 deterministic command/transaction tests。
- 真实服务器确认提交、回滚、生成值和并发行为后，才能关闭对应 Required 行。
- stored-procedure modification mappings 继续明确拒绝，直到单独提升 `DEF-06`。

### 波次 5：数据库和 Schema 生命周期

**功能 ID：** `SCH-01..03`

**主要代码：**

- `src/OceanBase.EntityFrameworkCore/Storage/OceanBaseDatabaseCreator*.cs`
- `OceanBaseSchemaSqlGenerator.cs`
- `src/OceanBase.EntityFrameworkCore/Migrations/OceanBaseMigrator.cs`
- `OceanBaseHistoryRepository.cs`
- `OceanBaseMigrationsSqlGenerator.cs`

**必须实现：**

- absence、认证失败、权限失败、网络失败分类清楚。
- existing schema、MySQL owned database、Oracle owned user/schema 的边界清楚。
- GenerateCreateScript、EnsureCreated、EnsureDeleted、Migrate、HasData 的执行顺序、失败恢复和所有权清楚。
- DDL 非原子性、部分创建后的恢复责任和 retry 禁止行为必须写进文档。

**完成门槛：**

- 离线 SQL/double 测试先通过，再申请 test-owned live namespace。
- live DDL、权限、identity、drop、并发连接和 pool 影响分别记录。
- 未取得隔离 namespace 和清理范围前，不执行 destructive live 测试。

### 波次 6：Specification、包和发布证据

**功能 ID：** `TST-01`、`TST-02`、`REL-01`、`REL-02`、`PKG-01..04`

**必须完成：**

- 扩展 EF relational specification model/query/write/runtime families；每个排除项映射到具体 checklist ID。
- 两种模式、声明的 server/tenant/connector/EF patch 运行完整 live core matrix。
- 记录不含凭据的支持环境、TLS/transport、direct/proxy、已知限制。
- 在最终 revision 上重新跑 Debug/Release、EF patch、包内容、包消费者、tooling/bundle 和 `git diff --check`。

**发布门槛：**

- 任一 Required 行为 `In progress` 或 `Planned`，或 `TST-02` 缺失，都不得发布。
- package passed 只能证明包结构/消费者可用，不能替代 live 验收。

## Deferred 功能规则

以下功能本轮保持 `Deferred`，不允许因为局部代码方便就插队：

- `DEF-04`：JSON、primitive collections/arrays、complex types
- `DEF-05`：Spatial、XML、native enum、OBJECT、LOB locator、REF CURSOR
- `DEF-06`：stored-procedure modification mappings、sequence/HiLo
- `DEF-10`：full-text/vector/search、advanced batching

`DEF-01/02/03/07/08/09` 已有部分本地实现，但只有在 native/specification/release 门槛满足后才能提升状态。
普通 enum converter 属于 Required 的 `MAP-07`，不能因为 native enum 延期而一起跳过。

## 每个模块必须留下的执行记录

### 2026-11-15：TimeSpan total-member translation

Module: TimeSpan total-member translation
Checklist IDs: QRY-07, QRY-11, MAP-05, MAP-06
Base commit: `5b58dfe`
Implementation files: `OceanBaseTemporalMemberTranslator.cs`, `OceanBaseQuerySqlGenerator.cs`
Tests added/changed: `TemporalMemberQueryTests.cs`, `TimeSpanTotalFunctionalTests.cs`
Focused command and result: Unit 4 passed; Functional 12 passed.
Full local command and result: Release Unit 2,325 passed; Release Functional 1,857 passed; zero failures/skips.
Package/tooling command and result: package consumer passed on EF10.0.0/10.0.14 in both modes; tooling consumer
passed catalog/compiled-model/migration checks on both patches with `-SkipBundle`.
Live command and result: not run; existing User Secrets probes still close before SQL during pre-authentication.
Evidence boundary: local translation, carrier validation, intercepted sync/async materialization and package/tooling
compatibility are proved; native interval precision/range/arithmetic and live results are not proved.
Known gaps: bundle verification requires unavailable 10.0.14 runtime packs; native and live gates remain open.
Next module: choose the next complete local query/mapping module after this commit.
Commit: `27509aa`.

### 2026-11-15：TimeSpan component-member translation

Module: TimeSpan component-member translation
Checklist IDs: QRY-07, QRY-11, MAP-05, MAP-06
Base commit: `27509aa`
Implementation files: `OceanBaseTemporalMemberTranslator.cs`, `OceanBaseQuerySqlGenerator.cs`
Tests added/changed: `TemporalMemberQueryTests.cs`, `TimeSpanComponentFunctionalTests.cs`
Focused command and result: Unit filtered component tests 4 passed; Functional filtered component tests 12 passed.
Full local command and result: Release Unit 2,329 passed; Release Functional 1,869 passed; Release Specification 868 passed; Consumer 2 passed; zero failures/skips.
Package/tooling command and result: Verify-Package and PackageConsumer passed on EF10.0.0/10.0.14 in both modes using an isolated feed/cache; ToolingConsumer passed catalog, compiled-model and migration/script checks on both patches with `-SkipBundle`. The full bundle substep remains environment-blocked by missing 10.0.14 runtime packs.
Live command and result: not run; the existing User Secrets probes still close before SQL during the pre-authentication handshake.
Evidence boundary: local SQL generation, signed component semantics, carrier validation and intercepted sync/async reader shaping are covered; native interval precision/range/arithmetic and live OceanBase results are not proved.
Known gaps: bundle/runtime-pack verification and native/live gates remain open.
Next module: begin the execution plan's connection/service/diagnostic boundary wave (`CFG-04`, `CON-02..05`, `DIA-01..02`, `SQL-01`).
Commit: `747c61a`.

每个波次完成后，在对应 plan/progress 文档增加一段记录，字段必须完整：

```text
Module:
Checklist IDs:
Base commit:
Implementation files:
Tests added/changed:
Focused command and result:
Full local command and result:
Package/tooling command and result:
Live command and result:
Evidence boundary:
Known gaps:
Next module:
Commit:
```

`Evidence boundary` 必须明确写出“证明了什么”和“没有证明什么”。如果 live 环境不可用，状态写 `Blocked-live`，保留错误、时间、endpoint 类型和“未执行 SQL/DDL”的事实；不得把它改写为 `Done`。

## 固定检查命令

先跑 focused，再跑完整本地矩阵；focused 未通过时不得启动完整矩阵：

```powershell
dotnet test tests/OceanBase.EntityFrameworkCore.UnitTests/OceanBase.EntityFrameworkCore.UnitTests.csproj --no-restore --configuration Release --filter "FullyQualifiedName~<FocusedTest>"
dotnet test tests/OceanBase.EntityFrameworkCore.FunctionalTests/OceanBase.EntityFrameworkCore.FunctionalTests.csproj --no-restore --configuration Release --filter "FullyQualifiedName~<FocusedTest>"

dotnet test tests/OceanBase.EntityFrameworkCore.UnitTests/OceanBase.EntityFrameworkCore.UnitTests.csproj --no-restore --configuration Release
dotnet test tests/OceanBase.EntityFrameworkCore.FunctionalTests/OceanBase.EntityFrameworkCore.FunctionalTests.csproj --no-restore --configuration Release
dotnet test tests/OceanBase.EntityFrameworkCore.SpecificationTests/OceanBase.EntityFrameworkCore.SpecificationTests.csproj --no-restore --configuration Release
dotnet test tests/OceanBase.EntityFrameworkCore.ConsumerTests/OceanBase.EntityFrameworkCore.ConsumerTests.csproj --no-restore --configuration Release

pwsh tests/OceanBase.EntityFrameworkCore.PackageConsumer/Verify-Package.ps1
pwsh tests/OceanBase.EntityFrameworkCore.ToolingConsumer/Verify-Tooling.ps1
git diff --check
```

Live 测试必须显式设置 `OCEANBASE_RUN_LIVE_TESTS=1`，凭据只从 User Secrets 读取；先做 `SELECT 1` 探针，再做读，最后才做 test-owned DDL/写入。

## 停止和交接规则

- 一个波次未完成，不开始下一个波次。
- 任何测试失败先修当前波次，不通过增加排除项来获得绿色。
- 文档、代码、测试和包证据不同步时，状态保持 `In progress`。
- 每次上下文即将结束时，必须记录当前 commit、工作树改动、已运行命令、结果、剩余 ID 和下一条具体命令。
- “本地测试全绿”只能关闭本地证据，不能自动关闭 native/live/specification/release 证据。
