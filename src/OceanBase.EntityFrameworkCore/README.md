# OceanBase.EntityFrameworkCore

Entity Framework Core 10 provider for OceanBase MySQL and Oracle compatibility modes, using the public
OceanBaseConnector API. The package targets .NET 10 and accepts EF Core 10 patch versions in `[10.0.0,11.0.0)`.
Select the tenant's compatibility mode explicitly when configuring a context:

```csharp
using Microsoft.EntityFrameworkCore;
using OceanBase.EntityFrameworkCore;

optionsBuilder.UseOceanBase(connectionString, OceanBaseCompatibilityMode.MySql);
// For an Oracle-mode tenant, select OceanBaseCompatibilityMode.Oracle.
```

Load the connection string from application configuration or a secret store. The selected mode must match the
tenant. Driver protocol, authentication, connection pooling and transport settings belong to OceanBaseConnector.

This is a development provider. Local tests cover model/type mappings, SQL generation, intercepted query/write
pipelines and package consumption. Real OceanBase query, write, transaction and schema acceptance is still open
for both modes. Passing local tests does not establish server compatibility or release readiness.

Database/schema creation, migrations, reverse engineering and the wider EF feature inventory are still under
development. EF Core 8/9 adaptation is postponed until the EF10 provider stabilizes. Review the repository's EF10
feature checklist and progress documents before choosing a development checkpoint for an application.
