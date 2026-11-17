using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests;

public sealed class LiveTextStorageTests
{
    private const string SeedText = "海🌊\r\n'\\";

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
    public async Task Bounded_text_defaults_converters_and_tracked_writes_round_trip(
        OceanBaseCompatibilityMode mode, bool async, bool noBackslashEscapes)
    {
        if (Environment.GetEnvironmentVariable("OCEANBASE_RUN_LIVE_TESTS") != "1"
            || Environment.GetEnvironmentVariable("OCEANBASE_RUN_LIVE_SCHEMA_TESTS") != "1")
            throw new InvalidOperationException("Set OCEANBASE_RUN_LIVE_TESTS=1 and OCEANBASE_RUN_LIVE_SCHEMA_TESTS=1 to create owned live test tables.");
        var key = mode == OceanBaseCompatibilityMode.MySql ? "ConnectionStrings:OceanBaseMySql" : "ConnectionStrings:OceanBaseOracle";
        var configured = new ConfigurationBuilder().AddUserSecrets<LiveConnectionTests>(optional: false).Build()[key];
        if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException($"Missing User Secret '{key}'.");
        // Connector's public Oracle RETURNING path requires this explicit write-profile opt-in.
        if (mode == OceanBaseCompatibilityMode.Oracle)
            configured = new OceanBaseConnectionStringBuilder(configured) { UseOraclePrepareExecute = true }.ConnectionString;
        var table = "EFT_" + Guid.NewGuid().ToString("N")[..24];
        var observer = new Commands();
        var stage = "scope";
        Exception? failure = null;
        try
        {
            await using var probe = new DbContext(new DbContextOptionsBuilder().UseOceanBase(configured, mode).Options);
            var scopeSql = mode == OceanBaseCompatibilityMode.MySql
                ? "SELECT DATABASE() AS `Value`"
                : "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') AS \"Value\" FROM DUAL";
            var scopeQuery = probe.Database.SqlQueryRaw<string>(scopeSql);
            var scope = Assert.Single(async ? await scopeQuery.ToListAsync() : scopeQuery.ToList());
            Assert.False(string.IsNullOrWhiteSpace(scope));
            await using var context = new TextContext(new DbContextOptionsBuilder<TextContext>()
                .UseOceanBase(configured, mode).EnableOceanBaseSchemaManagement(scope)
                .ReplaceService<IModelCacheKeyFactory, CacheKey>().AddInterceptors(observer).Options, mode, table);
            if (noBackslashEscapes)
            {
                if (async) await context.Database.OpenConnectionAsync();
                else context.Database.OpenConnection();
                const string sqlMode = "SET SESSION sql_mode = CONCAT(@@SESSION.sql_mode, ',NO_BACKSLASH_ESCAPES')";
                if (async) await context.Database.ExecuteSqlRawAsync(sqlMode);
                else context.Database.ExecuteSqlRaw(sqlMode);
            }
            observer.CreateSql = "CREATE TABLE " + context.GetService<ISqlGenerationHelper>().DelimitIdentifier(table);
            stage = "CreateTables/defaults/HasData";
            var creator = context.GetService<IRelationalDatabaseCreator>();
            if (async) await creator.CreateTablesAsync();
            else creator.CreateTables();
            Assert.True(observer.OwnsTable);
            var seeded = Assert.Single(async
                ? await context.Set<Row>().AsNoTracking().ToListAsync()
                : context.Set<Row>().AsNoTracking().ToList());
            Assert.Equal(SeedText, seeded.Plain);
            Assert.Equal(SeedText, seeded.National);
            Assert.Equal(SeedText, seeded.DefaultPlain);
            Assert.Equal(SeedText, seeded.DefaultNational);
            Assert.Equal(new TextValue("seed"), seeded.Converted);

            stage = "parameter inserts/materialization";
            var texts = new string?[] { null, "", "Ready's\\\r\n", "海🌊\n", string.Concat(Enumerable.Repeat("🌊", 32)) };
            for (var index = 0; index < texts.Length; index++)
            {
                var row = new Row { Id = index + 2, Plain = texts[index], National = texts[index],
                    Fixed = "fix", NationalFixed = "海", Converted = new TextValue("value") };
                context.Add(row);
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                Assert.Equal(SeedText, row.DefaultPlain);
                Assert.Equal(SeedText, row.DefaultNational);
                context.ChangeTracker.Clear();
                var query = context.Set<Row>().AsNoTracking().Where(value => value.Id == row.Id);
                var stored = Assert.Single(async ? await query.ToListAsync() : query.ToList());
                var expected = mode == OceanBaseCompatibilityMode.Oracle && texts[index] == "" ? null : texts[index];
                Assert.Equal(expected, stored.Plain);
                Assert.Equal(expected, stored.National);
                Assert.Equal(mode == OceanBaseCompatibilityMode.Oracle ? "fix".PadRight(8) : "fix", stored.Fixed);
                Assert.Equal(mode == OceanBaseCompatibilityMode.Oracle ? "海".PadRight(8) : "海", stored.NationalFixed);
                Assert.Equal(new TextValue("value"), stored.Converted);
            }
            Assert.True(observer.ParameterWrites >= texts.Length);

            stage = "length validation";
            foreach (var converted in new[] { false, true })
            {
                var invalid = new Row { Id = 99, Plain = converted ? "ok" : new string('a', 33),
                    Converted = new TextValue(converted ? new string('b', 9) : "ok") };
                context.Add(invalid);
                var writes = observer.ParameterWrites;
                var error = async
                    ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                    : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
                Assert.IsType<ArgumentException>(error.InnerException);
                Assert.Equal(writes, observer.ParameterWrites);
                context.ChangeTracker.Clear();
            }

            stage = "tracked update/delete";
            var tracked = async ? await context.Set<Row>().SingleAsync(value => value.Id == 2)
                : context.Set<Row>().Single(value => value.Id == 2);
            tracked.Plain = "updated\r\n海";
            tracked.National = "updated🌊\n";
            tracked.Converted = new TextValue("changed");
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            context.ChangeTracker.Clear();
            var updated = async ? await context.Set<Row>().SingleAsync(value => value.Id == 2)
                : context.Set<Row>().Single(value => value.Id == 2);
            Assert.Equal(tracked.Plain, updated.Plain);
            Assert.Equal(tracked.National, updated.National);
            Assert.Equal(tracked.Converted, updated.Converted);
            context.Remove(updated);
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.False(async ? await context.Set<Row>().AnyAsync(value => value.Id == 2)
                : context.Set<Row>().Any(value => value.Id == 2));
        }
        catch (Exception error)
        {
            failure = SafeFailure(mode, table, stage, error, configured);
            throw failure;
        }
        finally
        {
            if (observer.OwnsTable)
            {
                try
                {
                    await using var cleanup = new DbContext(new DbContextOptionsBuilder().UseOceanBase(configured, mode).Options);
                    var identifier = cleanup.GetService<ISqlGenerationHelper>().DelimitIdentifier(table);
                    var dropSql = "DROP TABLE " + identifier;
                    if (async) await cleanup.Database.ExecuteSqlRawAsync(dropSql);
                    else cleanup.Database.ExecuteSqlRaw(dropSql);
                    var existsSql = mode == OceanBaseCompatibilityMode.MySql
                        ? "SELECT table_name AS `Value` FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = {0}"
                        : "SELECT TABLE_NAME AS \"Value\" FROM USER_TABLES WHERE TABLE_NAME = {0}";
                    var remaining = cleanup.Database.SqlQueryRaw<string>(existsSql, table);
                    Assert.Empty(async ? await remaining.ToListAsync() : remaining.ToList());
                }
                catch (Exception error)
                {
                    var cleanupFailure = SafeFailure(mode, table, "cleanup; inspect owned table", error, configured);
                    if (failure is null) throw cleanupFailure;
                    throw new AggregateException(failure, cleanupFailure);
                }
            }
        }
    }

