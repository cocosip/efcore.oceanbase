using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests;

public sealed class LiveBinaryStorageTests
{
    private static readonly Guid Known = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly byte[] Seed = [0, 0x27, 0x5c, 0x0a, 0x7f, 0x80, 0xff, 0];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MySql_4422_rejects_LOB_defaults_without_losing_the_owned_table(bool async)
        => await WithTable(OceanBaseCompatibilityMode.MySql, async, async (context, _, stage) =>
        {
            stage("MySQL LOB DEFAULT server boundary");
            if (async) await context.Database.OpenConnectionAsync();
            else context.Database.OpenConnection();
            Assert.Contains("4.4.2.2", context.Database.GetDbConnection().ServerVersion, StringComparison.Ordinal);
            var table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(context.Table);
            foreach (var value in new[] { "X''", "X'00'" })
            {
                var sql = "ALTER TABLE " + table + " ADD `RejectedLob` LONGBLOB DEFAULT (" + value + ")";
                var error = async ? await Assert.ThrowsAsync<OceanBaseException>(() => context.Database.ExecuteSqlRawAsync(sql))
                    : Assert.Throws<OceanBaseException>(() => context.Database.ExecuteSqlRaw(sql));
                Assert.Equal(1067, error.ServerErrorCode);
                Assert.Equal(1, async ? await context.Set<Row>().CountAsync() : context.Set<Row>().Count());
            }
        });

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Empty_large_binary_defaults_seed_and_updates_remain_distinct_from_null(OceanBaseCompatibilityMode mode, bool async)
        => await WithTable(mode, async, async (context, _, stage) =>
        {
            stage("empty BLOB versus NULL");
            await InsertAndRead(context, new Row { Id = 2 }, async);
            foreach (var empty in new[] { true, false, true })
            {
                var tracked = async ? await context.Set<Row>().SingleAsync(row => row.Id == 2) : context.Set<Row>().Single(row => row.Id == 2);
                tracked.Large = empty ? [] : null;
                tracked.LargeConverted = empty ? new HexValue("") : null;
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                context.ChangeTracker.Clear();
                var query = context.Set<Row>().AsNoTracking().Where(row => row.Id == 2);
                AssertStored(tracked, Assert.Single(async ? await query.ToArrayAsync() : query.ToArray()), mode);
            }
        });

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Maximum_bounded_binary_capacity_round_trips_and_rejects_one_extra_byte(OceanBaseCompatibilityMode mode, bool async)
        => await WithTable(mode, async, async (context, observer, stage) =>
        {
            stage("maximum inline binary capacity");
            await InsertAndRead(context, new Row { Id = 2, Payload = Pattern(context.BinaryLimit) }, async);
            var invalid = new Row { Id = 99, Payload = Pattern(context.BinaryLimit + 1) };
            context.Add(invalid);
            var writes = observer.ParameterWrites;
            var error = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.IsType<ArgumentException>(error.InnerException);
            Assert.Same(invalid, Assert.Single(error.Entries).Entity);
            Assert.Equal(writes, observer.ParameterWrites);
            context.ChangeTracker.Clear();
            Assert.False(async ? await context.Set<Row>().AnyAsync(row => row.Id == 99) : context.Set<Row>().Any(row => row.Id == 99));
        }, binaryLimit: mode == OceanBaseCompatibilityMode.MySql ? 262144 : 2000);

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Bounded_binary_defaults_converters_padding_and_tracked_writes_round_trip(OceanBaseCompatibilityMode mode, bool async)
        => await WithTable(mode, async, async (context, _, stage) =>
        {
            stage("bounded binary parameter/default readback");
            byte[]?[] values = mode == OceanBaseCompatibilityMode.MySql
                ? [null, [], [0], [0, 0x80, 0xff, 0], Seed] : [null, [0], [0, 0x80, 0xff, 0], Seed];
            for (var index = 0; index < values.Length; index++)
            {
                var bytes = values[index];
                var row = new Row { Id = index + 2, Payload = bytes, Fixed = mode == OceanBaseCompatibilityMode.MySql ? bytes : null,
                    Converted = bytes is null ? null : new HexValue(Convert.ToHexString(bytes)) };
                await InsertAndRead(context, row, async);
                Assert.Equal(Known, row.DefaultToken);
                Assert.Equal(Seed, row.DefaultBytes);
            }
            stage("binary query comparand above column capacity");
            var larger = Seed.Append((byte)1).ToArray();
            var query = context.Set<Row>().Where(row => row.Payload == larger);
            Assert.Empty(async ? await query.ToListAsync() : query.ToList());
            stage("binary tracked update/delete");
            var tracked = async ? await context.Set<Row>().SingleAsync(row => row.Id == 2) : context.Set<Row>().Single(row => row.Id == 2);
            tracked.Payload = [0xff, 0x80, 0, 0];
            tracked.Converted = new HexValue("FF800000");
            if (mode == OceanBaseCompatibilityMode.MySql) tracked.Fixed = [0xff];
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            context.ChangeTracker.Clear();
            var updated = async ? await context.Set<Row>().SingleAsync(row => row.Id == 2) : context.Set<Row>().Single(row => row.Id == 2);
            AssertStored(tracked, updated, mode);
            context.Remove(updated);
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.False(async ? await context.Set<Row>().AnyAsync(row => row.Id == 2) : context.Set<Row>().Any(row => row.Id == 2));
        });

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Large_binary_columns_and_converters_preserve_all_bytes(OceanBaseCompatibilityMode mode, bool async)
        => await WithTable(mode, async, async (context, _, stage) =>
        {
            int?[] lengths = [null, 0, 2000, 2001, 65536];
            for (var index = 0; index < lengths.Length; index++)
            {
                stage($"large binary length={lengths[index]}");
                var bytes = lengths[index] is { } length ? Pattern(length) : null;
                await InsertAndRead(context, new Row { Id = index + 2, Large = bytes,
                    LargeConverted = bytes is null ? null : new HexValue(Convert.ToHexString(bytes)) }, async);
            }
            stage("large binary tracked update");
            var tracked = async ? await context.Set<Row>().SingleAsync(row => row.Id == 2) : context.Set<Row>().Single(row => row.Id == 2);
            tracked.Large = Pattern(131073);
            tracked.LargeConverted = new HexValue(Convert.ToHexString(tracked.Large));
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            context.ChangeTracker.Clear();
            var updated = async ? await context.Set<Row>().AsNoTracking().SingleAsync(row => row.Id == 2) : context.Set<Row>().AsNoTracking().Single(row => row.Id == 2);
            AssertStored(tracked, updated, mode);
        });

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Guid_parameters_literals_defaults_and_converters_preserve_value_and_server_bytes(OceanBaseCompatibilityMode mode, bool async)
        => await WithTable(mode, async, async (context, _, stage) =>
        {
            stage("Guid nullable/empty/known/endpoints");
            var values = new Guid?[] { null, Guid.Empty, Known, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff") };
            for (var index = 0; index < values.Length; index++)
            {
                var value = values[index];
                await InsertAndRead(context, new Row { Id = index + 2, Token = value, TextToken = value, BinaryToken = value,
                    VariableBinaryToken = mode == OceanBaseCompatibilityMode.MySql ? value : null,
                    ConvertedToken = value is null ? null : new TokenValue(value.Value) }, async);
                var query = context.Set<Row>().Where(row => row.BinaryToken == value && row.Id == index + 2).Select(row => row.Id);
                Assert.Equal(index + 2, Assert.Single(async ? await query.ToArrayAsync() : query.ToArray()));
            }
            stage("Guid literal predicates");
            var literals = context.Set<Row>().Where(row => row.BinaryToken == EF.Constant(Known) && row.TextToken == EF.Constant(Known))
                .OrderBy(row => row.Id).Select(row => row.Id);
            Assert.Equal(new[] { 1, 4 }, async ? await literals.ToArrayAsync() : literals.ToArray());
            stage("server hexadecimal/text identity");
            var helper = context.GetService<ISqlGenerationHelper>();
            var table = helper.DelimitIdentifier(context.Table);
            var column = helper.DelimitIdentifier(nameof(Row.BinaryToken));
            var hexSql = "SELECT " + (mode == OceanBaseCompatibilityMode.MySql ? "HEX(" : "RAWTOHEX(") + column
                + ") AS " + helper.DelimitIdentifier("Value") + " FROM " + table + " WHERE " + helper.DelimitIdentifier(nameof(Row.Id)) + " = 4";
            var hex = context.Database.SqlQueryRaw<string>(hexSql);
            // EF's GuidToBytesConverter uses Guid.ToByteArray mixed-endian bytes, not RFC/network order.
            Assert.Equal("33221100554477668899AABBCCDDEEFF", Assert.Single(async ? await hex.ToArrayAsync() : hex.ToArray()));
            var textSql = "SELECT " + helper.DelimitIdentifier(nameof(Row.TextToken)) + " AS " + helper.DelimitIdentifier("Value")
                + " FROM " + table + " WHERE " + helper.DelimitIdentifier(nameof(Row.Id)) + " = 4";
            var text = context.Database.SqlQueryRaw<string>(textSql);
            Assert.Equal("00112233-4455-6677-8899-aabbccddeeff", Assert.Single(async ? await text.ToArrayAsync() : text.ToArray()));
            stage("Guid tracked update");
            var tracked = async ? await context.Set<Row>().SingleAsync(row => row.Id == 2) : context.Set<Row>().Single(row => row.Id == 2);
            tracked.Token = Known;
            tracked.TextToken = Known;
            tracked.BinaryToken = Known;
            tracked.ConvertedToken = new TokenValue(Known);
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            context.ChangeTracker.Clear();
            AssertStored(tracked, async ? await context.Set<Row>().AsNoTracking().SingleAsync(row => row.Id == 2)
                : context.Set<Row>().AsNoTracking().Single(row => row.Id == 2), mode);
        });

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Invalid_binary_capacity_and_Oracle_empty_inputs_reject_before_DML(OceanBaseCompatibilityMode mode, bool async)
        => await WithTable(mode, async, async (context, observer, stage) =>
        {
            stage("binary length/transport rejection");
            var invalid = new[] { new Row { Id = 99, Payload = new byte[9] },
                new Row { Id = 99, Converted = new HexValue("000000000000000000") } };
            invalid = mode == OceanBaseCompatibilityMode.MySql
                ? invalid.Append(new Row { Id = 99, Fixed = new byte[9] }).ToArray()
                : invalid.Concat(new[] { new Row { Id = 99, Payload = [] },
                    new Row { Id = 99, Converted = new HexValue("") } }).ToArray();
            foreach (var row in invalid)
            {
                context.Add(row);
                var writes = observer.ParameterWrites;
                var error = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                    : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
                if (row.Payload?.Length == 0 || row.Large?.Length == 0 || row.Converted?.Value == "" || row.LargeConverted?.Value == "")
                    Assert.IsType<NotSupportedException>(error.InnerException);
                else Assert.IsType<ArgumentException>(error.InnerException);
                Assert.Same(row, Assert.Single(error.Entries).Entity);
                Assert.Equal(writes, observer.ParameterWrites);
                context.ChangeTracker.Clear();
                Assert.False(async ? await context.Set<Row>().AnyAsync(value => value.Id == 99) : context.Set<Row>().Any(value => value.Id == 99));
            }
            stage("valid write after rejection");
            await InsertAndRead(context, new Row { Id = 2, Payload = Seed, Token = Guid.Empty }, async);
        });

    private static byte[] Pattern(int length)
        => Enumerable.Range(0, length).Select(index => (byte)(index % 256)).ToArray();

    private static async Task InsertAndRead(BinaryContext context, Row row, bool async)
    {
        context.Add(row);
        Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
        Assert.Empty(Assert.IsType<byte[]>(row.DefaultEmpty));
        context.ChangeTracker.Clear();
        var query = context.Set<Row>().AsNoTracking().Where(value => value.Id == row.Id);
        AssertStored(row, Assert.Single(async ? await query.ToListAsync() : query.ToList()), context.Mode);
    }

    private static void AssertStored(Row expected, Row actual, OceanBaseCompatibilityMode mode)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Payload, actual.Payload);
        Assert.Equal(expected.Fixed is null ? null : expected.Fixed.Concat(new byte[8 - expected.Fixed.Length]).ToArray(), actual.Fixed);
        Assert.Equal(expected.Large, actual.Large);
        Assert.Equal(expected.Converted, actual.Converted);
        Assert.Equal(expected.LargeConverted, actual.LargeConverted);
        Assert.Equal(expected.Token, actual.Token);
        Assert.Equal(expected.TextToken, actual.TextToken);
        Assert.Equal(expected.BinaryToken, actual.BinaryToken);
        Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? expected.VariableBinaryToken : null, actual.VariableBinaryToken);
        Assert.Equal(expected.ConvertedToken, actual.ConvertedToken);
        Assert.Equal(Known, actual.DefaultToken);
        Assert.Equal(Seed, actual.DefaultBytes);
        Assert.Empty(Assert.IsType<byte[]>(actual.DefaultEmpty));
    }

    private static Task WithTable(OceanBaseCompatibilityMode mode, bool async,
        Func<BinaryContext, LiveStorageTable.Commands, Action<string>, Task> run, int binaryLimit = 8)
        => LiveStorageTable.WithTable<BinaryContext>(mode, async, false,
            (options, table) => new BinaryContext(new DbContextOptionsBuilder<BinaryContext>(options)
                .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, mode, table, binaryLimit),
            async (context, observer, stage) =>
            {
                var query = context.Set<Row>().AsNoTracking();
                AssertStored(new Row { Id = 1, Payload = Seed, Converted = new HexValue("0080FF00"), Token = Known,
                    TextToken = Known, BinaryToken = Known, ConvertedToken = new TokenValue(Known), Large = [], LargeConverted = new HexValue("") },
                    Assert.Single(async ? await query.ToArrayAsync() : query.ToArray()), mode);
                await run(context, observer, stage);
            });

    private sealed class BinaryContext(DbContextOptions<BinaryContext> options, OceanBaseCompatibilityMode mode, string table, int binaryLimit) : DbContext(options)
    {
        public OceanBaseCompatibilityMode Mode => mode;
        public string Table => table;
        public int BinaryLimit => binaryLimit;
        protected override void OnModelCreating(ModelBuilder model)
        {
            var row = model.Entity<Row>();
            row.ToTable(table);
            row.Property(value => value.Id).ValueGeneratedNever();
            row.Property(value => value.Payload).HasMaxLength(binaryLimit);
            if (mode == OceanBaseCompatibilityMode.MySql) row.Property(value => value.Fixed).IsFixedLength().HasMaxLength(8);
            else row.Ignore(value => value.Fixed);
            row.Property(value => value.Large).HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "BLOB" : "LONGBLOB");
            row.Property(value => value.Converted).HasConversion(value => Convert.FromHexString(value!.Value), value => new HexValue(Convert.ToHexString(value))).HasMaxLength(8);
            row.Property(value => value.LargeConverted).HasConversion(value => Convert.FromHexString(value!.Value), value => new HexValue(Convert.ToHexString(value)))
                .HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "BLOB" : "LONGBLOB");
            row.Property(value => value.TextToken).HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "VARCHAR2(36 CHAR)" : "VARCHAR(36)");
            row.Property(value => value.BinaryToken).HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "RAW(16)" : "BINARY(16)");
            if (mode == OceanBaseCompatibilityMode.MySql) row.Property(value => value.VariableBinaryToken).HasColumnType("VARBINARY(16)");
            else row.Ignore(value => value.VariableBinaryToken);
            row.Property(value => value.ConvertedToken).HasConversion(value => value!.Value, value => new TokenValue(value));
            row.Property(value => value.DefaultToken).HasDefaultValue(Known);
            row.Property(value => value.DefaultBytes).HasMaxLength(8).HasDefaultValue(Seed);
            row.Property(value => value.DefaultEmpty).HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "BLOB" : "VARBINARY(8)")
                .HasDefaultValue(Array.Empty<byte>());
            row.HasData(new Row { Id = 1, Payload = Seed, Converted = new HexValue("0080FF00"), Token = Known,
                TextToken = Known, BinaryToken = Known, ConvertedToken = new TokenValue(Known), Large = [], LargeConverted = new HexValue("") });
        }
    }

    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => (context.GetType(), ((BinaryContext)context).Mode, ((BinaryContext)context).Table,
                ((BinaryContext)context).BinaryLimit, designTime);
    }

    private sealed record HexValue(string Value);
    private sealed record TokenValue(Guid Value);
    private sealed class Row
    {
        public int Id { get; set; }
        public byte[]? Payload { get; set; }
        public byte[]? Fixed { get; set; }
        public byte[]? Large { get; set; }
        public HexValue? Converted { get; set; }
        public HexValue? LargeConverted { get; set; }
        public Guid? Token { get; set; }
        public Guid? TextToken { get; set; }
        public Guid? BinaryToken { get; set; }
        public Guid? VariableBinaryToken { get; set; }
        public TokenValue? ConvertedToken { get; set; }
        public Guid? DefaultToken { get; set; }
        public byte[]? DefaultBytes { get; set; }
        public byte[]? DefaultEmpty { get; set; }
    }
}
