# EF Core 10 Boolean conversion plan

## Scope

This module covers the exact `System.Convert.ToBoolean` overloads that can be translated
without changing `OceanBaseConnector`:

- numeric overloads for `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`,
  `float`, `double` and `decimal`;
- `Convert.ToBoolean(bool)` for native Boolean mappings and Boolean properties with a
  non-canonical converter;
- MySQL and Oracle modes, synchronous and asynchronous EF query execution.

String, dynamic `object`, unsupported carriers, NaN/Infinity server semantics and live
OceanBase results remain outside this module.

## Implementation

`OceanBaseBooleanTranslator` is registered in the provider method-call translator. Numeric
operands become `operand <> 0` using the existing numeric mapping validation and provider
carrier. A canonical Boolean operand is preserved. A non-canonical Boolean converter is
wrapped with the existing internal Boolean marker so predicates and projections use a native
Boolean result without changing the property's storage converter.

No public API, dependency or Connector change is required.

## Acceptance standard

| Layer | Required evidence | Current result |
| --- | --- | --- |
| RED | The new numeric conversion query fails before the translator exists | Passed: both modes rejected `Convert.ToBoolean` before implementation |
| Unit | SQL shape for numeric, nullable and converted Boolean operands; string rejection | Passed: 76 Boolean query/XOR tests |
| Functional | Sync/async intercepted reader, numeric result, boxed-null result and converted Boolean result | Passed: 4 conversion cases plus Boolean collection regression cases |
| Live OceanBase | Native coercion, floating edge values and result semantics | Open: supplied endpoints close during pre-authentication |
| Package/release | Full package and final release matrix | Open: this commit is a local feature checkpoint |

The module is therefore `Local complete` for code and intercepted tests, while `QRY-01`,
`QRY-11` and `MAP-01` remain `In progress` until their wider matrices and live evidence pass.

## Reproduction commands

```text
dotnet test tests/OceanBase.EntityFrameworkCore.UnitTests/OceanBase.EntityFrameworkCore.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~BooleanQueryTests|FullyQualifiedName~BooleanXorQueryTests"
dotnet test tests/OceanBase.EntityFrameworkCore.FunctionalTests/OceanBase.EntityFrameworkCore.FunctionalTests.csproj -c Release --no-restore --filter "FullyQualifiedName~BooleanCollectionRootTests|FullyQualifiedName~BooleanConversionTests"
```

Implementation commit: `aa2cee8` (local only).
