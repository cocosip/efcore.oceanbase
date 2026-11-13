using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Storage;

internal sealed partial class OceanBaseDatabaseCreator
{
    private sealed record CreationPlan(IReadOnlyList<string> Schema, IReadOnlyList<string> Data);

    private CreationPlan BuildCreationPlan(IModel model)
    {
        var context = currentContext.Context;
        var schema = new OceanBaseSchemaSqlGenerator(context.GetService<ISqlGenerationHelper>(), _mode).Generate(model);
        if (!model.GetEntityTypes().Any(entity => entity.GetSeedData().Any())) return new(schema, []);
        var includedTables = model.GetRelationalModel().Tables.Where(table => !table.IsExcludedFromMigrations)
            .Select(table => (table.Name, table.Schema)).ToHashSet();
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel())
            .Where(operation => operation switch
            {
                InsertDataOperation insert => includedTables.Contains((insert.Table, insert.Schema)),
                UpdateDataOperation update => includedTables.Contains((update.Table, update.Schema)),
                DeleteDataOperation delete => includedTables.Contains((delete.Table, delete.Schema)),
                _ => false
            }).ToArray();
        var data = context.GetService<IMigrationsSqlGenerator>().Generate(operations, model)
            .Select(command => command.CommandText.TrimEnd().TrimEnd(';')).ToArray();
        return new(schema, data);
    }

    private void ApplyModelData(IReadOnlyList<string> statements)
    {
        if (statements.Count == 0) return;
        var transaction = currentContext.Context.Database.BeginTransaction();
        Exception? failure = null;
        try
        {
            ValidateCurrentScope(ScopeProbe().ExecuteScalar(Parameters()));
            foreach (var statement in statements) commands.Build(statement).ExecuteNonQuery(Parameters());
            transaction.Commit();
        }
        catch (Exception error)
        {
            failure = error;
            try { transaction.Rollback(); }
            catch (Exception cleanup) { error.Data["OceanBase.ModelDataRollbackError"] = cleanup; }
            throw;
        }
        finally
        {
            try { transaction.Dispose(); }
            catch (Exception cleanup) when (failure is not null)
            {
                failure.Data["OceanBase.ModelDataCleanupError"] = cleanup;
                if (ReferenceEquals(connection.CurrentTransaction, transaction))
                {
                    try { connection.UseTransaction(null); connection.Close(); }
                    catch (Exception balance) { failure.Data["OceanBase.ModelDataConnectionCleanupError"] = balance; }
                }
            }
        }
    }

    private async Task ApplyModelDataAsync(IReadOnlyList<string> statements, CancellationToken cancellationToken)
    {
        if (statements.Count == 0) return;
        cancellationToken.ThrowIfCancellationRequested();
        var transaction = await currentContext.Context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        Exception? failure = null;
        try
        {
            ValidateCurrentScope(await ScopeProbe().ExecuteScalarAsync(Parameters(), cancellationToken).ConfigureAwait(false));
            foreach (var statement in statements)
                await commands.Build(statement).ExecuteNonQueryAsync(Parameters(), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failure = error;
            // DDL has already committed; only this model-data transaction can be rolled back.
            try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception cleanup) { error.Data["OceanBase.ModelDataRollbackError"] = cleanup; }
            throw;
        }
        finally
        {
            try { await transaction.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) when (failure is not null)
            {
                failure.Data["OceanBase.ModelDataCleanupError"] = cleanup;
                if (ReferenceEquals(connection.CurrentTransaction, transaction))
                {
                    try { connection.UseTransaction(null); await connection.CloseAsync().ConfigureAwait(false); }
                    catch (Exception balance) { failure.Data["OceanBase.ModelDataConnectionCleanupError"] = balance; }
                }
            }
        }
    }
}
