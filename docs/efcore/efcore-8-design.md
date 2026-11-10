# EF Core 8 Provider Design

## Goal

Implement the first usable OceanBase EF Core provider on the EF Core 8 branch. It must support both MySQL and
Oracle tenant compatibility modes through the existing OceanBaseConnector ADO.NET provider.

## Package and runtime

- Branch: efcore-8 (created from dev after the design baseline is accepted).
- Package: OceanBase.EntityFrameworkCore 8.x.
- EF dependency: Microsoft.EntityFrameworkCore and Microsoft.EntityFrameworkCore.Relational in the 8.x range.
- Initial provider asset: net8.0.
- EF8 requires .NET 8 to build and run. A later application TFM is a separate compatibility test and is not
  promised by the package merely because the net8.0 asset can be referenced.

## Public configuration

The provider exposes UseOceanBase overloads for a connection string and an existing DbConnection. Both take an
explicit OceanBaseCompatibilityMode value. The mode is included in the EF options extension and model-cache key.

## First feature profile

The first implementation must cover the normal relational path:

- provider registration and IsOceanBase detection;
- model building for tables, columns, keys, indexes and relationships;
- scalar mappings for common numeric, text, binary, Boolean, date/time and decimal CLR types;
- parameterized queries with projection, predicates, ordering, joins and paging where the target mode supports it;
- insert, update and delete with generated-key retrieval;
- explicit local transactions and cancellation propagation;
- EnsureCreated/database existence behavior where connector permissions allow it;
- both synchronous and asynchronous execution paths.

Migrations, reverse engineering, provider-specific JSON/LOB/OBJECT/REF CURSOR APIs and advanced batching are
separate milestones. They must not be implied by the first package's basic CRUD support.

## EF8 release review

The EF8 documentation highlights complex types, primitive collections, JSON column mapping, raw SQL for unmapped
types, lazy-loading improvements, ExecuteUpdate/ExecuteDelete improvements, better IN queries, pending-model
change checks, sentinel values for database defaults and RETURNING/OUTPUT configuration. These are review items,
not automatic support claims. Each feature is either covered by the first feature profile or explicitly deferred
until OceanBase SQL and MySQL/Oracle result semantics are tested.

## Mode-specific contracts

The branch must test and document identifier quoting, parameter naming, pagination, Boolean representation,
decimal precision, temporal types, identity/key generation and concurrency behavior independently for MySQL and
Oracle modes. A shared implementation is allowed only when the generated SQL and result semantics match.

## Verification gates

Before publishing 8.x, run provider unit tests, EF relational tests, packed-package consumer tests and every
selected EF relational specification test class, then the packed-package consumer tests and every available
MySQL/Oracle live test. The report must separate local results from unavailable server, proxy, TLS,
authentication and platform evidence.
