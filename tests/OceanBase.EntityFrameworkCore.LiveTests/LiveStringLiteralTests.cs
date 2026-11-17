using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using OceanBaseConnector;
using Xunit;
using Xunit.Sdk;

namespace OceanBase.EntityFrameworkCore.LiveTests;

public sealed class LiveStringLiteralTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, "utf8mb4", true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "utf8mb4", true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "latin1", true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "utf8mb4", false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "latin1", false)]
    public async Task Generated_text_literals_round_trip_without_test_objects(
        OceanBaseCompatibilityMode mode, string characterSet, bool unicode)
    {
        if (Environment.GetEnvironmentVariable("OCEANBASE_RUN_LIVE_TESTS") != "1")
            throw new InvalidOperationException("Set OCEANBASE_RUN_LIVE_TESTS=1 to run live OceanBase tests.");
        var key = mode == OceanBaseCompatibilityMode.MySql ? "ConnectionStrings:OceanBaseMySql" : "ConnectionStrings:OceanBaseOracle";
        var configured = new ConfigurationBuilder().AddUserSecrets<LiveConnectionTests>(optional: false).Build()[key];
        if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException($"Missing User Secret '{key}'.");
        configured = LiveTestConfiguration.ConnectionString(configured);
        var connection = new OceanBaseConnectionStringBuilder(configured) { CharacterSet = characterSet };
        try
        {
            await using var context = new DbContext(new DbContextOptionsBuilder()
                .UseOceanBase(connection.ConnectionString, mode).Options);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(string), null, unicode: unicode)!;
            var texts = new string?[] { null, "", "Ready's", @"\6d77", "\n", "a\rb", "a\r\nb", "\r\n\t\0", "' OR 1=1--\n\0" };
            if (unicode || characterSet == "utf8mb4") texts = texts.Concat(["海", "海🌊", "海🌊\n"]).ToArray();
            if (!unicode) texts = texts.Concat([new string('a', 3998) + "\nb", new string('\n', 4000),
                string.Concat(Enumerable.Repeat("\r\n", 2000))]).ToArray();
            foreach (var text in texts)
            {
                var literal = mapping.GenerateSqlLiteral(text);
                var sql = mode == OceanBaseCompatibilityMode.Oracle
                    ? $"SELECT {literal} AS \"Value\" FROM DUAL"
                    : $"SELECT {literal} AS `Value`";
                var query = context.Database.SqlQueryRaw<string?>(sql);
                var expected = mode == OceanBaseCompatibilityMode.Oracle && text == "" ? null : text;
                Assert.Equal(expected, Assert.Single(query.ToList()));
                Assert.Equal(expected, Assert.Single(await query.ToListAsync()));
                if (!string.IsNullOrEmpty(text))
                {
                    var filtered = query.Where(value => value == EF.Constant(text));
                    Assert.Equal(text, Assert.Single(filtered.ToList()));
                    Assert.Equal(text, Assert.Single(await filtered.ToListAsync()));
                }
            }
            if (!unicode)
            {
                foreach (var (newline, source) in new[] { ("\n", "CHR(10)"), ("\r", "CHR(13)"), ("\r\n", "CHR(13) || CHR(10)") })
                {
                    var sql = "SELECT CASE WHEN CAST('a' || " + source + " AS CHAR(20 CHAR)) = "
                        + mapping.GenerateSqlLiteral("a" + newline) + " THEN 'yes' ELSE 'no' END AS \"Value\" FROM DUAL";
                    Assert.Equal("yes", Assert.Single(context.Database.SqlQueryRaw<string>(sql).ToList()));
                    Assert.Equal("yes", Assert.Single(await context.Database.SqlQueryRaw<string>(sql).ToListAsync()));
                }
            }
            if (!unicode && characterSet == "latin1")
            {
                var sql = "SELECT " + mapping.GenerateSqlLiteral("海\n") + " AS \"Value\" FROM DUAL";
                Assert.Throws<EncoderFallbackException>(() => context.Database.SqlQueryRaw<string>(sql).ToList());
                await Assert.ThrowsAsync<EncoderFallbackException>(() => context.Database.SqlQueryRaw<string>(sql).ToListAsync());
            }
        }
        catch (Exception error) when (error is not XunitException)
        {
            var code = error is OceanBaseException native ? native.ServerErrorCode.ToString() : "none";
            throw new InvalidOperationException($"{mode}/{characterSet}: literal probe failed; error={error.GetType().Name}; server-code={code}. No test objects were created.");
        }
    }
}
