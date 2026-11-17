using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests;

public sealed class LiveNumericStorageTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task All_integer_widths_and_enum_endpoints_round_trip(OceanBaseCompatibilityMode mode, bool async)
        => await WithTable(mode, async, async (context, _, stage) =>
        {
            stage("integer endpoints/nulls");
            var rows = new[]
            {
                new Row { Id = 1, SignedByte = sbyte.MinValue, Byte = byte.MinValue, Int16 = short.MinValue,
                    UInt16 = ushort.MinValue, Int32 = int.MinValue, UInt32 = uint.MinValue,
                    Int64 = long.MinValue, UInt64 = ulong.MinValue, State = State.Minimum },
                new Row { Id = 2, SignedByte = sbyte.MaxValue, Byte = byte.MaxValue, Int16 = short.MaxValue,
                    UInt16 = ushort.MaxValue, Int32 = int.MaxValue, UInt32 = uint.MaxValue,
                    Int64 = long.MaxValue, UInt64 = ulong.MaxValue, State = State.Maximum },
                new Row { Id = 3 }
            };
            foreach (var row in rows) await InsertAndRead(context, row, async);
            stage("captured unsigned/enum predicates");
            var maximum = ulong.MaxValue;
            var state = State.Minimum;
            var query = context.Set<Row>().Where(row => row.UInt64 == maximum || row.State == state).OrderBy(row => row.Id).Select(row => row.Id);
            Assert.Equal(new[] { 1, 2 }, async ? await query.ToArrayAsync() : query.ToArray());
            stage("integer tracked update/delete");
            var tracked = async ? await context.Set<Row>().SingleAsync(row => row.Id == 1) : context.Set<Row>().Single(row => row.Id == 1);
            context.Entry(tracked).CurrentValues.SetValues(rows[1] with { Id = 1 });
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            context.ChangeTracker.Clear();
            Assert.Equal(rows[1] with { Id = 1 }, async ? await context.Set<Row>().AsNoTracking().SingleAsync(row => row.Id == 1) : context.Set<Row>().AsNoTracking().Single(row => row.Id == 1));
            context.Remove(tracked);
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.False(async ? await context.Set<Row>().AnyAsync(row => row.Id == 1) : context.Set<Row>().Any(row => row.Id == 1));
        });

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Boolean_columns_preserve_false_true_null_and_lifted_logic(OceanBaseCompatibilityMode mode, bool async)
        => await WithTable(mode, async, async (context, _, stage) =>
        {
            stage("Boolean writes/readback");
            var flags = new bool?[] { false, true, null };
            var rows = flags.SelectMany(flag => flags.Select(other => new Row { Flag = flag, OtherFlag = other }))
                .Select((row, index) => row with { Id = index + 1 }).ToArray();
            foreach (var row in rows) await InsertAndRead(context, row, async);
            stage("Boolean nullable operator projections");
            var query = context.Set<Row>().OrderBy(row => row.Id).Select(row => new
            {
                row.Id, Not = !row.Flag, And = row.Flag & row.OtherFlag, Or = row.Flag | row.OtherFlag,
                Xor = row.Flag ^ row.OtherFlag, Case = row.Flag == true ? 1 : 0,
                Exists = context.Set<Row>().Any(other => other.Id == row.Id && other.Flag == true)
            });
            var expected = rows.Select(row => new { row.Id, Not = !row.Flag, And = row.Flag & row.OtherFlag,
                Or = row.Flag | row.OtherFlag, Xor = row.Flag ^ row.OtherFlag, Case = row.Flag == true ? 1 : 0,
                Exists = rows.Any(other => other.Id == row.Id && other.Flag == true) }).ToArray();
            Assert.Equal(expected, async ? await query.ToArrayAsync() : query.ToArray());
            stage("Boolean captured predicates");
            foreach (var flag in new bool?[] { false, true, null })
            {
                var filtered = context.Set<Row>().Where(row => row.Flag == flag).OrderBy(row => row.Id).Select(row => row.Id);
                Assert.Equal(rows.Where(row => row.Flag == flag).Select(row => row.Id), async ? await filtered.ToArrayAsync() : filtered.ToArray());
            }
            var negated = context.Set<Row>().Where(row => !(row.Flag ?? false)).OrderBy(row => row.Id).Select(row => row.Id);
            Assert.Equal(new[] { 1, 2, 3, 7, 8, 9 }, async ? await negated.ToArrayAsync() : negated.ToArray());
        });

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Decimal_default_explicit_facets_and_converter_preserve_exact_values(OceanBaseCompatibilityMode mode, bool async)
        => await WithTable(mode, async, async (context, _, stage) =>
        {
            stage("decimal endpoints/scale/nulls");
            var values = new decimal?[] { null, 0m, decimal.MinValue, decimal.MaxValue,
                0.0000000000000000000000000001m, -123.45678901234567890123456789m };
            for (var index = 0; index < values.Length; index++)
            {
                var facet = index == 0 ? (decimal?)null : index % 2 == 0 ? -999.99m : 999.9900m;
                await InsertAndRead(context, new Row { Id = index + 1, Decimal = values[index], Faceted = facet,
                    Converted = facet is null ? null : new Amount(facet.Value) }, async);
            }
            stage("decimal comparand retains its own scale");
            var value = 999.991m;
            var query = context.Set<Row>().Where(row => row.Faceted < value).OrderBy(row => row.Id).Select(row => row.Id);
            Assert.Equal(new[] { 2, 3, 4, 5, 6 }, async ? await query.ToArrayAsync() : query.ToArray());
            stage("decimal tracked update");
            var tracked = async ? await context.Set<Row>().SingleAsync(row => row.Id == 1) : context.Set<Row>().Single(row => row.Id == 1);
            tracked.Decimal = 1.2345678901234567890123456789m;
            tracked.Faceted = -0.01m;
            tracked.Converted = new Amount(0.01m);
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            context.ChangeTracker.Clear();
            Assert.Equal(tracked, async ? await context.Set<Row>().AsNoTracking().SingleAsync(row => row.Id == 1) : context.Set<Row>().AsNoTracking().Single(row => row.Id == 1));
        });

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Native_floating_columns_and_converter_round_trip_finite_boundaries(OceanBaseCompatibilityMode mode, bool async)
        => await WithTable(mode, async, async (context, _, stage) =>
        {
            stage("native floating endpoints/subnormals/nulls");
            var singles = new float?[] { null, 0f, float.MinValue, float.MaxValue, float.Epsilon,
                -float.Epsilon, BitConverter.Int32BitsToSingle(0x00800000), MathF.BitIncrement(1f) };
            // Oracle Double parameter extremes/underflow have a separate unresolved-boundary test.
            var doubles = mode == OceanBaseCompatibilityMode.MySql
                ? new double?[] { null, 0d, double.MinValue, double.MaxValue, double.Epsilon,
                    -double.Epsilon, BitConverter.Int64BitsToDouble(0x0010000000000000), Math.BitIncrement(1d) }
                : new double?[] { null, 0d, -1.25d, 12345.6789d, 1E-20d, -1E-20d, 1E20d, Math.BitIncrement(1d) };
            for (var index = 0; index < singles.Length; index++)
            {
                stage($"floating index={index}");
                await InsertAndRead(context, new Row { Id = index + 1, Single = singles[index], Double = doubles[index],
                    ConvertedDouble = doubles[index] is { } value ? new Measurement(value) : null }, async);
            }
            stage("floating tracked update");
            var tracked = async ? await context.Set<Row>().SingleAsync(row => row.Id == 1) : context.Set<Row>().Single(row => row.Id == 1);
            tracked.Single = -1.25f;
            tracked.Double = Math.PI;
            tracked.ConvertedDouble = new Measurement(-Math.E);
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            context.ChangeTracker.Clear();
            Assert.Equal(tracked, async ? await context.Set<Row>().AsNoTracking().SingleAsync(row => row.Id == 1) : context.Set<Row>().AsNoTracking().Single(row => row.Id == 1));
        });

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Native_floating_literals_preserve_extremes_and_subnormals(OceanBaseCompatibilityMode mode, bool async)
        => await WithTable(mode, async, async (context, _, stage) =>
        {
            stage("Single literal exact bounds");
            var source = context.GetService<IRelationalTypeMappingSource>();
            foreach (var value in new[] { float.MinValue, float.MaxValue, float.Epsilon, -float.Epsilon, 0.1f })
            {
                var literal = source.FindMapping(typeof(float))!.GenerateSqlLiteral(value);
                var sql = "SELECT " + literal + (mode == OceanBaseCompatibilityMode.Oracle ? " AS \"Value\" FROM DUAL" : " AS `Value`");
                var query = context.Database.SqlQueryRaw<float>(sql);
                Assert.Equal(value, Assert.Single(async ? await query.ToArrayAsync() : query.ToArray()));
            }
            stage("Double literal exact bounds");
            foreach (var value in new[] { double.MinValue, double.MaxValue, double.Epsilon, -double.Epsilon, Math.BitIncrement(1d) })
            {
                var literal = source.FindMapping(typeof(double))!.GenerateSqlLiteral(value);
                var sql = "SELECT " + literal + (mode == OceanBaseCompatibilityMode.Oracle ? " AS \"Value\" FROM DUAL" : " AS `Value`");
                var query = context.Database.SqlQueryRaw<double>(sql);
                Assert.Equal(value, Assert.Single(async ? await query.ToArrayAsync() : query.ToArray()));
            }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Oracle_4422_double_parameter_extremes_fail_and_subnormals_lose_value(bool async)
        => await WithTable(OceanBaseCompatibilityMode.Oracle, async, async (context, observer, stage) =>
        {
            stage("unresolved Oracle Double parameter boundary");
            if (async) await context.Database.OpenConnectionAsync();
            else context.Database.OpenConnection();
            Assert.Contains("4.4.2.2", context.Database.GetDbConnection().ServerVersion, StringComparison.Ordinal);
            foreach (var converted in new[] { false, true })
            foreach (var value in new[] { double.MinValue, double.MaxValue })
            {
                var row = new Row { Id = 99, Double = converted ? null : value, ConvertedDouble = converted ? new Measurement(value) : null };
                context.Add(row);
                var writes = observer.ParameterWrites;
                var error = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                    : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
                Assert.Equal(1426, Assert.IsType<OceanBaseException>(error.InnerException).ServerErrorCode);
                Assert.Same(row, Assert.Single(error.Entries).Entity);
                Assert.Equal(writes + 1, observer.ParameterWrites);
                context.ChangeTracker.Clear();
                Assert.False(async ? await context.Set<Row>().AnyAsync() : context.Set<Row>().Any());
            }
            foreach (var value in new[] { double.Epsilon, -double.Epsilon, BitConverter.Int64BitsToDouble(0x0010000000000000) })
            {
                var row = new Row { Id = 1, Double = value, ConvertedDouble = new Measurement(value) };
                context.Add(row);
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                context.ChangeTracker.Clear();
                var stored = async ? await context.Set<Row>().SingleAsync() : context.Set<Row>().Single();
                Assert.Equal(0d, stored.Double);
                Assert.Equal(new Measurement(0d), stored.ConvertedDouble);
                Assert.NotEqual(value, stored.Double);
                context.Remove(stored);
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            }
        });

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Lossy_decimal_narrow_integer_and_nonfinite_writes_fail_before_DML(OceanBaseCompatibilityMode mode, bool async)
        => await WithTable(mode, async, async (context, observer, stage) =>
        {
            stage("invalid numeric writes");
            var invalid = new[] { new Row { Id = 99, Faceted = 1000m }, new Row { Id = 99, Faceted = 0.001m },
                new Row { Id = 99, Converted = new Amount(-1000m) }, new Row { Id = 99, Converted = new Amount(0.001m) },
                new Row { Id = 99, Narrow = mode == OceanBaseCompatibilityMode.MySql ? 128 : 100 } }
                .Concat(new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity }
                    .SelectMany(value => new[] { new Row { Id = 99, Single = (float)value },
                        new Row { Id = 99, Double = value }, new Row { Id = 99, ConvertedDouble = new Measurement(value) } }));
            foreach (var row in invalid)
            {
                context.Add(row);
                var writes = observer.ParameterWrites;
                var error = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                    : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
                Assert.IsAssignableFrom<ArgumentException>(error.InnerException);
                Assert.Same(row, Assert.Single(error.Entries).Entity);
                Assert.Equal(writes, observer.ParameterWrites);
                context.ChangeTracker.Clear();
                Assert.False(async ? await context.Set<Row>().AnyAsync() : context.Set<Row>().Any());
            }
            stage("valid write after rejection");
            await InsertAndRead(context, new Row { Id = 1, Faceted = 12.34m, Narrow = 42, Single = 1.25f }, async);
        });

    private static async Task InsertAndRead(NumericContext context, Row row, bool async)
    {
        context.Add(row);
        Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
        context.ChangeTracker.Clear();
        var query = context.Set<Row>().AsNoTracking().Where(value => value.Id == row.Id);
        Assert.Equal(row, Assert.Single(async ? await query.ToListAsync() : query.ToList()));
    }

    private static Task WithTable(OceanBaseCompatibilityMode mode, bool async,
        Func<NumericContext, LiveStorageTable.Commands, Action<string>, Task> run)
        => LiveStorageTable.WithTable<NumericContext>(mode, async, false,
            (options, table) => new NumericContext(new DbContextOptionsBuilder<NumericContext>(options)
                .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, mode, table), run);

    private sealed class NumericContext(DbContextOptions<NumericContext> options, OceanBaseCompatibilityMode mode, string table) : DbContext(options)
    {
        public OceanBaseCompatibilityMode Mode => mode;
        public string Table => table;
        protected override void OnModelCreating(ModelBuilder model)
        {
            var row = model.Entity<Row>();
            row.ToTable(table);
            row.Property(value => value.Id).ValueGeneratedNever();
            row.Property(value => value.Faceted).HasPrecision(5, 2);
            row.Property(value => value.Converted).HasConversion(value => value!.Value, value => new Amount(value)).HasPrecision(5, 2);
            row.Property(value => value.ConvertedDouble).HasConversion(value => value!.Value, value => new Measurement(value));
            row.Property(value => value.Narrow).HasColumnType(mode == OceanBaseCompatibilityMode.MySql ? "TINYINT" : "NUMBER(2)");
        }
    }

    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => (context.GetType(), ((NumericContext)context).Mode, ((NumericContext)context).Table, designTime);
    }

    private enum State : long { Minimum = long.MinValue, Maximum = long.MaxValue }
    private sealed record Amount(decimal Value);
    private sealed record Measurement(double Value);
    private sealed record Row
    {
        public int Id { get; set; }
        public sbyte? SignedByte { get; set; }
        public byte? Byte { get; set; }
        public short? Int16 { get; set; }
        public ushort? UInt16 { get; set; }
        public int? Int32 { get; set; }
        public uint? UInt32 { get; set; }
        public long? Int64 { get; set; }
        public ulong? UInt64 { get; set; }
        public State? State { get; set; }
        public bool? Flag { get; set; }
        public bool? OtherFlag { get; set; }
        public decimal? Decimal { get; set; }
        public decimal? Faceted { get; set; }
        public Amount? Converted { get; set; }
        public float? Single { get; set; }
        public double? Double { get; set; }
        public Measurement? ConvertedDouble { get; set; }
        public int? Narrow { get; set; }
    }
}
