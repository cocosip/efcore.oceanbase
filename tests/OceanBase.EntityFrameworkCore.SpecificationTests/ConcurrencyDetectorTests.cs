using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace OceanBase.EntityFrameworkCore.SpecificationTests
{
    public sealed class MySqlConcurrencyDetectorTests(MySqlConcurrencyFixture fixture)
        : OceanBaseConcurrencyDetectorTests<MySqlConcurrencyFixture>(fixture);

    public sealed class OracleConcurrencyDetectorTests(OracleConcurrencyFixture fixture)
        : OceanBaseConcurrencyDetectorTests<OracleConcurrencyFixture>(fixture);

    public sealed class MySqlConcurrencyFixture()
        : OceanBaseConcurrencyFixture<MySqlConcurrencyFixture>(OceanBaseCompatibilityMode.MySql);

    public sealed class OracleConcurrencyFixture()
        : OceanBaseConcurrencyFixture<OracleConcurrencyFixture>(OceanBaseCompatibilityMode.Oracle);

    public abstract class OceanBaseConcurrencyDetectorTests<TFixture>(TFixture fixture)
        : ConcurrencyDetectorEnabledRelationalTestBase<TFixture>(fixture)
        where TFixture : OceanBaseConcurrencyFixture<TFixture>, new()
    {
        public override Task FromSql(bool async)
        {
            var sql = Fixture.Mode == OceanBaseCompatibilityMode.MySql
                ? "SELECT * FROM `Products`" : "SELECT * FROM \"Products\"";
            return ConcurrencyDetectorTest(async context => async
                ? await context.Products.FromSqlRaw(sql).ToListAsync()
                : context.Products.FromSqlRaw(sql).ToList());
        }

        public override async Task SaveChanges(bool async)
        {
            await ConcurrencyDetectorTest(async context =>
            {
                context.Products.Add(new Product { Id = 2, Name = "Unicorn Replacement Horn Pack" });
                return async ? await context.SaveChangesAsync() : context.SaveChanges();
            });
            // The store-backed absence query belongs to live acceptance; this fixture proves zero opens.
        }

        protected override Task ConcurrencyDetectorTest(Func<ConcurrencyDetectorDbContext, Task<object>> test)
            => base.ConcurrencyDetectorTest(async context =>
            {
                try
                {
                    return await test(context);
                }
                finally
                {
                    var local = Assert.IsType<OceanBaseConcurrencyFixture<TFixture>.OfflineContext>(context);
                    Assert.Equal(0, local.Guard.OpeningAttempts);
                }
            });

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public Task Concurrent_bulk_operations_are_rejected_before_open(bool async, bool delete)
            => ConcurrencyDetectorTest(async context =>
            {
                var query = context.Products.Where(product => product.Id == 1);
                if (delete) return async ? await query.ExecuteDeleteAsync() : query.ExecuteDelete();
                return async
                    ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(product => product.Name, "changed"))
                    : query.ExecuteUpdate(setters => setters.SetProperty(product => product.Name, "changed"));
            });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Context_reaches_execution_after_contention_ends(bool async)
        {
            await using var context = Fixture.CreateContext();
            var detector = context.GetService<IConcurrencyDetector>();
            using (await Task.Run(() => detector.EnterCriticalSection()))
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Query(context, async));
                Assert.Equal(CoreStrings.ConcurrentMethodInvocation, error.Message);
                Assert.Equal(0, context.Guard.OpeningAttempts);
            }
            await Assert.ThrowsAsync<DatabaseAccessBlockedException>(() => Query(context, async));
            Assert.Equal(1, context.Guard.OpeningAttempts);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Explicitly_disabled_checks_reach_execution_during_contention(bool async)
        {
            await using var context = Fixture.CreateContext(threadSafetyChecks: false);
            var detector = context.GetService<IConcurrencyDetector>();
            using (await Task.Run(() => detector.EnterCriticalSection()))
            {
                await Assert.ThrowsAsync<DatabaseAccessBlockedException>(() => Query(context, async));
            }
            Assert.Equal(1, context.Guard.OpeningAttempts);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Fixture_uses_real_provider_model_and_blocks_database_open(bool async)
        {
            await using var context = Fixture.CreateContext();
            Assert.True(context.Database.IsOceanBase());
            Assert.Equal(typeof(OceanBaseCompatibilityMode).Assembly,
                context.GetService<IRelationalConnection>().GetType().Assembly);
            var native = Assert.IsType<OceanBaseConnector.OceanBaseConnection>(context.Database.GetDbConnection());
            var configured = new OceanBaseConnector.OceanBaseConnectionStringBuilder(native.ConnectionString);
            Assert.Equal(Fixture.Mode == OceanBaseCompatibilityMode.MySql
                ? OceanBaseConnector.OceanBaseCompatibilityMode.MySql
                : OceanBaseConnector.OceanBaseCompatibilityMode.Oracle, configured.CompatibilityMode);
            Assert.Equal(ValueGenerated.Never, context.Model.FindEntityType(typeof(Product))!
                .FindProperty(nameof(Product.Id))!.ValueGenerated);
            await Assert.ThrowsAsync<DatabaseAccessBlockedException>(async () =>
            {
                if (async) await context.Database.OpenConnectionAsync();
                else context.Database.OpenConnection();
            });
            Assert.Equal(1, context.Guard.OpeningAttempts);
            Assert.Equal(ConnectionState.Closed, native.State);
        }

        private static async Task Query(ConcurrencyDetectorDbContext context, bool async)
        {
            if (async) await context.Products.ToListAsync();
            else context.Products.ToList();
        }
    }

    public abstract class OceanBaseConcurrencyFixture<TFixture>(OceanBaseCompatibilityMode mode)
        : ConcurrencyDetectorTestBase<TFixture>.ConcurrencyDetectorFixtureBase
        where TFixture : OceanBaseConcurrencyFixture<TFixture>, new()
    {
        public OceanBaseCompatibilityMode Mode { get; } = mode;

        protected override ITestStoreFactory TestStoreFactory
            => throw new InvalidOperationException("Pre-execution specifications must not initialize a store.");

        public override Task InitializeAsync() => Task.CompletedTask;
        public override Task DisposeAsync() => Task.CompletedTask;
        public override OfflineContext CreateContext() => CreateContext(threadSafetyChecks: null);

        public OfflineContext CreateContext(bool? threadSafetyChecks)
        {
            var guard = new DatabaseAccessGuard();
            var options = new DbContextOptionsBuilder<ConcurrencyDetectorTestBase<TFixture>.ConcurrencyDetectorDbContext>()
                .UseOceanBase("Server=offline;Database=specification_concurrency", Mode)
                .AddInterceptors(guard);
            if (threadSafetyChecks is { } enabled) options.EnableThreadSafetyChecks(enabled);
            return new OfflineContext(options.Options, guard);
        }

        public sealed class OfflineContext(
            DbContextOptions<ConcurrencyDetectorTestBase<TFixture>.ConcurrencyDetectorDbContext> options,
            DatabaseAccessGuard guard)
            : ConcurrencyDetectorTestBase<TFixture>.ConcurrencyDetectorDbContext(options)
        {
            public DatabaseAccessGuard Guard { get; } = guard;

            protected override void OnModelCreating(ModelBuilder modelBuilder)
                => modelBuilder.Entity<ConcurrencyDetectorTestBase<TFixture>.Product>()
                    .Property(product => product.Id).ValueGeneratedNever();
        }
    }

    public sealed class DatabaseAccessBlockedException()
        : InvalidOperationException("Concurrency specifications cannot access a database.");

    public sealed class DatabaseAccessGuard : DbConnectionInterceptor
    {
        public int OpeningAttempts { get; private set; }

        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result)
        {
            OpeningAttempts++;
            throw new DatabaseAccessBlockedException();
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            OpeningAttempts++;
            throw new DatabaseAccessBlockedException();
        }
    }
}
