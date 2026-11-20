using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using OceanBase.EntityFrameworkCore.Internal;
using OceanBase.EntityFrameworkCore.Metadata;
using OceanBase.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.ValueGeneration
{
    internal sealed class OceanBaseValueGeneratorSelector(ValueGeneratorSelectorDependencies dependencies,
        IRelationalConnection connection, IRawSqlCommandBuilder commands, IUpdateSqlGenerator updateSql,
        IRelationalCommandDiagnosticsLogger logger, IDbContextOptions options) : RelationalValueGeneratorSelector(dependencies)
    {
        private readonly Dictionary<IProperty, ValueGenerator> _generators = new();

        public override ValueGenerator Select(IProperty property, ITypeBase typeBase)
        {
            if (_generators.TryGetValue(property, out var valueGenerator)) return valueGenerator;
            if (OceanBaseRelationalAnnotationProvider.FindHiLoSequence(property) is not { } sequence)
                return base.Select(property, typeBase);

            var mysql = options.FindExtension<OceanBaseOptionsExtension>()!.CompatibilityMode == OceanBaseCompatibilityMode.MySql;
            var scope = commands.Build(mysql ? "SELECT DATABASE()" : "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM DUAL");
            var int32 = property.GetTypeMapping() is OceanBaseIntegerTypeMapping<int>;
            var storeMaximum = property.GetTypeMapping() switch
            {
                OceanBaseIntegerTypeMapping<int> mapping => Math.Min(int.MaxValue, mapping.Maximum),
                OceanBaseIntegerTypeMapping<long> mapping => Math.Min(long.MaxValue, mapping.Maximum),
                _ => throw new NotSupportedException("OceanBase HiLo requires an OceanBase integer store mapping.")
            };
            var maximumKey = (long)Math.Min(sequence.MaxValue ?? long.MaxValue, storeMaximum);
            valueGenerator = new SequenceKeyGenerator(connection, commands, updateSql, logger, sequence, scope, mysql, int32, maximumKey);
            _generators.Add(property, valueGenerator);
            return valueGenerator;
        }

        private sealed class SequenceKeyGenerator(IRelationalConnection connection, IRawSqlCommandBuilder commands,
            IUpdateSqlGenerator updateSql, IRelationalCommandDiagnosticsLogger logger, ISequence sequence,
            IRelationalCommand scope, bool mysql, bool int32, long maximumKey) : ValueGenerator
        {
            private long _next;
            private int _remaining;
            private (DbConnection Connection, string? ConnectionString, string Database) _blockTarget;

            public override bool GeneratesTemporaryValues => false;

            protected override object NextValue(EntityEntry entry)
            {
                if (!HasBlock())
                {
                    connection.Open();
                    try
                    {
                        var parameters = Parameters(entry);
                        var owner = sequence.Schema ?? ReadOwner(scope.ExecuteScalar(parameters));
                        using var metadata = connection.DbConnection.GetSchema("Sequences", Restrictions(owner));
                        var blockMaximum = GetBlockMaximum(metadata);
                        Allocate(Command(owner).ExecuteScalar(parameters), blockMaximum);
                    }
                    finally
                    {
                        connection.Close();
                    }
                }
                return TakeKey();
            }

            protected override async ValueTask<object?> NextValueAsync(EntityEntry entry, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!HasBlock())
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        var parameters = Parameters(entry);
                        var owner = sequence.Schema ?? ReadOwner(await scope.ExecuteScalarAsync(parameters, cancellationToken).ConfigureAwait(false));
                        using var metadata = await connection.DbConnection.GetSchemaAsync("Sequences", Restrictions(owner), cancellationToken).ConfigureAwait(false);
                        var blockMaximum = GetBlockMaximum(metadata);
                        Allocate(await Command(owner).ExecuteScalarAsync(parameters, cancellationToken).ConfigureAwait(false), blockMaximum);
                    }
                    finally
                    {
                        await connection.CloseAsync().ConfigureAwait(false);
                    }
                }
                return TakeKey();
            }

            private static string ReadOwner(object? value)
                => value as string ?? throw new InvalidOperationException("OceanBase HiLo requires a selected database or schema.");

            private string?[] Restrictions(string owner)
                => mysql ? [owner, null, sequence.Name] : [null, owner, sequence.Name];

            private IRelationalCommand Command(string owner)
                => commands.Build(updateSql.GenerateNextSequenceValueOperation(sequence.Name, owner));

            private long GetBlockMaximum(DataTable metadata)
            {
                if (metadata.Rows.Count != 1
                    || (long)OceanBaseNumericConversion.ConvertValue(metadata.Rows[0]["INCREMENT"], typeof(long)) != sequence.IncrementBy
                    || !Equals(metadata.Rows[0]["CYCLE_FLAG"], "N"))
                {
                    throw new InvalidOperationException("OceanBase HiLo requires a database sequence with the configured increment and cycling disabled.");
                }

                var maximum = (decimal)OceanBaseNumericConversion.ConvertValue(metadata.Rows[0]["MAXIMUM_VALUE"], typeof(decimal));
                return (long)Math.Min(maximumKey, maximum);
            }

            private RelationalCommandParameterObject Parameters(EntityEntry entry)
                => new(connection, null, null, entry.Context, logger, CommandSource.ValueGenerator);

            private (DbConnection Connection, string? ConnectionString, string Database) CurrentTarget()
                => (connection.DbConnection, connection.ConnectionString, connection.DbConnection.Database);

            private bool HasBlock() => _remaining > 0 && _blockTarget == CurrentTarget();

            private void Allocate(object? value, long blockMaximum)
            {
                if (value is null or DBNull)
                    throw new InvalidOperationException("The OceanBase sequence did not return a key.");

                var key = (long)OceanBaseNumericConversion.ConvertValue(value, typeof(long));
                if (key < 1 || key > blockMaximum)
                    throw new OverflowException("The OceanBase sequence returned a key outside the configured property range.");

                _blockTarget = CurrentTarget();
                _next = key;
                _remaining = (int)Math.Min(sequence.IncrementBy, blockMaximum - key + 1);
            }

            private object TakeKey()
            {
                var key = _next;
                if (--_remaining > 0) _next++;
                return int32 ? (object)(int)key : key;
            }
        }
    }
}
