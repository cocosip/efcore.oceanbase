using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class LiveLocalTimeZoneTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public Task Oracle_local_timestamps_bind_and_read_exact_session_wall_clock_values(bool async)
            => WithTable(async, async (context, observer, stage) =>
            {
                if (async) await context.Database.OpenConnectionAsync();
                else context.Database.OpenConnection();
                var originalQuery = context.Database.SqlQueryRaw<string>("SELECT SESSIONTIMEZONE AS \"Value\" FROM DUAL");
                var original = async ? await originalQuery.SingleAsync() : originalQuery.Single();
                var stamp = new DateTime(2024, 1, 15, 12, 0, 0).AddTicks(1234567);
                var milliseconds = new DateTime(2024, 1, 15, 12, 0, 0).AddMilliseconds(123);
                var shift = TimeSpan.FromMinutes(345);
                var table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(context.Table);
                try
                {
                    for (var index = 0; index < 2; index++)
                    {
                        var zone = index == 0 ? "+00:00" : "+05:45";
                        stage("parameter write at session " + zone);
                        await Zone(zone);
                        context.Add(new Row { Id = index + 1, WallClock = stamp, Milliseconds = milliseconds, Wide = stamp });
                        var writes = observer.ParameterWrites;
                        Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                        Assert.Equal(writes + 1, observer.ParameterWrites);
                        context.ChangeTracker.Clear();
                        var storedQuery = context.Set<Row>().AsNoTracking().Where(row => row.Id == index + 1);
                        var stored = async ? await storedQuery.SingleAsync() : storedQuery.Single();
                        Assert.Equal(stamp, stored.WallClock);
                        Assert.Equal(stamp, stored.Wide);
                        Assert.Equal(milliseconds, stored.Milliseconds);
                        Assert.Equal(DateTimeKind.Unspecified, stored.WallClock!.Value.Kind);

                        using var command = context.Database.GetDbConnection().CreateCommand();
                        command.CommandText = "SELECT \"WallClock\" FROM " + table + " WHERE \"Id\" = " + (index + 1);
                        var native = Assert.IsType<OceanBaseTimestampLocalTimeZone>(async
                            ? await command.ExecuteScalarAsync() : command.ExecuteScalar());
                        Assert.Equal(zone, native.SessionTimeZone);
                        Assert.Equal(stamp, native.ToDateTime());
                    }

                    context.Add(new Row { Id = 3 });
                    Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                    context.ChangeTracker.Clear();
                    foreach (var zone in new[] { "+00:00", "+05:45" })
                    {
                        stage("stored instant renders in session " + zone);
                        await Zone(zone);
                        var rows = context.Set<Row>().AsNoTracking().OrderBy(row => row.Id);
                        var stored = async ? await rows.ToArrayAsync() : rows.ToArray();
                        Assert.Equal(3, stored.Length);
                        var sessionShift = zone == "+00:00" ? TimeSpan.Zero : shift;
                        Assert.Equal(stamp + sessionShift, stored[0].WallClock);
                        Assert.Equal(stamp + sessionShift - shift, stored[1].WallClock);
                        Assert.Null(stored[2].WallClock);
                        Assert.Null(stored[2].Wide);
                        var chronological = context.Set<Row>().Where(row => row.WallClock != null)
                            .OrderBy(row => row.WallClock).Select(row => row.Id);
                        Assert.Equal(new[] { 2, 1 }, async ? await chronological.ToArrayAsync() : chronological.ToArray());

                        var expected = stamp + sessionShift;
                        var match = context.Set<Row>().Where(row => row.WallClock == expected && row.WallClock == EF.Constant(expected));
                        Assert.Equal(1, Assert.Single(async ? await match.ToArrayAsync() : match.ToArray()).Id);
                        var rawSql = "SELECT \"WallClock\" AS \"Value\" FROM " + table + " WHERE \"Id\" = 1";
                        var raw = context.Database.SqlQueryRaw<DateTime>(rawSql);
                        Assert.Equal(expected, async ? await raw.SingleAsync() : raw.Single());
                        var comparand = milliseconds + sessionShift + TimeSpan.FromTicks(1);
                        var precise = context.Set<Row>().Where(row => row.Id == 1 && row.Milliseconds < comparand
                            && row.Milliseconds < EF.Constant(comparand));
                        Assert.Single(async ? await precise.ToArrayAsync() : precise.ToArray());
                        context.ChangeTracker.Clear();
                    }

                    stage("update and literal write use current session local fields");
                    var update = async ? await context.Set<Row>().SingleAsync(row => row.Id == 1)
                        : context.Set<Row>().Single(row => row.Id == 1);
                    update.WallClock = stamp.AddHours(2);
                    Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                    context.ChangeTracker.Clear();
                    var mapping = context.Model.FindEntityType(typeof(Row))!.FindProperty(nameof(Row.WallClock))!.GetRelationalTypeMapping();
                    await Execute(context, "INSERT INTO " + table + " (\"Id\", \"WallClock\") VALUES (4, " + mapping.GenerateSqlLiteral(stamp) + ")", async);
                    await Zone("+00:00");
                    Assert.Equal(stamp.AddHours(2) - shift, (async ? await context.Set<Row>().AsNoTracking().SingleAsync(row => row.Id == 1)
                        : context.Set<Row>().AsNoTracking().Single(row => row.Id == 1)).WallClock);
                    Assert.Equal(stamp - shift, (async ? await context.Set<Row>().AsNoTracking().SingleAsync(row => row.Id == 4)
                        : context.Set<Row>().AsNoTracking().Single(row => row.Id == 4)).WallClock);

                    stage("write precision and Kind reject before DML");
                    foreach (var invalid in new[] { new Row { Id = 5, Milliseconds = milliseconds.AddTicks(1) },
                        new Row { Id = 5, WallClock = DateTime.SpecifyKind(stamp, DateTimeKind.Utc) } })
                    {
                        context.Add(invalid);
                        var writes = observer.ParameterWrites;
                        var error = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                            : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
                        Assert.IsType<ArgumentException>(error.InnerException);
                        Assert.Equal(writes, observer.ParameterWrites);
                        context.ChangeTracker.Clear();
                    }

                    stage("subtick local timestamp rejects lossy DateTime materialization");
                    var exact = context.Database.SqlQueryRaw<DateTime>("SELECT CAST(TIMESTAMP '2024-01-15 12:00:00.123456789' "
                        + "AS TIMESTAMP(9) WITH LOCAL TIME ZONE) AS \"Value\" FROM DUAL");
                    var rejected = async ? await Record.ExceptionAsync(async () => await exact.ToArrayAsync())
                        : Record.Exception(() => exact.ToArray());
                    Assert.IsType<OverflowException>(rejected?.InnerException ?? rejected);
                }
                finally
                {
                    await Zone(original);
                }

                Task Zone(string zone) => Execute(context, "ALTER SESSION SET TIME_ZONE = '" + zone.Replace("'", "''", StringComparison.Ordinal) + "'", async);
            });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public Task Oracle_local_timestamp_ddl_and_scaffolding_preserve_fractional_precision(bool async)
            => WithTable(async, async (context, _, stage) =>
            {
                stage("generated create SQL retains LTZ declarations");
                var script = context.Database.GenerateCreateScript();
                Assert.Contains("\"WallClock\" TIMESTAMP(7) WITH LOCAL TIME ZONE", script);
                Assert.Contains("\"Milliseconds\" TIMESTAMP(3) WITH LOCAL TIME ZONE", script);
                Assert.Contains("\"Wide\" TIMESTAMP(9) WITH LOCAL TIME ZONE", script);

                stage("native catalog scaffolds DateTime and exact LTZ facets");
    #pragma warning disable EF1001 // Exercise actual provider design services against native metadata.
                using var services = (ServiceProvider)new DesignTimeServicesBuilder(typeof(LiveLocalTimeZoneTests).Assembly,
                    typeof(LiveLocalTimeZoneTests).Assembly, new OperationReporter(null), [])
                    .Build(typeof(OceanBaseCompatibilityMode).Assembly.GetName().Name!);
    #pragma warning restore EF1001
                var database = services.GetRequiredService<IDatabaseModelFactory>().Create(context.Database.GetDbConnection(),
                    new DatabaseModelFactoryOptions(tables: [context.Table]));
                var scaffolded = services.GetRequiredService<IScaffoldingModelFactory>().Create(database,
                    new ModelReverseEngineerOptions { UseDatabaseNames = true, NoPluralize = true });
                var entity = Assert.Single(scaffolded.GetEntityTypes());
                foreach (var (name, precision) in new[] { (nameof(Row.WallClock), 7), (nameof(Row.Milliseconds), 3), (nameof(Row.Wide), 9) })
                {
                    var property = entity.FindProperty(name)!;
                    Assert.Equal(typeof(DateTime?), property.ClrType);
                    Assert.Equal($"TIMESTAMP({precision}) WITH LOCAL TIME ZONE", property.GetColumnType(), ignoreCase: true);
                }

                stage("scaffolded declarations survive migration operations");
                var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, scaffolded.GetRelationalModel());
                var create = Assert.Single(operations.OfType<CreateTableOperation>());
                foreach (var column in create.Columns.Where(column => column.Name != nameof(Row.Id)))
                    Assert.EndsWith(" WITH LOCAL TIME ZONE", column.ColumnType!, StringComparison.OrdinalIgnoreCase);
                await Task.CompletedTask;
            });

        private static async Task Execute(LocalContext context, string sql, bool async)
        {
            if (async) await context.Database.ExecuteSqlRawAsync(sql);
            else context.Database.ExecuteSqlRaw(sql);
        }

        private static Task WithTable(bool async, Func<LocalContext, LiveStorageTable.Commands, Action<string>, Task> run)
            => LiveStorageTable.WithTable<LocalContext>(OceanBaseCompatibilityMode.Oracle, async, false,
                (options, table) => new LocalContext(new DbContextOptionsBuilder<LocalContext>(options)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table), run);

        private sealed class LocalContext(DbContextOptions<LocalContext> options, string table) : DbContext(options)
        {
            public string Table => table;
            protected override void OnModelCreating(ModelBuilder model)
            {
                var row = model.Entity<Row>();
                row.ToTable(table);
                row.Property(value => value.Id).ValueGeneratedNever();
                row.Property(value => value.WallClock).HasColumnType("TIMESTAMP(7) WITH LOCAL TIME ZONE");
                row.Property(value => value.Milliseconds).HasColumnType("TIMESTAMP WITH LOCAL TIME ZONE").HasPrecision(3);
                row.Property(value => value.Wide).HasColumnType("TIMESTAMP(9) WITH LOCAL TIME ZONE");
            }
        }

        private sealed class CacheKey : IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime) => (context.GetType(), ((LocalContext)context).Table, designTime);
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public DateTime? WallClock { get; set; }
            public DateTime? Milliseconds { get; set; }
            public DateTime? Wide { get; set; }
        }
    }
}
