using System;
using System.Linq;
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
    [InlineData(OceanBaseCompatibilityMode.MySql, "utf8mb4")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "utf8mb4")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "latin1")]
    public async Task Generated_text_literals_round_trip_without_test_objects(
        OceanBaseCompatibilityMode mode, string characterSet)
    {
        if (Environment.GetEnvironmentVariable("OCEANBASE_RUN_LIVE_TESTS") != "1")
            throw new InvalidOperationException("Set OCEANBASE_RUN_LIVE_TESTS=1 to run live OceanBase tests.");
        var key = mode == OceanBaseCompatibilityMode.MySql ? "ConnectionStrings:OceanBaseMySql" : "ConnectionStrings:OceanBaseOracle";
        var configured = new ConfigurationBuilder().AddUserSecrets<LiveConnectionTests>(optional: false).Build()[key];
        if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException($"Missing User Secret '{key}'.");
        var connection = new OceanBaseConnectionStringBuilder(configured) { CharacterSet = characterSet };
        try
        {
            await using var context = new DbContext(new DbContextOptionsBuilder()
                .UseOceanBase(connection.ConnectionString, mode).Options);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(string))!;
            foreach (var text in new string?[] { null, "", "Ready's", "海", "海🌊", @"\6d77", "' OR 1=1--\n\0" })
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
        }
        catch (Exception error) when (error is not XunitException)
        {
            var code = error is OceanBaseException native ? native.ServerErrorCode.ToString() : "none";
            throw new InvalidOperationException($"{mode}/{characterSet}: literal probe failed; error={error.GetType().Name}; server-code={code}. No test objects were created.");
        }
    }
}
