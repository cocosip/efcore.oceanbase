using System;
using System.Linq;
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
            // EF10 keeps executable SQL separate from logs; never log a credential literal, even in sensitive mode.
            return new RelationalCommand(commandDependencies, prefix + "\"" + password + "\"", prefix + "<redacted>", []);
        }

        private IRelationalCommand OracleUserGrant(string scope)
            => commands.Build("GRANT CREATE SESSION, CREATE TABLE TO "
                + currentContext.Context.GetService<ISqlGenerationHelper>().DelimitIdentifier(scope));
    }
}
