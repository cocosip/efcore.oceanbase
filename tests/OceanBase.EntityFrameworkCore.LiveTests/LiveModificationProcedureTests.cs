using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class LiveModificationProcedureTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public Task SaveChanges_uses_function_returns_for_update_and_delete_concurrency(OceanBaseCompatibilityMode mode, bool async)
            => LiveStorageTable.WithTable<FunctionContext>(mode, async, false,
                (options, table) => new FunctionContext(new DbContextOptionsBuilder<FunctionContext>(options)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table),
                async (context, _, stage) =>
                {
                    var mysql = mode == OceanBaseCompatibilityMode.MySql;
                    var name = context.Table;
                    var sql = context.GetService<ISqlGenerationHelper>();
                    var table = sql.DelimitIdentifier(name);
                    var id = sql.DelimitIdentifier(nameof(Row.Id));
                    var value = sql.DelimitIdentifier(nameof(Row.Value));
                    stage("seed row and create owned functions");
                    var insert = $"INSERT INTO {table} ({id}, {value}) VALUES (17, 2)";
                    await context.Database.ExecuteSqlRawAsync(insert);
                    var created = new List<string>();
                    Exception? failure = null;
                    try
                    {
                        await Create(name + "U", mysql ? "p_value INT, p_id INT, p_original INT" : "p_value IN INTEGER, p_id IN INTEGER, p_original IN INTEGER",
                            $"UPDATE {table} SET {value}=p_value WHERE {id}=p_id AND {value}=p_original;");
                        await Create(name + "D", mysql ? "p_original INT, p_id INT" : "p_original IN INTEGER, p_id IN INTEGER",
                            $"DELETE FROM {table} WHERE {id}=p_id AND {value}=p_original;");

                        stage("update through a function and roll it back");
                        await using (var transaction = await context.Database.BeginTransactionAsync())
                        {
                            var row = await Find();
                            row.Value = 7;
                            Assert.Equal(1, await Save());
                            await transaction.RollbackAsync();
                        }

                        context.ChangeTracker.Clear();
                        var persisted = await Find();
                        Assert.Equal(2, persisted.Value);
                        stage("reject stale update and delete through return value zero");
                        context.Entry(persisted).Property(row => row.Value).OriginalValue = -1;
                        persisted.Value = 9;
                        Assert.Same(persisted, Assert.Single((await Assert.ThrowsAsync<DbUpdateConcurrencyException>(Save)).Entries).Entity);
                        context.Remove(persisted);
                        Assert.Same(persisted, Assert.Single((await Assert.ThrowsAsync<DbUpdateConcurrencyException>(Save)).Entries).Entity);

                        stage("update and delete through both functions");
                        context.ChangeTracker.Clear();
                        persisted = await Find();
                        persisted.Value = 5;
                        Assert.Equal(1, await Save());
                        context.ChangeTracker.Clear();
                        persisted = await Find();
                        Assert.Equal(5, persisted.Value);
                        context.Remove(persisted);
                        Assert.Equal(1, await Save());
                        Assert.False(await context.Set<Row>().AnyAsync());
                    }
                    catch (Exception error)
                    {
                        failure = error;
                        throw;
                    }
                    finally
                    {
                        await DropRoutines(context, sql, "FUNCTION", created, failure);
                    }

                    async Task Create(string function, string parameters, string body)
                    {
                        var declaration = mysql ? "RETURNS INT MODIFIES SQL DATA BEGIN" : "RETURN INTEGER AS BEGIN";
                        var rows = mysql ? "ROW_COUNT()" : "SQL%ROWCOUNT";
                        var create = $"CREATE FUNCTION {sql.DelimitIdentifier(function)} ({parameters}) {declaration} {body} RETURN {rows}; END";
                        await context.Database.ExecuteSqlRawAsync(create);
                        created.Add(function);
                    }

                    Task<int> Save() => async ? context.SaveChangesAsync() : Task.FromResult(context.SaveChanges());
                    async Task<Row> Find() => (await context.Set<Row>().FindAsync(17))!;
                });

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public Task SaveChanges_uses_procedure_outputs_for_writeback_and_concurrency(OceanBaseCompatibilityMode mode, bool async)
            => LiveStorageTable.WithTable<Context>(mode, async, false,
                (options, table) => new Context(new DbContextOptionsBuilder<Context>(options)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table),
                async (context, _, stage) =>
                {
                    stage("create owned insert, update and delete procedures");
                    var sql = context.GetService<ISqlGenerationHelper>();
                    var table = sql.DelimitIdentifier(context.Table);
                    var id = sql.DelimitIdentifier(nameof(Row.Id));
                    var value = sql.DelimitIdentifier(nameof(Row.Value));
                    var stamp = sql.DelimitIdentifier(nameof(Row.Stamp));
                    var affected = mode == OceanBaseCompatibilityMode.MySql
                        ? "SET p_rows = ROW_COUNT();" : "p_rows := SQL%ROWCOUNT;";
                    var generated = mode == OceanBaseCompatibilityMode.MySql
                        ? "SET p_stamp = COALESCE(p_stamp, p_value + 100);"
                        : "p_stamp := COALESCE(p_stamp, p_value + 100);";
                    var created = new List<string>();
                    Exception? failure = null;
                    try
                    {
                        await Create("I", ["p_key", "p_value"], true,
                            $"{generated} INSERT INTO {table} ({id}, {value}, {stamp}) VALUES (p_key, p_value, p_stamp);");
                        var accumulated = mode == OceanBaseCompatibilityMode.MySql
                            ? "SET p_stamp = p_stamp + p_value;" : "p_stamp := p_stamp + p_value;";
                        await Create("U", ["p_value", "p_key", "p_original"], true,
                            $"{accumulated} UPDATE {table} SET {value}=p_value, {stamp}=p_stamp WHERE {id}=p_key AND {value}=p_original; {affected} "
                            + (mode == OceanBaseCompatibilityMode.MySql
                                ? "IF p_value=13 THEN SET p_rows=2; END IF;"
                                : "IF p_value=13 THEN p_rows:=2; END IF;"));
                        await Create("D", ["p_original", "p_key"], false,
                            $"DELETE FROM {table} WHERE {id}=p_key AND {value}=p_original; {affected}");

                        stage("reject an untransactional procedure without changing the table");
                        context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
                        context.Add(new Row { Id = 17, Value = 2 });
                        var missingTransaction = await Record.ExceptionAsync(Save);
                        Assert.False(await context.Set<Row>().AnyAsync());
                        Assert.IsType<DbUpdateException>(missingTransaction);
                        context.ChangeTracker.Clear();
                        context.Database.AutoTransactionBehavior = AutoTransactionBehavior.WhenNeeded;

                        stage("insert and propagate generated output");
                        var row = new Row { Id = 17, Value = 2 };
                        context.Add(row);
                        Assert.Equal(1, await Save());
                        Assert.Equal(102, row.Stamp);
                        context.ChangeTracker.Clear();
                        row = await Find();
                        Assert.Equal(102, row.Stamp);

                        stage("update with original value and rollback");
                        context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
                        await using (var transaction = await context.Database.BeginTransactionAsync())
                        {
                            row.Value = 5;
                            Assert.Equal(1, await Save());
                            Assert.Equal(107, row.Stamp);
                            await transaction.RollbackAsync();
                        }
                        context.Database.AutoTransactionBehavior = AutoTransactionBehavior.WhenNeeded;
                        context.ChangeTracker.Clear();
                        row = await Find();
                        Assert.Equal((2, 102), (row.Value, row.Stamp));

                        stage("roll back a write when the procedure reports an invalid row count");
                        row.Value = 13;
                        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(Save);
                        context.ChangeTracker.Clear();
                        row = await Find();
                        Assert.Equal((2, 102), (row.Value, row.Stamp));

                        stage("detect update mismatch from the rows-affected OUT parameter");
                        context.Entry(row).Property(entity => entity.Value).OriginalValue = -1;
                        row.Value = 9;
                        var concurrency = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(Save);
                        Assert.Same(row, Assert.Single(concurrency.Entries).Entity);
                        Assert.Equal(102, row.Stamp);

                        stage("delete using procedure and verify result");
                        context.ChangeTracker.Clear();
                        context.Remove(await Find());
                        Assert.Equal(1, await Save());
                        Assert.False(await context.Set<Row>().AnyAsync());
                    }
                    catch (Exception error)
                    {
                        failure = error;
                        throw;
                    }
                    finally
                    {
                        await DropRoutines(context, sql, "PROCEDURE", created, failure);
                    }

                    async Task Create(string suffix, string[] inputs, bool returnsStamp, string body)
                    {
                        var name = context.Table + suffix;
                        var parameters = new List<string>();
                        foreach (var input in inputs)
                            parameters.Add(mode == OceanBaseCompatibilityMode.MySql ? $"IN {input} INT" : $"{input} IN NUMBER");
                        if (suffix != "I")
                            parameters.Add(mode == OceanBaseCompatibilityMode.MySql ? "OUT p_rows INT" : "p_rows OUT NUMBER");
                        if (returnsStamp)
                        {
                            parameters.Add(mode == OceanBaseCompatibilityMode.MySql
                                ? "INOUT p_stamp INT"
                                : "p_stamp IN OUT NUMBER");
                        }
                        var declaration = mode == OceanBaseCompatibilityMode.MySql ? ") BEGIN " : ") AS BEGIN ";
                        var create = "CREATE PROCEDURE " + sql.DelimitIdentifier(name)
                            + " (" + string.Join(", ", parameters) + declaration + body + " END";
                        await context.Database.ExecuteSqlRawAsync(create);
                        created.Add(name);
                    }

                    Task<int> Save() => async ? context.SaveChangesAsync() : Task.FromResult(context.SaveChanges());
                    async Task<Row> Find() => (await context.Set<Row>().FindAsync(17))!;
                });

        private static async Task DropRoutines(DbContext context, ISqlGenerationHelper sql, string kind, List<string> names, Exception? failure)
        {
            var cleanupFailures = new List<Exception>();
            foreach (var name in names)
            {
                var drop = $"DROP {kind} {sql.DelimitIdentifier(name)}";
                try
                {
                    await context.Database.ExecuteSqlRawAsync(drop);
                }
                catch (Exception error)
                {
                    cleanupFailures.Add(new InvalidOperationException($"Could not drop owned {kind} {name}.", error));
                }
            }

            if (cleanupFailures.Count > 0)
            {
                if (failure is not null) cleanupFailures.Insert(0, failure);
                throw new AggregateException(cleanupFailures);
            }
        }

        private sealed class FunctionContext(DbContextOptions<FunctionContext> options, string table) : DbContext(options)
        {
            internal string Table => table;

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                var row = modelBuilder.Entity<Row>().ToTable(table);
                row.Ignore(value => value.Stamp);
                row.Property(value => value.Id).ValueGeneratedNever();
                row.Property(value => value.Value).IsConcurrencyToken();
                row.UpdateUsingStoredProcedure(table + "U", procedure => procedure
                    .HasParameter(value => value.Value)
                    .HasOriginalValueParameter(value => value.Id)
                    .HasOriginalValueParameter(value => value.Value)
                    .HasRowsAffectedReturnValue());
                row.DeleteUsingStoredProcedure(table + "D", procedure => procedure
                    .HasOriginalValueParameter(value => value.Value)
                    .HasOriginalValueParameter(value => value.Id)
                    .HasRowsAffectedReturnValue());
            }
        }

        private sealed class Context(DbContextOptions<Context> options, string table) : DbContext(options)
        {
            internal string Table => table;

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                var row = modelBuilder.Entity<Row>().ToTable(table);
                row.Property(value => value.Id).ValueGeneratedNever();
                row.Property(value => value.Value).IsConcurrencyToken();
                row.Property(value => value.Stamp).HasDefaultValue(0).ValueGeneratedOnAddOrUpdate()
                    .Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Save);
                row.InsertUsingStoredProcedure(table + "I", procedure => procedure
                    .HasParameter(value => value.Id, parameter => parameter.HasName("p_arg"))
                    .HasParameter(value => value.Value, parameter => parameter.HasName("P_ARG"))
                    .HasParameter(value => value.Stamp, parameter => parameter.HasName("p_stamp").IsInputOutput()));
                row.UpdateUsingStoredProcedure(table + "U", procedure => procedure
                    .HasParameter(value => value.Value, parameter => parameter.HasName("p_value"))
                    .HasOriginalValueParameter(value => value.Id, parameter => parameter.HasName("p_key"))
                    .HasOriginalValueParameter(value => value.Value, parameter => parameter.HasName("p_original"))
                    .HasRowsAffectedParameter(parameter => parameter.HasName("p_rows"))
                    .HasOriginalValueParameter(value => value.Stamp, parameter => parameter.HasName("p_stamp").IsInputOutput()));
                row.DeleteUsingStoredProcedure(table + "D", procedure => procedure
                    .HasOriginalValueParameter(value => value.Value, parameter => parameter.HasName("p_original"))
                    .HasOriginalValueParameter(value => value.Id, parameter => parameter.HasName("p_key"))
                    .HasRowsAffectedParameter(parameter => parameter.HasName("p_rows")));
            }
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public int Value { get; set; }
            public int Stamp { get; set; }
        }

        private sealed class CacheKey : IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime)
                => (context is FunctionContext function ? function.Table : ((Context)context).Table, designTime);
        }
    }
}
