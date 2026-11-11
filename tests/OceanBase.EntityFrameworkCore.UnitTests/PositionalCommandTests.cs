using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.IO;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class PositionalCommandTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Bound_values_follow_SQL_occurrences_and_preserve_decimal_metadata(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var prefix = mode == OceanBaseCompatibilityMode.MySql ? "@" : ":";
            var builder = context.GetService<IRelationalCommandBuilderFactory>().Create();
            builder.Append($"SELECT {prefix}b, {prefix}a, {prefix}b");
            var mappings = context.GetService<IRelationalTypeMappingSource>();
            builder.AddParameter("a", prefix + "a", mappings.FindMapping(typeof(string))!, true);
            builder.AddParameter("b", prefix + "b", mappings.FindMapping(typeof(decimal))!, false);
            using var command = builder.Build().CreateDbCommand(new RelationalCommandParameterObject(
                context.GetService<IRelationalConnection>(), new Dictionary<string, object?> { ["a"] = "' DROP ? @b", ["b"] = 1.25m },
                null, context, null), Guid.NewGuid(), DbCommandMethod.ExecuteReader);
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Equal("SELECT ?, ?, ?", command.CommandText);
                Assert.Equal(new object[] { 1.25m, "' DROP ? @b", 1.25m }, command.Parameters.Cast<OceanBaseParameter>().Select(p => p.Value));
                Assert.NotSame(command.Parameters[0], command.Parameters[2]);
                Assert.Equal(command.Parameters[0].Scale, command.Parameters[2].Scale);
                Assert.Equal(command.Parameters[0].Precision, command.Parameters[2].Precision);
            }
            else
            {
                Assert.Equal("SELECT :b, :a, :b", command.CommandText);
                Assert.Equal(2, command.Parameters.Count);
            }
        }

        [Theory]
        [InlineData("SELECT '@a', \"@a\", `@a`, @a -- @a\n# @a\n/* @a */", "SELECT '@a', \"@a\", `@a`, ? -- @a\n# @a\n/* @a */")]
        [InlineData("SELECT 'it''s @a', `x``@a`, @@version, @user, @a", "SELECT 'it''s @a', `x``@a`, @@version, @user, ?")]
        [InlineData("SELECT @ab, @a", "SELECT @ab, ?")]
        [InlineData("SELECT @a.b, @a", "SELECT @a.b, ?")]
        [InlineData("SELECT 1 --\0 @a\n, @a", "SELECT 1 --\0 @a\n, ?")]
        [InlineData("SELECT @a--1", "SELECT ?--1")]
        [InlineData("SELECT ?", "SELECT ?")]
        public void Only_SQL_parameter_tokens_are_rewritten(string sql, string expected)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
            var builder = context.GetService<IRelationalCommandBuilderFactory>().Create();
            builder.Append(sql).AddParameter("a", "@a", context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(int))!, false);
            using var command = builder.Build().CreateDbCommand(new RelationalCommandParameterObject(
                context.GetService<IRelationalConnection>(), new Dictionary<string, object?> { ["a"] = 7 }, null, context, null),
                Guid.NewGuid(), DbCommandMethod.ExecuteScalar);
            Assert.Equal(expected, command.CommandText);
            Assert.Equal(7, command.Parameters[0].Value);
        }

        [Fact]
        public void Connector_020_preparation_consumes_all_repeated_mapped_positions()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
            var builder = context.GetService<IRelationalCommandBuilderFactory>().Create();
            builder.Append("SELECT @b + @a + @b");
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(int))!;
            builder.AddParameter("a", "@a", mapping, false);
            builder.AddParameter("b", "@b", mapping, false);
            using var command = builder.Build().CreateDbCommand(new RelationalCommandParameterObject(
                context.GetService<IRelationalConnection>(), new Dictionary<string, object?> { ["a"] = 7, ["b"] = 11 }, null, context, null),
                Guid.NewGuid(), DbCommandMethod.ExecuteScalar);
            // Exercise the pinned connector's real offline fallback; production uses only public APIs.
            var assembly = typeof(OceanBaseParameter).Assembly;
            var bindings = assembly.GetType("OceanBaseConnector.Internal.Sql.ParameterBindingPlan")!;
            var create = bindings.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Single(method => method.Name == "Create" && method.GetParameters()[2].ParameterType == typeof(OceanBaseParameter[]));
            var plan = create.Invoke(null, [command.CommandText, OceanBaseConnector.OceanBaseCompatibilityMode.MySql,
                command.Parameters.Cast<OceanBaseParameter>().ToArray(), false]);
            var build = assembly.GetType("OceanBaseConnector.Internal.Sql.ClientPreparedSql")!
                .GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic)!;
            var literalSql = build.Invoke(null, [plan, OceanBaseConnector.OceanBaseCompatibilityMode.MySql, null, null, false, null]);
            Assert.Equal("SELECT (11) + (7) + (11)", literalSql);
        }

        [Fact]
        public void Initialization_interceptor_sees_all_final_positions_once()
        {
            var interceptor = new EditInitializedParameters();
            using var context = new DbContext(new DbContextOptionsBuilder().UseOceanBase("Server=localhost;Database=test", OceanBaseCompatibilityMode.MySql)
                .AddInterceptors(interceptor).Options);
            var builder = context.GetService<IRelationalCommandBuilderFactory>().Create();
            builder.Append("SELECT @a, @a").AddParameter("a", "@a", context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(int))!, false);
            using var command = builder.Build().CreateDbCommand(new RelationalCommandParameterObject(context.GetService<IRelationalConnection>(),
                new Dictionary<string, object?> { ["a"] = 7 }, null, context, context.GetService<IRelationalCommandDiagnosticsLogger>()),
                Guid.NewGuid(), DbCommandMethod.ExecuteScalar);
            Assert.Equal(1, interceptor.Calls);
            Assert.Equal("SELECT ?, ?", command.CommandText);
            Assert.Equal(new object[] { 99, 99 }, command.Parameters.Cast<DbParameter>().Select(p => p.Value));
        }

        private sealed class EditInitializedParameters : DbCommandInterceptor
        {
            public int Calls { get; private set; }
            public override DbCommand CommandInitialized(CommandEndEventData eventData, DbCommand command)
            {
                Calls++;
                foreach (DbParameter parameter in command.Parameters) parameter.Value = 99;
                return command;
            }
        }

        [Fact]
        public void Rejected_reader_binding_balances_EF_open_count()
        {
            using var context = new DbContext(new DbContextOptionsBuilder().UseOceanBase("Server=localhost;Database=test", OceanBaseCompatibilityMode.MySql)
                .AddInterceptors(new SuppressOpen()).Options);
            var connection = context.GetService<IRelationalConnection>();
            connection.Open();
            var builder = context.GetService<IRelationalCommandBuilderFactory>().Create();
            builder.Append("SELECT @a, ?").AddParameter("a", "@a", context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(int))!, false);
            Assert.Throws<NotSupportedException>(() => builder.Build().CreateDbCommand(new RelationalCommandParameterObject(connection,
                new Dictionary<string, object?> { ["a"] = 7 }, null, context, null), Guid.NewGuid(), DbCommandMethod.ExecuteReader));
            Assert.Equal(0, typeof(RelationalConnection).GetField("_openedCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(connection));
        }

        private sealed class SuppressOpen : DbConnectionInterceptor
        {
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
                => InterceptionResult.Suppress();
        }

        [Fact]
        public void Sharing_one_stream_across_distinct_positions_is_rejected()
        {
            using var stream = new MemoryStream([1, 2, 3]);
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
            var builder = context.GetService<IRelationalCommandBuilderFactory>().Create();
            builder.Append("SELECT @a, @b");
            builder.AddRawParameter("a", new OceanBaseParameter { ParameterName = "@a", Value = stream });
            builder.AddRawParameter("b", new OceanBaseParameter { ParameterName = "@b", Value = stream });
            Assert.Throws<NotSupportedException>(() => builder.Build().CreateDbCommand(new RelationalCommandParameterObject(
                context.GetService<IRelationalConnection>(), null, null, context, null), Guid.NewGuid(), DbCommandMethod.ExecuteScalar));
            Assert.Equal(0, stream.Position);
        }

        [Fact]
        public void Repeated_native_parameters_reject_without_copying_ambiguous_facets()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
            var builder = context.GetService<IRelationalCommandBuilderFactory>().Create();
            builder.Append("SELECT @a, @a");
            var original = new OceanBaseParameter { ParameterName = "@a", DbType = DbType.Decimal, Scale = 0, Value = 1m };
            builder.AddRawParameter("a", original);
            var error = Assert.Throws<NotSupportedException>(() => builder.Build().CreateDbCommand(new RelationalCommandParameterObject(
                context.GetService<IRelationalConnection>(), null, null, context, null), Guid.NewGuid(), DbCommandMethod.ExecuteScalar));
            Assert.Contains("clone", error.Message);
            Assert.Equal((byte)0, original.Scale);
        }

        [Theory]
        [InlineData("SELECT '@a", "Unterminated")]
        [InlineData("SELECT /* @a", "Unterminated")]
        [InlineData("SELECT 'a\\b', @a", "backslash")]
        [InlineData("SELECT /*! @a */", "executable")]
        [InlineData("SELECT @a, ?", "mix")]
        [InlineData("SELECT @missing", "unused")]
        public void Ambiguous_binding_fails_before_execution(string sql, string reason)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
            var builder = context.GetService<IRelationalCommandBuilderFactory>().Create();
            builder.Append(sql).AddParameter("a", "@a", context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(int))!, false);
            var failure = Assert.Throws<NotSupportedException>(() => builder.Build().CreateDbCommand(new RelationalCommandParameterObject(
                context.GetService<IRelationalConnection>(), new Dictionary<string, object?> { ["a"] = 7 }, null, context, null),
                Guid.NewGuid(), DbCommandMethod.ExecuteScalar));
            Assert.Contains(reason, failure.Message, StringComparison.OrdinalIgnoreCase);
        }

        private static DbContext CreateContext(OceanBaseCompatibilityMode mode)
            => new(new DbContextOptionsBuilder().UseOceanBase("Server=localhost;Database=test", mode).Options);
    }
}
