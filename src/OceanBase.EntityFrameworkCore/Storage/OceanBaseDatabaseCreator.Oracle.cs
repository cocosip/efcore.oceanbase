using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed partial class OceanBaseDatabaseCreator
    {
        private static bool IsOracleLogin(OceanBaseConnection native, string scope)
        {
            var user = new OceanBaseConnectionStringBuilder(native.ConnectionString).UserId;
            return string.Equals(user, scope, StringComparison.Ordinal)
                || user.StartsWith(scope + "@", StringComparison.Ordinal) && user.Length > scope.Length + 1;
        }

        private static void ValidateOracleTarget(OceanBaseConnection target, string scope)
        {
            if (!IsOracleLogin(target, scope) || target.Database.Length != 0
                && !string.Equals(target.Database, scope, StringComparison.Ordinal))
                throw new InvalidOperationException("The Oracle target login/schema does not match the declared owned user. No DDL was executed.");
        }

        private IRelationalCommand OracleUserStatement(string scope, bool delete)
        {
            var name = currentContext.Context.GetService<ISqlGenerationHelper>().DelimitIdentifier(scope);
            if (delete) return commands.Build("DROP USER " + name + " CASCADE");
            var password = options.FindExtension<Internal.OceanBaseOptionsExtension>()!.OracleUserPasswordFactory!();
            if (string.IsNullOrEmpty(password) || password.Contains('"') || password.Any(char.IsControl))
                throw new ArgumentException("The Oracle user password must be nonempty and contain no double quotes or control characters.");
            var prefix = "CREATE USER " + name + " IDENTIFIED BY ";
            return new OracleCreateUserCommand(commandDependencies, prefix + "<redacted>", prefix + "\"" + password + "\"");
        }

        private IRelationalCommand OracleUserGrant(string scope)
            => commands.Build("GRANT CREATE SESSION, CREATE TABLE TO "
                + currentContext.Context.GetService<ISqlGenerationHelper>().DelimitIdentifier(scope));

        // EF9 reads DbCommand.CommandText for its logs and interception events.
        private sealed class OracleCreateUserCommand(RelationalCommandBuilderDependencies dependencies, string redactedSql, string executableSql)
            : RelationalCommand(dependencies, redactedSql, [])
        {
            public override DbCommand CreateDbCommand(RelationalCommandParameterObject parameterObject, Guid commandId, DbCommandMethod commandMethod)
                => new CredentialCommand(base.CreateDbCommand(parameterObject, commandId, commandMethod), CommandText, executableSql);
        }

        private sealed class CredentialCommand(DbCommand command, string redactedSql, string executableSql) : DbCommand
        {
            private string _visibleSql = command.CommandText;

            [AllowNull]
            public override string CommandText
            {
                get => _visibleSql;
                set => _visibleSql = value ?? "";
            }
            public override int CommandTimeout { get => command.CommandTimeout; set => command.CommandTimeout = value; }
            public override CommandType CommandType { get => command.CommandType; set => command.CommandType = value; }
            public override bool DesignTimeVisible { get => command.DesignTimeVisible; set => command.DesignTimeVisible = value; }
            public override UpdateRowSource UpdatedRowSource { get => command.UpdatedRowSource; set => command.UpdatedRowSource = value; }
            protected override DbConnection? DbConnection { get => command.Connection; set => command.Connection = value; }
            protected override DbTransaction? DbTransaction { get => command.Transaction; set => command.Transaction = value; }
            protected override DbParameterCollection DbParameterCollection => command.Parameters;
            protected override DbParameter CreateDbParameter() => command.CreateParameter();
            public override void Cancel() => command.Cancel();
            public override void Prepare() => throw new NotSupportedException();
            public override object ExecuteScalar() => throw new NotSupportedException();
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();

            public override int ExecuteNonQuery()
            {
                command.CommandText = GetExecutableSql();
                try { return command.ExecuteNonQuery(); }
                finally { command.CommandText = _visibleSql; }
            }

            public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
            {
                command.CommandText = GetExecutableSql();
                try { return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
                finally { command.CommandText = _visibleSql; }
            }

            private string GetExecutableSql()
            {
                var index = _visibleSql.IndexOf(redactedSql, StringComparison.Ordinal);
                if (CommandType != CommandType.Text || index < 0
                    || index != _visibleSql.LastIndexOf(redactedSql, StringComparison.Ordinal)
                    || !IsLeadingSqlTrivia(_visibleSql.AsSpan(0, index)))
                    return _visibleSql;

                return string.Concat(_visibleSql.AsSpan(0, index), executableSql, _visibleSql.AsSpan(index + redactedSql.Length));
            }

            // An interceptor can add tags, but the credential belongs only to the leading CREATE USER statement.
            private static bool IsLeadingSqlTrivia(ReadOnlySpan<char> prefix)
            {
                while (!(prefix = prefix.TrimStart()).IsEmpty)
                {
                    if (prefix.StartsWith("--", StringComparison.Ordinal))
                    {
                        var end = prefix.IndexOfAny('\r', '\n');
                        if (end < 0) return false;
                        prefix = prefix[(end + 1)..];
                    }
                    else if (prefix.StartsWith("/*", StringComparison.Ordinal))
                    {
                        var end = prefix[2..].IndexOf("*/", StringComparison.Ordinal);
                        if (end < 0) return false;
                        prefix = prefix[(end + 4)..];
                    }
                    else return false;
                }

                return true;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    command.Dispose();
                base.Dispose(disposing);
            }

            public override ValueTask DisposeAsync() => command.DisposeAsync();
        }
    }
}
