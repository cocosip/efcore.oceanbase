using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
#pragma warning disable EF9100 // Exercise EF10's experimental generated query contract; NativeAOT is a separate acceptance gate.
    public sealed class PrecompiledQueryTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Generated_query_compiles_and_preserves_scalar_reads_and_repeated_binding(OceanBaseCompatibilityMode mode, bool async, bool localTimeZone)
        {
            var capture = new Capture(localTimeZone);
            using var context = new Context(new DbContextOptionsBuilder<Context>()
                .UseOceanBase("Server=offline", mode).ReplaceService<IRelationalConnection, Connection>()
                .AddInterceptors(capture).EnableServiceProviderCaching(false).Options, localTimeZone);
            var source = $$"""
                using System;
                using System.Linq;
                using System.Threading.Tasks;
                using Microsoft.EntityFrameworkCore;
                using OceanBase.EntityFrameworkCore.UnitTests;
                public static class QueryCalls
                {
                    public static {{(async ? "Task<PrecompiledQueryTests.Row[]>" : "PrecompiledQueryTests.Row[]")}} Run(PrecompiledQueryTests.Context context, int? minimum)
                    {
                        var db = context;
                        var bound = minimum;
                        return db.Rows.Where(row => row.Id > bound || row.ParentId == bound)
                            .OrderBy(row => row.Id).Select(row => new PrecompiledQueryTests.Row
                            {
                                Id = row.Id, ParentId = row.ParentId, Reading = row.Reading,
                                Moment = row.Moment, Amount = Math.Round(row.Amount, 2)
                            }).{{(async ? "ToArrayAsync()" : "ToArray()")}};
                    }
                }
                """;
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Concat(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location)).Select(a => a.Location))
                .Distinct().Select(path => MetadataReference.CreateFromFile(path));
            var parseOptions = CSharpParseOptions.Default.WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Microsoft.EntityFrameworkCore.GeneratedInterceptors")]);
            var compilation = CSharpCompilation.Create("Precompiled_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(source, parseOptions, path: "QueryCalls.cs")], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var original = Emit(compilation);
            using var services = (ServiceProvider)new DesignTimeServicesBuilder(typeof(Context).Assembly,
                typeof(Context).Assembly, new OperationReporter(null), []).Build(context);
            using var workspace = new AdhocWorkspace();
            var errors = new List<PrecompiledQueryCodeGenerator.QueryPrecompilationError>();
            var generated = services.GetRequiredService<IPrecompiledQueryCodeGeneratorSelector>().Select("C#")
                .GeneratePrecompiledQueries(compilation, SyntaxGenerator.GetGenerator(workspace, LanguageNames.CSharp),
                    context, new Dictionary<MemberInfo, QualifiedName>(), errors, new HashSet<string>(), original, "", default).ToArray();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Select(error => error.Exception.ToString())));
            Assert.NotEmpty(generated);
            var assembly = Emit(compilation.WithAssemblyName("Generated_" + Guid.NewGuid().ToString("N"))
                .AddSyntaxTrees(generated.Select(file => CSharpSyntaxTree.ParseText(file.Code, parseOptions, path: file.Path))));
            var run = assembly.GetType("QueryCalls")!.GetMethod("Run")!;
            capture.RejectQueryCompilation = true;
            foreach (var minimum in new int?[] { 3, null, 7 })
            {
                var result = run.Invoke(null, [context, minimum])!;
                var rows = async ? await (Task<Row[]>)result : (Row[])result;
                Assert.Equal(minimum.HasValue ? new[] { 11, 12 } : [], rows.Select(row => row.Id));
                Assert.All(rows, row =>
                {
                    Assert.Equal(2, row.ParentId);
                    Assert.Equal(1.25f, row.Reading);
                    Assert.Equal(new DateTime(2024, 2, 29, 12, 34, 56), row.Moment);
                    Assert.Equal(13m, row.Amount);
                });
                Assert.Equal(minimum.HasValue ? mode == OceanBaseCompatibilityMode.MySql ? 2 : 1 : 0, capture.Values.Length);
                Assert.All(capture.Values, value => Assert.Equal(minimum, value));
                if (mode == OceanBaseCompatibilityMode.MySql)
                {
                    Assert.Equal(minimum.HasValue ? 2 : 0, capture.Sql.Count(c => c == '?'));
                    Assert.DoesNotContain("@", capture.Sql);
                }
            }
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }

        private static Assembly Emit(CSharpCompilation compilation)
        {
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.True(result.Success, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            return Assembly.Load(stream.ToArray());
        }

        public sealed class Context(DbContextOptions<Context> options, bool localTimeZone = false) : DbContext(options)
        {
            public DbSet<Row> Rows => Set<Row>();
            protected override void OnModelCreating(ModelBuilder model)
            {
                model.Entity<Row>();
                if (localTimeZone) model.Entity<Row>().Property(row => row.Moment).HasColumnType("TIMESTAMP(7) WITH LOCAL TIME ZONE");
            }
        }
        public sealed class Row
        {
            public int Id { get; set; }
            public int ParentId { get; set; }
            public float Reading { get; set; }
            public DateTime Moment { get; set; }
            public decimal Amount { get; set; }
        }

        private sealed class Capture(bool localTimeZone) : DbCommandInterceptor, IQueryExpressionInterceptor
        {
            internal bool RejectQueryCompilation { get; set; }
            internal string Sql { get; private set; } = "";
            internal object?[] Values { get; private set; } = [];
            public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
                => RejectQueryCompilation ? throw new InvalidOperationException("Generated queries must bypass runtime query compilation.") : queryExpression;

            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
                InterceptionResult<DbDataReader> result)
            {
                Sql = command.CommandText;
                Values = command.Parameters.Cast<DbParameter>().Select(p => p.Value).ToArray();
                var table = new DataTable();
                table.Columns.Add("Id", typeof(decimal));
                table.Columns.Add("ParentId", typeof(decimal));
                table.Columns.Add("Reading", typeof(double));
                table.Columns.Add("Moment", typeof(object));
                table.Columns.Add("Amount", typeof(decimal));
                if (Values.Length > 0)
                {
                    var date = new DateTime(2024, 2, 29, 12, 34, 56);
                    object moment = localTimeZone
                        ? new OceanBaseConnector.OceanBaseTimestampLocalTimeZone(new OceanBaseConnector.OceanBaseTimestamp(date), "+05:45") : date;
                    table.Rows.Add(11m, 2m, 1.25d, moment, 13m);
                    table.Rows.Add(12m, 2m, 1.25d, moment, 13m);
                }
                return InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader());
            }

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
                CommandEventData eventData, InterceptionResult<DbDataReader> result, System.Threading.CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ReaderExecuting(command, eventData, result));
        }

        private sealed class Connection(RelationalConnectionDependencies dependencies) : RelationalConnection(dependencies)
        {
            protected override DbConnection CreateDbConnection() => new OfflineConnection();
        }
        private sealed class OfflineConnection : DbConnection
        {
            private ConnectionState _state;
            [System.Diagnostics.CodeAnalysis.AllowNull] public override string ConnectionString { get; set; } = "";
            public override string Database => "offline";
            public override string DataSource => "offline";
            public override string ServerVersion => "0";
            public override ConnectionState State => _state;
            public override void Open() => _state = ConnectionState.Open;
            public override void Close() => _state = ConnectionState.Closed;
            public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
            protected override DbCommand CreateDbCommand() => new OceanBaseConnector.OceanBaseCommand();
        }
    }
#pragma warning restore EF9100
}