    private static Exception SafeFailure(OceanBaseCompatibilityMode mode, string table, string stage, Exception error, string configured)
    {
        if (error is Xunit.Sdk.XunitException) return error;
        // Do not attach Connector exceptions: authentication diagnostics can contain connection details.
        var types = error.GetType().Name;
        var code = 0;
        var detail = "";
        for (var nested = error; nested is not null; nested = nested.InnerException)
        {
            if (nested is OceanBaseException native) code = native.ServerErrorCode;
            if (nested != error) types += "/" + nested.GetType().Name;
            detail = nested.Message + "\n" + nested.StackTrace;
        }
        var settings = new OceanBaseConnectionStringBuilder(configured);
        foreach (var value in new[] { configured, settings.Password, settings.UserId, settings.Server, settings.Database })
            if (!string.IsNullOrEmpty(value)) detail = detail.Replace(value, "[redacted]", StringComparison.OrdinalIgnoreCase);
        return new InvalidOperationException($"{mode}: table={table}; stage={stage}; error={types}; server-code={code}; detail={detail}");
    }

    private sealed class TextContext(DbContextOptions<TextContext> options, OceanBaseCompatibilityMode mode, string table) : DbContext(options)
    {
        public OceanBaseCompatibilityMode Mode => mode;
        public string Table => table;

        protected override void OnModelCreating(ModelBuilder model)
        {
            var row = model.Entity<Row>();
            row.ToTable(table);
            row.Property(value => value.Id).ValueGeneratedNever();
            row.Property(value => value.Plain).IsUnicode(false).HasMaxLength(32);
            row.Property(value => value.National).IsUnicode().HasMaxLength(32);
            row.Property(value => value.Fixed).IsUnicode(false).IsFixedLength().HasMaxLength(8);
            row.Property(value => value.NationalFixed).IsUnicode().IsFixedLength().HasMaxLength(8);
            row.Property(value => value.Converted).HasConversion(value => value!.Value, value => new TextValue(value)).HasMaxLength(8);
            row.Property(value => value.DefaultPlain).IsUnicode(false).HasMaxLength(32).HasDefaultValue(SeedText);
            row.Property(value => value.DefaultNational).IsUnicode().HasMaxLength(32).HasDefaultValue(SeedText);
            row.HasData(new Row { Id = 1, Plain = SeedText, National = SeedText, Converted = new TextValue("seed") });
        }
    }

    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => (context.GetType(), ((TextContext)context).Mode, ((TextContext)context).Table, designTime);
    }

    private sealed record TextValue(string Value);
    private sealed class Row
    {
        public int Id { get; set; }
        public string? Plain { get; set; }
        public string? National { get; set; }
        public string? Fixed { get; set; }
        public string? NationalFixed { get; set; }
        public TextValue? Converted { get; set; }
        public string? DefaultPlain { get; set; }
        public string? DefaultNational { get; set; }
    }

    private sealed class Commands : DbCommandInterceptor
    {
        public string CreateSql { get; set; } = "";
        public bool OwnsTable { get; private set; }
        public int ParameterWrites { get; private set; }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            if (eventData.CommandSource == CommandSource.SaveChanges && command.Parameters.Count > 0) ParameterWrites++;
            return result;
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(NonQueryExecuting(command, eventData, result));
        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        {
            if (command.CommandText.StartsWith(CreateSql + " (", StringComparison.Ordinal)) OwnsTable = true;
            return result;
        }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(NonQueryExecuted(command, eventData, result));
    }
}
