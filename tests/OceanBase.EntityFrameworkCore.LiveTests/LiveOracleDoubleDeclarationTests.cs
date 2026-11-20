using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests;

public sealed class LiveOracleDoubleDeclarationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_declarations_and_decimal_conversion_have_distinct_precision_contracts(bool async)
        => await WithTable(async, async (context, observer, stage) =>
        {
            stage("model and actual declarations");
            var entity = context.Model.FindEntityType(typeof(Row))!;
            Assert.Equal("BINARY_DOUBLE", entity.FindProperty(nameof(Row.Native))!.GetColumnType());
            Assert.Equal("BINARY_DOUBLE", entity.FindProperty(nameof(Row.Explicit))!.GetColumnType());
            Assert.Equal("NUMBER(18,6)", entity.FindProperty(nameof(Row.Fixed))!.GetColumnType());
            var source = context.GetService<IRelationalTypeMappingSource>();
            var nativePrecision = new ModelBuilder().Entity<Row>().Property(value => value.Native)
                .HasColumnType("BINARY_DOUBLE").HasPrecision(17).Metadata;
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping((IProperty)nativePrecision));
            var decimalWithoutConversion = new ModelBuilder().Entity<Row>().Property(value => value.Native)
                .HasColumnType("NUMBER(18,6)").Metadata;
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping((IProperty)decimalWithoutConversion));
            var catalog = context.Database.SqlQueryRaw<string>(
                "SELECT COLUMN_NAME || ':' || DATA_TYPE || ':' || NVL(TO_CHAR(DATA_PRECISION), 'NULL') || ':' || NVL(TO_CHAR(DATA_SCALE), 'NULL') AS \"Value\" "
                + "FROM USER_TAB_COLUMNS WHERE TABLE_NAME = {0} AND COLUMN_NAME <> 'Id' ORDER BY COLUMN_NAME", context.Table);
            Assert.Equal(new[] { "Explicit:BINARY_DOUBLE:NULL:NULL", "Fixed:NUMBER:18:6", "Native:BINARY_DOUBLE:NULL:NULL" },
                async ? await catalog.ToArrayAsync() : catalog.ToArray());

            stage("native ordinary value and fixed-domain conversion");
            var row = new Row { Id = 1, Native = 123.456789123d, Explicit = 123.456789123d, Fixed = 123.456789d };
            context.Add(row);
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            context.ChangeTracker.Clear();
            Assert.Equal(row, async ? await context.Set<Row>().SingleAsync() : context.Set<Row>().Single());

            stage("decimal conversion excess scale rejects before DML");
            var invalid = new Row { Id = 2, Fixed = 123.456789123d };
            context.Add(invalid);
            var writes = observer.ParameterWrites;
            var error = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.IsAssignableFrom<ArgumentException>(error.InnerException);
            Assert.Same(invalid, Assert.Single(error.Entries).Entity);
            Assert.Equal(writes, observer.ParameterWrites);
            context.ChangeTracker.Clear();

            stage("server NUMBER declared-scale rounding control");
            await Execute(context, "UPDATE \"" + context.Table + "\" SET \"Fixed\" = 123.456789123D WHERE \"Id\" = 1", async);
            var stored = async ? await context.Set<Row>().SingleAsync() : context.Set<Row>().Single();
            Assert.Equal(123.456789d, stored.Fixed);
            Assert.Equal(row.Native, stored.Native);
            Assert.Equal(row.Explicit, stored.Explicit);
        });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Native_double_parameters_and_literals_preserve_the_same_range(bool async, bool explicitType)
        => await WithTable(async, async (context, observer, stage) =>
        {
            stage("versioned native parameter versus literal storage");
            if (async) await context.Database.OpenConnectionAsync();
            else context.Database.OpenConnection();
            Assert.Contains("4.4.2.2", context.Database.GetDbConnection().ServerVersion, StringComparison.Ordinal);
            var column = explicitType ? nameof(Row.Explicit) : nameof(Row.Native);
            var mapping = (RelationalTypeMapping)context.Model.FindEntityType(typeof(Row))!.FindProperty(column)!.GetTypeMapping();
            Assert.Equal("BINARY_DOUBLE", mapping.StoreType);
            foreach (var value in new[] { double.MinValue, double.MaxValue, double.Epsilon, -double.Epsilon, 1E-131d, -1E-131d, 1E-120d })
            {
                stage($"{column} value={value:R} parameter/literal");
                var row = new Row { Id = 1, Native = explicitType ? null : value, Explicit = explicitType ? value : null };
                context.Add(row);
                var writes = observer.ParameterWrites;
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                context.ChangeTracker.Clear();
                var stored = async ? await context.Set<Row>().SingleAsync() : context.Set<Row>().Single();
                Assert.Equal(BitConverter.DoubleToInt64Bits(value),
                    BitConverter.DoubleToInt64Bits((explicitType ? stored.Explicit : stored.Native)!.Value));
                Assert.Equal(writes + 1, observer.ParameterWrites);
                context.ChangeTracker.Clear();
                await Execute(context, "DELETE FROM \"" + context.Table + "\"", async);

                // The same native column must actually retain each literal, not merely SELECT it.
                await Execute(context, "INSERT INTO \"" + context.Table + "\" (\"Id\", \"" + column + "\") VALUES (1, "
                    + mapping.GenerateSqlLiteral(value) + ")", async);
                var literalStored = async ? await context.Set<Row>().SingleAsync() : context.Set<Row>().Single();
                Assert.Equal(BitConverter.DoubleToInt64Bits(value),
                    BitConverter.DoubleToInt64Bits((explicitType ? literalStored.Explicit : literalStored.Native)!.Value));
                context.ChangeTracker.Clear();
                await Execute(context, "DELETE FROM \"" + context.Table + "\"", async);
            }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_double_parameter_precision_preserves_range(bool async)
        => await WithTable(async, async (context, _, stage) =>
        {
            stage("direct public Connector parameter precision control");
            if (async) await context.Database.OpenConnectionAsync();
            else context.Database.OpenConnection();
            Assert.Contains("4.4.2.2", context.Database.GetDbConnection().ServerVersion, StringComparison.Ordinal);
            foreach (var precision in new byte[] { 0, 17 })
            foreach (var value in new[] { double.MaxValue, 1E-131d, 1E-120d })
            {
                var sql = "INSERT INTO \"" + context.Table + "\" (\"Id\", \"Explicit\") VALUES (1, :p)";
                var parameter = new OceanBaseParameter { ParameterName = "p", DbType = DbType.Double, Precision = precision, Value = value };
                Assert.Equal(1, async ? await context.Database.ExecuteSqlRawAsync(sql, parameter) : context.Database.ExecuteSqlRaw(sql, parameter));
                var stored = async ? await context.Set<Row>().SingleAsync() : context.Set<Row>().Single();
                Assert.Equal(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(stored.Explicit!.Value));
                context.ChangeTracker.Clear();
                await Execute(context, "DELETE FROM \"" + context.Table + "\"", async);
            }
        });

    private static async Task Execute(DoubleContext context, string sql, bool async)
    {
        if (async) await context.Database.ExecuteSqlRawAsync(sql);
        else context.Database.ExecuteSqlRaw(sql);
    }

    private static Task WithTable(bool async, Func<DoubleContext, LiveStorageTable.Commands, Action<string>, Task> run)
        => LiveStorageTable.WithTable<DoubleContext>(OceanBaseCompatibilityMode.Oracle, async, false,
            (options, table) => new DoubleContext(new DbContextOptionsBuilder<DoubleContext>(options)
                .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table), run);

    private sealed class DoubleContext(DbContextOptions<DoubleContext> options, string table) : DbContext(options)
    {
        public string Table => table;
        protected override void OnModelCreating(ModelBuilder model)
        {
            var row = model.Entity<Row>();
            row.ToTable(table);
            row.Property(value => value.Id).ValueGeneratedNever();
            row.Property(value => value.Explicit).HasColumnType("BINARY_DOUBLE");
            row.Property(value => value.Fixed).HasConversion<decimal>().HasPrecision(18, 6);
        }
    }

    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (context.GetType(), ((DoubleContext)context).Table, designTime);
    }

    private sealed record Row
    {
        public int Id { get; set; }
        public double? Native { get; set; }
        public double? Explicit { get; set; }
        public double? Fixed { get; set; }
    }
}
