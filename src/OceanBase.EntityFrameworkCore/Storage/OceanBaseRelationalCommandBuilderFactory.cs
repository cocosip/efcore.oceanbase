using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
// EF10 parameter definitions let repeated binds retain mapping/converter/facet behavior; review on major upgrades.
#pragma warning disable EF1001
using Microsoft.EntityFrameworkCore.Storage.Internal;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed class OceanBaseRelationalCommandBuilderFactory(
        RelationalCommandBuilderDependencies dependencies, OceanBaseCompatibilityMode mode) : IRelationalCommandBuilderFactory
    {
        public IRelationalCommandBuilder Create() => new Builder(dependencies, mode);

        private sealed class Builder(RelationalCommandBuilderDependencies dependencies, OceanBaseCompatibilityMode mode)
            : RelationalCommandBuilder(dependencies)
        {
            public override IRelationalCommand Build()
            {
                var template = base.Build();
                return mode == OceanBaseCompatibilityMode.MySql ? new PositionalCommand(Dependencies, template) : template;
            }
        }

        private sealed class PositionalCommand(RelationalCommandBuilderDependencies dependencies, IRelationalCommand template)
            : RelationalCommand(dependencies, template.CommandText, template.LogCommandText, WithBinding(template.Parameters))
        {
            public override DbCommand CreateDbCommand(RelationalCommandParameterObject parameterObject, Guid commandId, DbCommandMethod commandMethod)
            {
                try { return base.CreateDbCommand(parameterObject, commandId, commandMethod); }
                catch
                {
                    // EF opens readers before command creation, outside its command cleanup scope.
                    if (commandMethod == DbCommandMethod.ExecuteReader) parameterObject.Connection.Close();
                    throw;
                }
            }

            private static IReadOnlyList<IRelationalParameter> WithBinding(IReadOnlyList<IRelationalParameter> parameters)
            {
                var result = new List<IRelationalParameter>(parameters) { new BindingParameter(parameters) };
                return result;
            }
        }

        private sealed class BindingParameter(IReadOnlyList<IRelationalParameter> parameters) : IRelationalParameter
        {
            public string InvariantName => "__oceanbase_positional_binding";
            public void AddDbParameter(DbCommand command, object? value) => throw new NotSupportedException();

            public void AddDbParameter(DbCommand command, IReadOnlyDictionary<string, object?>? values)
            {
                if (command.Parameters.Count == 0) return;
                try
                {
                    var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    var streams = new HashSet<Stream>(ReferenceEqualityComparer.Instance);
                    for (var i = 0; i < command.Parameters.Count; i++)
                    {
                        var parameter = command.Parameters[i];
                        if (parameter.Direction != ParameterDirection.Input)
                            throw new NotSupportedException("MySQL provider commands currently require input parameters.");
                        if (parameter.Value is Stream stream && !streams.Add(stream))
                            throw new NotSupportedException("A stream cannot be consumed by repeated MySQL parameter positions.");
                        var name = parameter.ParameterName.StartsWith('@') ? parameter.ParameterName : "@" + parameter.ParameterName;
                        if (!indices.TryAdd(name, i))
                            throw new NotSupportedException("MySQL parameter names must be unique after normalization.");
                    }
                    var plan = OceanBasePositionalSql.Rewrite(command.CommandText, indices);
                    if (plan.Positions > 0) return;
                    var ordered = new List<DbParameter>();
                    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var name in plan.Names)
                    {
                        var original = command.Parameters[indices[name]];
                        if (used.Add(name)) ordered.Add(original);
                        else
                        {
                            if (original.Value is Stream)
                                throw new NotSupportedException("A stream cannot be consumed by repeated MySQL parameter positions.");
                            using var scratch = new OceanBaseConnector.OceanBaseCommand();
                            if (!AddMappedParameter(name, scratch, values))
                                throw new NotSupportedException("Repeated caller-owned MySQL parameters require a public connector clone contract; use distinct parameter instances.");
                            var repeated = scratch.Parameters[0];
                            scratch.Parameters.Clear();
                            ordered.Add(repeated);
                        }
                    }
                    command.Parameters.Clear();
                    foreach (var parameter in ordered) command.Parameters.Add(parameter);
                    command.CommandText = plan.Sql;
                    return;
                }
                catch
                {
                    command.Parameters.Clear();
                    command.Dispose();
                    throw;
                }
            }

            private bool AddMappedParameter(string name, DbCommand target, IReadOnlyDictionary<string, object?>? values)
            {
                foreach (var parameter in parameters)
                {
                    if (Add(parameter, values is not null && values.TryGetValue(parameter.InvariantName, out var supplied) ? supplied : null)) return true;
                }
                return false;

                bool Add(IRelationalParameter parameter, object? value)
                {
                    if (parameter is TypeMappedRelationalParameter mapped
                        && string.Equals(mapped.Name.StartsWith('@') ? mapped.Name : "@" + mapped.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        mapped.AddDbParameter(target, value);
                        return true;
                    }
                    if (parameter is CompositeRelationalParameter composite && value is object[] elements)
                        for (var i = 0; i < composite.RelationalParameters.Count; i++)
                            if (Add(composite.RelationalParameters[i], elements[i])) return true;
                    return false;
                }
            }
        }
    }
}
#pragma warning restore EF1001
