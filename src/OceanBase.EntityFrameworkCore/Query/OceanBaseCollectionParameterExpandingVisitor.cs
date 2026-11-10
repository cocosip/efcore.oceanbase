using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseCollectionParameterExpandingVisitor(
        ISqlExpressionFactory factory, RelationalParameterBasedSqlProcessorParameters processorParameters,
        ParametersCacheDecorator parameters) : ExpressionVisitor
    {
        private readonly Dictionary<(string Name, string Invariant), List<object?>> _snapshots = [];
        private readonly Dictionary<RelationalTypeMapping,
            Dictionary<(string Name, string Invariant, ParameterTranslationMode Mode, int Index), SqlExpression>> _expanded
            = new(ReferenceEqualityComparer.Instance);

        protected override Expression VisitExtension(Expression node)
        {
            if (node is not InExpression { ValuesParameter: { } source } membership
                || source.TypeMapping?.ElementTypeMapping is not RelationalTypeMapping mapping
                || !OceanBaseOracleStringVisitor.IsProviderString(mapping))
                return base.VisitExtension(node);

            var dictionary = parameters.GetAndDisableCaching();
            var identity = (source.Name, source.InvariantName);
            if (!_snapshots.TryGetValue(identity, out var values))
            {
                if (dictionary[source.Name] is { } value && value is not IEnumerable)
                    throw new InvalidOperationException("Text collection membership requires an enumerable parameter value.");
                values = ((IEnumerable?)dictionary[source.Name])?.Cast<object?>().ToList() ?? [];
                _snapshots.Add(identity, values);
            }

            var mode = source.TranslationMode ?? processorParameters.CollectionParameterTranslationMode;
            var bucket = mode != ParameterTranslationMode.MultipleParameters ? 1 : values.Count switch
            {
                <= 5 => 1,
                <= 150 => 10,
                <= 750 => 50,
                <= 2000 => 100,
                _ => 200
            };
            var count = values.Count + (bucket - values.Count % bucket) % bucket;
            var scalars = new List<SqlExpression>(count);
            if (!_expanded.TryGetValue(mapping, out var expanded))
                _expanded.Add(mapping, expanded = []);
            var elementType = source.Type.IsArray ? source.Type.GetElementType()
                : source.Type.GetInterfaces().Append(source.Type)
                    .FirstOrDefault(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    ?.GenericTypeArguments[0];
            var type = mapping.ClrType;
            if (Nullable.GetUnderlyingType(elementType ?? type) is not null && type.IsValueType
                && Nullable.GetUnderlyingType(type) is null)
                type = typeof(Nullable<>).MakeGenericType(type);

            for (var index = 0; index < count; index++)
            {
                var key = (source.Name, source.InvariantName, mode, index);
                if (!expanded.TryGetValue(key, out var scalar))
                {
                    // Pad raw originals so the scalar visitor can classify provider-empty values before EF handles NULLs.
                    var raw = values[Math.Min(index, values.Count - 1)];
                    if (mode == ParameterTranslationMode.Constant)
                        scalar = factory.Constant(raw, type, sensitive: true, mapping);
                    else
                    {
                        var name = Uniquifier.Uniquify(source.Name + "_text" + index, dictionary, int.MaxValue);
                        dictionary.Add(name, raw);
                        scalar = new SqlParameterExpression(name, type, mapping);
                    }
                    expanded.Add(key, scalar);
                }
                scalars.Add(scalar);
            }

            return membership.Update((SqlExpression)Visit(membership.Item), scalars);
        }
    }
}
