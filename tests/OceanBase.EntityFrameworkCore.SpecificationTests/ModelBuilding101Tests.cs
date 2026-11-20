using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OceanBase.EntityFrameworkCore.SpecificationTests;

public abstract class OceanBaseModelBuilding101Tests(OceanBaseCompatibilityMode mode)
    : ModelBuilding101RelationalTestBase
{
    protected override DbContextOptionsBuilder ConfigureContext(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseOceanBase("Server=offline;Database=model_building_101", mode)
            .AddInterceptors(NoDatabaseAccess.Instance);

    private sealed class NoDatabaseAccess : DbConnectionInterceptor
    {
        public static NoDatabaseAccess Instance { get; } = new();

        public override InterceptionResult ConnectionOpening(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            => throw new InvalidOperationException("Model specification tests cannot access a database.");

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Model specification tests cannot access a database.");
    }
}

public sealed class MySqlModelBuilding101Tests()
    : OceanBaseModelBuilding101Tests(OceanBaseCompatibilityMode.MySql);

public sealed class OracleModelBuilding101Tests()
    : OceanBaseModelBuilding101Tests(OceanBaseCompatibilityMode.Oracle);
