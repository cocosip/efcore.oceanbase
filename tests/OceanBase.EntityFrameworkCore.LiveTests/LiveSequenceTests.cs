using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.Configuration;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests;

public sealed class LiveSequenceTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Creates_alters_restarts_and_drops_an_owned_model_sequence(OceanBaseCompatibilityMode mode, bool async)
    {
        if (Environment.GetEnvironmentVariable("OCEANBASE_RUN_LIVE_TESTS") != "1"
            || Environment.GetEnvironmentVariable("OCEANBASE_RUN_LIVE_SCHEMA_TESTS") != "1")
            throw new InvalidOperationException("Enable the live and schema test opt-ins.");

        var configured = new ConfigurationBuilder().AddUserSecrets<LiveConnectionTests>(optional: false)
            .Build()["ConnectionStrings:OceanBase" + mode] ?? throw new InvalidOperationException("Missing test connection.");
        var connectionString = LiveTestConfiguration.ConnectionString(configured);
        var name = "EFT_SEQ_" + Guid.NewGuid().ToString("N")[..22].ToUpperInvariant();
        var stage = "open";
        try
        {
            await using var connection = new OceanBaseConnection(connectionString);
            if (async) await connection.OpenAsync();
            else connection.Open();
            await using var catalog = connection.CreateCommand();
            catalog.CommandText = mode == OceanBaseCompatibilityMode.MySql
                ? "SELECT DATABASE()" : "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM DUAL";
            var schema = Assert.IsType<string>(async ? await catalog.ExecuteScalarAsync() : catalog.ExecuteScalar());
            var restrictions = mode == OceanBaseCompatibilityMode.MySql
                ? new string?[] { schema, null, name } : [null, schema, name];
            var options = new DbContextOptionsBuilder<SequenceContext>()
                .UseOceanBase(connection, mode)
                .EnableOceanBaseSchemaManagement(schema)
                .ReplaceService<IModelCacheKeyFactory, SequenceCacheKey>().Options;
            await using var context = new SequenceContext(options, name);
            var creator = context.GetService<IRelationalDatabaseCreator>();
            var generator = context.GetService<IMigrationsSqlGenerator>();
            var updateSql = context.GetService<IUpdateSqlGenerator>();
            var activeName = name;
            var created = false;
            try
            {
                stage = "create sequence from model";
                if (async) await creator.CreateTablesAsync();
                else creator.CreateTables();
                created = true;

                stage = "read exact sequence metadata and verify initial values";
                using (var rows = async ? await connection.GetSchemaAsync("Sequences", restrictions)
                    : connection.GetSchema("Sequences", restrictions))
                {
                    Assert.Single(rows.Rows);
                    Assert.Equal("19", Assert.IsType<OceanBaseDecimal>(rows.Rows[0]["MAXIMUM_VALUE"]).ToString());
                }
                Assert.Equal(7, await NextValue());
                Assert.Equal(10, await NextValue(schema));

                stage = "apply migration and inverse migration";
                await ChangeIncrement(2);
                Assert.Equal(12, await NextValue());
                await ChangeIncrement(3);
                Assert.Equal(15, await NextValue());

                stage = "restart the sequence";
                await Execute(generator.Generate([new RestartSequenceOperation { Name = name, StartValue = 4 }]).Single().CommandText);
                Assert.Equal(4, await NextValue());

                stage = "move between disjoint ranges and roll back";
                await MoveRange(20, 40, 25);
                Assert.Equal(25, await NextValue());
                await MoveRange(1, 19, 7);
                Assert.Equal(7, await NextValue());

                if (mode == OceanBaseCompatibilityMode.Oracle)
                {
                    stage = "rename the sequence in the current schema";
                    var renamed = name[..^1] + (name[^1] == 'R' ? 'S' : 'R');
                    await Execute(generator.Generate([new RenameSequenceOperation { Name = name, NewName = renamed }])
                        .Single().CommandText);
                    activeName = renamed;
                    Assert.Equal(10, await NextValue());
                    using var original = async ? await connection.GetSchemaAsync("Sequences", restrictions)
                        : connection.GetSchema("Sequences", restrictions);
                    Assert.Empty(original.Rows);
                }
            }
            catch (OceanBaseException error)
            {
                throw SafeFailure(mode, name, stage, error);
            }
            finally
            {
                if (created)
                {
                    var drop = generator.Generate([new DropSequenceOperation { Name = activeName }]).Single();
                    try { await Execute(drop.CommandText); }
                    catch (OceanBaseException error) { throw SafeFailure(mode, activeName, "drop owned sequence", error); }
                    stage = "verify owned sequence removal";
                    restrictions[2] = activeName;
                    using var rows = async ? await connection.GetSchemaAsync("Sequences", restrictions)
                        : connection.GetSchema("Sequences", restrictions);
                    Assert.Empty(rows.Rows);
                }
            }

            Task ChangeIncrement(int increment) => Execute(generator.Generate([new AlterSequenceOperation
            {
                Name = name, IncrementBy = increment, MinValue = 1, MaxValue = 19
            }]).Single().CommandText);

            Task MoveRange(long min, long max, long start) => Execute(generator.Generate([
                new AlterSequenceOperation { Name = name, MinValue = min, MaxValue = max, IncrementBy = 3 },
                new RestartSequenceOperation { Name = name, StartValue = start }
            ]).Single().CommandText);

            Task<int> Execute(string sql) => async ? context.Database.ExecuteSqlRawAsync(sql)
                : Task.FromResult(context.Database.ExecuteSqlRaw(sql));

            async Task<long> NextValue(string? schemaName = null)
            {
                catalog.CommandText = updateSql.GenerateNextSequenceValueOperation(activeName, schemaName);
                var value = async ? await catalog.ExecuteScalarAsync() : catalog.ExecuteScalar();
                return value is OceanBaseDecimal number ? decimal.ToInt64(number.ToDecimal()) : Assert.IsType<long>(value);
            }
        }
        catch (OceanBaseException error)
        {
            throw SafeFailure(mode, name, stage, error);
        }
    }

    private static InvalidOperationException SafeFailure(OceanBaseCompatibilityMode mode, string name, string stage, OceanBaseException error)
        => new($"{mode} sequence {name} failed at {stage} with server code {error.ServerErrorCode}.");

    private sealed class SequenceContext(DbContextOptions<SequenceContext> options, string name) : DbContext(options)
    {
        public string SequenceName => name;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.HasSequence<long>(SequenceName).StartsAt(7).IncrementsBy(3).HasMin(1).HasMax(19);
    }

    private sealed class SequenceCacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (((SequenceContext)context).SequenceName, designTime);
    }
}
