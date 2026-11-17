# EF Core 10 功能实现缺口

2026-11-17，核对基线 `c2c9b65`。本文只区分代码已实现、明确不支持和明确延期，
不将缺少实库验收、规格测试或发布检查计为“功能尚未实现”。

## 统计口径

- Required 清单的 53 行混有功能、测试、包和发布要求；`In progress` 不是未实现计数。
- 核心模块中未发现整块尚未实现的模块。连接、模型、标量映射、查询、Include/跟踪、
  EF10 命名过滤器、CRUD/并发、事务、Schema 生命周期均已有实现或 EF 关系型管线适配。
- migrations、scaffolding、ExecuteUpdate/Delete、继承/实体拆分、多对多、split query、
  lazy loading、普通 compiled model、HasData/seeding 和有限 retry 也已有实现。
  这不表示所有组合都支持，也不表示代码无缺陷。
- 已确认的功能边界按下表分为 **3 类部分实现/不支持的细项 + 4 组明确延期功能**。
  这是功能组计数，不是剩余 API 数量或开发完成百分比；当前矩阵没有完整的逐 API 未实现台账。

## 已有核心功能中的明确边界

| 类别 | 未实现或明确拒绝的细项示例 | Owning source |
| --- | --- | --- |
| 查询翻译的部分 API/组合 | DateTime.AddDays/AddHours 等未列入 temporal translator 的方法、TimeOnly.AddHours；部分字符串/显式 default overload；EF 单一 JSON 参数集合、映射集合属性；Oracle contextual CHAR/NCHAR 集合 roots、BLOB equality/membership/LOB set operations；不兼容 converter 的 bulk 表达式 | `Query/OceanBaseTemporalMethodTranslator.cs`、`OceanBaseStringTranslator.cs`、`OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.cs`、`OceanBaseQuerySqlGenerator.cs`、`OceanBaseBulkDmlVisitor.cs` |
| 生成值/主键策略的部分形状 | MySQL 生成键回读仅接受支持的 int/long identity，不接受 default/computed 生成键和多生成键形状；TPC 不提供跨表 store-generated key 分配 | `Update/OceanBaseModificationCommandBatch.cs`、`Metadata/OceanBaseRelationalAnnotationProvider.cs` |
| DDL/迁移/反向工程的部分元数据 | model trigger、表/列 comment、数据库 collation、filtered index、Oracle column collation；自动修改 identity/computed 列、跨 namespace 表迁移；scaffolding 对 unsupported types、expression/prefix indexes、已知 ON UPDATE 等拒绝 | `Storage/OceanBaseSchemaSqlGenerator.cs`、`Migrations/OceanBaseMigrationsSqlGenerator.cs`、`Design/OceanBaseDatabaseModelFactory.cs` |

这些边界中有主动限定范围的 API 和服务端语义限制，不能全部自动升级为当前 release 必须新增的功能。
`HasData`、普通生成值、普通 migrations/scaffolding、普通 bulk 和普通关系查询不能因此整体记为未实现。

## 当前版本明确延期的 4 组

| ID | 尚未提供的功能组 |
| --- | --- |
| DEF-04 | JSON、映射的 primitive collections/arrays、complex types，以及对应 query/update profile |
| DEF-05 | Spatial、XML、native enum、OBJECT、LOB locator、REF CURSOR 的 Provider 集成 |
| DEF-06 | 存储过程 INSERT/UPDATE/DELETE 映射、sequence/HiLo 策略 |
| DEF-10 | Provider 全文/向量/search functions、advanced batching 优化 |

捕获 CLR 集合作为 IN/查询参数、普通 enum converter、普通 CLOB/BLOB 标量和内部 JSON
reader metadata 已有实现，不应与以上延期能力混淆。

## 不计入 Provider 未实现数量

- Oracle Double 参数极值/微小值、MySQL 默认 Decimal SUM/AVG 与 scalar LEAST/GREATEST
  公开 reader 的已复现失败，属于 Connector/server 参数或结果边界，内部根因仍未归责。
- 目标版本 NCLOB DDL、MySQL LOB DEFAULT 等拒绝不能直接归为 Provider 功能缺失。
- Proxy/OB20/路由/会话迁移/池与协议由 Connector 负责，不转成 EF 开发任务。
- NativeAOT、precompiled query、代理/compiled model 组合、ambient/distributed transaction
  的支持未建立；缺少证据不能独自证明完全没有实现，不纳入上述精确分组计数。

后续进度应分别报告“新增或修复了什么功能”和“补了什么证据”。完整验收状态仍以
[功能矩阵](efcore-10-feature-matrix.md) 为准。
