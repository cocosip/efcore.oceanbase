using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class DatabaseManagementTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task Explicit_management_factory_preserves_native_hooks_and_owns_only_the_management_handle(bool async, bool supplied)
        {
            var capture = new Capture { NamespaceExists = true };
            using var target = new OceanBaseConnection("Server=offline;Database=owned;User Id=application");
            var hooks = new NativeHooks();
            OceanBaseConnection? management = null;
            var calls = 0;
            await using var context = Create(capture, configure: builder =>
            {
                if (supplied) builder.UseOceanBase(target, OceanBase.EntityFrameworkCore.OceanBaseCompatibilityMode.MySql);
                ConfigureManagement(builder, () =>
                {
                    calls++;
                    return management = new OceanBaseConnection("Server=offline;User Id=administrator;Password=synthetic")
                    {
                        AddressResolver = hooks,
                        CredentialProvider = hooks,
                        RemoteCertificateValidationCallback = (_, _, _, _) => false
                    };
                });
            });
            var actualTarget = (OceanBaseConnection)context.Database.GetDbConnection();
            actualTarget.AddressResolver = hooks;
            context.Database.SetCommandTimeout(23);
            Assert.True(async ? await context.Database.EnsureDeletedAsync() : context.Database.EnsureDeleted());
            Assert.Equal(1, calls);
            Assert.NotNull(management);
            Assert.All(capture.Commands, command =>
            {
                Assert.Same(management, command.Connection);
                Assert.Equal(23, command.Timeout);
                Assert.Equal(async, command.Async);
            });
            Assert.Equal("administrator", new OceanBaseConnectionStringBuilder(management.ConnectionString).UserId);
            Assert.Same(hooks, management.AddressResolver);
            Assert.Same(hooks, management.CredentialProvider);
            Assert.NotNull(management.RemoteCertificateValidationCallback);
            Assert.Same(management, Assert.Single(capture.Opens.Distinct()));
            Assert.Same(management, Assert.Single(capture.Disposals));
            Assert.Equal("owned", actualTarget.Database);
            Assert.Same(hooks, actualTarget.AddressResolver);
        }

        [Theory]
        [InlineData(false, "null")]
        [InlineData(true, "null")]
        [InlineData(false, "target")]
        [InlineData(true, "target")]
        [InlineData(false, "mode")]
        [InlineData(true, "mode")]
        [InlineData(false, "database")]
        [InlineData(true, "database")]
        public async Task Invalid_factory_results_reject_before_open_and_only_independent_results_are_disposed(bool async, string profile)
        {
            var capture = new Capture();
            OceanBaseConnection? target = null;
            OceanBaseConnection? candidate = null;
            await using var context = Create(capture, configure: builder => builder.UseOceanBaseManagementConnection(() =>
                candidate = profile switch
                {
                    "null" => null!,
                    "target" => target!,
                    "mode" => new OceanBaseConnection("Server=offline;ExpectedMode=Oracle"),
                    _ => new OceanBaseConnection("Server=offline;Database=owned")
                }));
            target = (OceanBaseConnection)context.Database.GetDbConnection();
            if (async) await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.EnsureDeletedAsync());
            else Assert.Throws<InvalidOperationException>(() => context.Database.EnsureDeleted());
            Assert.Empty(capture.Opens);
            Assert.Empty(capture.Commands);
            using var stillUsable = target.CreateCommand();
            if (profile is "mode" or "database")
                Assert.Throws<ObjectDisposedException>(() => candidate!.CreateCommand());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Each_operation_requests_and_disposes_its_own_management_connection_without_disposing_the_supplied_target(bool async)
        {
            var capture = new Capture { NamespaceExists = true };
            using var target = new OceanBaseConnection("Server=offline;Database=owned");
            var candidates = new List<OceanBaseConnection>();
            await using (var context = Create(capture, configure: builder =>
                builder.UseOceanBase(target, OceanBase.EntityFrameworkCore.OceanBaseCompatibilityMode.MySql)
                    .UseOceanBaseManagementConnection(() =>
                    {
                        var candidate = new OceanBaseConnection("Server=offline;User Id=administrator");
                        candidates.Add(candidate);
                        return candidate;
                    })))
            {
                Assert.True(async ? await context.Database.EnsureDeletedAsync() : context.Database.EnsureDeleted());
                Assert.False(async ? await context.Database.EnsureDeletedAsync() : context.Database.EnsureDeleted());
            }
            Assert.Equal(2, candidates.Count);
            Assert.NotSame(candidates[0], candidates[1]);
            Assert.Equal(candidates, capture.Disposals.Cast<OceanBaseConnection>());
            Assert.All(candidates, candidate => Assert.Throws<ObjectDisposedException>(() => candidate.CreateCommand()));
            using var stillUsable = target.CreateCommand();
        }

        [Theory]
        [InlineData(false, "table-only")]
        [InlineData(true, "table-only")]
        [InlineData(false, "oracle")]
        [InlineData(true, "oracle")]
        [InlineData(false, "mismatch")]
        [InlineData(true, "mismatch")]
        [InlineData(false, "cross-scope")]
        [InlineData(true, "cross-scope")]
        public async Task Invalid_ownership_or_model_rejects_before_requesting_privileged_credentials(bool async, string profile)
        {
            var capture = new Capture();
            var calls = 0;
            await using var context = Create(capture, profile, configure: builder => builder.UseOceanBaseManagementConnection(() =>
            {
                calls++;
                throw new InvalidOperationException("Must not request management credentials.");
            }));
            var failure = await Record.ExceptionAsync(async () =>
            {
                if (profile == "cross-scope")
                {
                    if (async) await context.Database.EnsureCreatedAsync(); else context.Database.EnsureCreated();
                }
                else if (async) await context.Database.EnsureDeletedAsync(); else context.Database.EnsureDeleted();
            });
            Assert.NotNull(failure);
            Assert.Equal(0, calls);
            Assert.Empty(capture.Opens);
            Assert.Empty(capture.Commands);
        }

        [Theory]
        [InlineData(false, "factory")]
        [InlineData(true, "factory")]
        [InlineData(false, "open")]
        [InlineData(true, "open")]
        [InlineData(false, "command")]
        [InlineData(true, "command")]
        [InlineData(false, "cleanup")]
        [InlineData(true, "cleanup")]
        public async Task Factory_open_command_and_cleanup_failures_preserve_the_primary_error(bool async, string stage)
        {
            var failure = new InvalidOperationException("synthetic primary failure");
            var cleanup = new InvalidOperationException("synthetic cleanup failure");
            var capture = new Capture { NamespaceExists = true, CleanupFailure = cleanup };
            if (stage == "open") capture.OpenFailure = failure;
            if (stage == "command") { capture.Failure = failure; capture.FailPrefix = "DROP DATABASE"; }
            OceanBaseConnection? candidate = null;
            await using var context = Create(capture, configure: builder => builder.UseOceanBaseManagementConnection(() =>
            {
                if (stage == "factory") throw failure;
                return candidate = new OceanBaseConnection("Server=offline;User Id=administrator");
            }));
            var actual = async ? await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.EnsureDeletedAsync())
                : Assert.Throws<InvalidOperationException>(() => context.Database.EnsureDeleted());
            Assert.Same(stage == "cleanup" ? cleanup : failure, actual);
            if (stage is "open" or "command") Assert.Same(cleanup, failure.Data["OceanBase.DatabaseManagementCleanupError"]);
            if (candidate is not null)
            {
                Assert.Same(candidate, Assert.Single(capture.Disposals));
                Assert.Throws<ObjectDisposedException>(() => candidate.CreateCommand());
            }
            else Assert.Empty(capture.Opens);
        }

        [Fact]
        public async Task Precancelled_factory_operations_never_request_credentials_and_inflight_cancellation_keeps_ownership()
        {
            var capture = new Capture { NamespaceExists = true };
            var calls = 0;
            await using var context = Create(capture, configure: builder => builder.UseOceanBaseManagementConnection(() =>
            {
                calls++;
                return new OceanBaseConnection("Server=offline");
            }));
            using var source = new CancellationTokenSource();
            source.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Database.EnsureDeletedAsync(source.Token));
            Assert.Equal(0, calls);
            using var running = new CancellationTokenSource();
            var failure = new OperationCanceledException(running.Token);
            capture.Failure = failure;
            capture.FailPrefix = "DROP DATABASE";
            capture.BeforeFailure = running.Cancel;
            Assert.Same(failure, await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Database.EnsureDeletedAsync(running.Token)));
            Assert.Equal(1, calls);
            Assert.Single(capture.Disposals);
            Assert.All(capture.Commands, command => Assert.Equal(running.Token, command.Token));
        }

        private static void ConfigureManagement(DbContextOptionsBuilder builder, Func<OceanBaseConnection> factory)
            => builder.UseOceanBaseManagementConnection(factory);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Factory_creation_runs_namespace_ddl_before_supplied_target_tables(bool async)
        {
            var capture = new Capture();
            using var target = new OceanBaseConnection("Server=offline;Database=owned");
            await using var context = Create(capture, configure: builder =>
                builder.UseOceanBase(target, OceanBase.EntityFrameworkCore.OceanBaseCompatibilityMode.MySql)
                    .UseOceanBaseManagementConnection(() => new OceanBaseConnection("Server=offline;User Id=administrator")));
            Assert.True(async ? await context.Database.EnsureCreatedAsync() : context.Database.EnsureCreated());
            Assert.Equal(4, capture.Commands.Count);
            Assert.Equal("CREATE DATABASE `owned`", capture.Commands[1].Sql);
            Assert.NotSame(target, capture.Commands[1].Connection);
            Assert.Same(target, capture.Commands[2].Connection);
            Assert.Same(target, capture.Commands[3].Connection);
            Assert.StartsWith("CREATE TABLE", capture.Commands[3].Sql);
        }

        [Theory]
        [InlineData(OceanBase.EntityFrameworkCore.OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBase.EntityFrameworkCore.OceanBaseCompatibilityMode.Oracle)]
        public async Task Table_only_schema_and_bulk_query_operations_do_not_request_management_credentials(OceanBase.EntityFrameworkCore.OceanBaseCompatibilityMode mode)
        {
            var capture = new Capture();
            var calls = 0;
            await using var context = Create(capture, "table-only", configure: builder =>
                builder.UseOceanBase("Server=offline;Database=owned", mode).UseOceanBaseManagementConnection(() =>
                {
                    calls++;
                    throw new InvalidOperationException("Must not request management credentials.");
                }));
            Assert.Contains("CREATE TABLE", context.Database.GenerateCreateScript());
            Assert.True(await context.Database.EnsureCreatedAsync());
            Assert.Equal(0, await context.Set<Row>().ExecuteDeleteAsync());
            Assert.Equal(0, calls);
            var target = context.Database.GetDbConnection();
            Assert.All(capture.Commands, command => Assert.Same(target, command.Connection));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Caller_and_ambient_transactions_reject_before_invoking_the_management_factory(bool async)
        {
            var capture = new Capture();
            var calls = 0;
            await using var context = Create(capture, configure: builder => builder.UseOceanBaseManagementConnection(() =>
            {
                calls++;
                throw new InvalidOperationException("Must not request management credentials.");
            }));
            using (var native = new CallerTransaction(context.Database.GetDbConnection()))
            {
                await using var transaction = context.Database.UseTransaction(native)!;
                if (async) await Assert.ThrowsAsync<NotSupportedException>(() => context.Database.EnsureDeletedAsync());
                else Assert.Throws<NotSupportedException>(() => context.Database.EnsureDeleted());
                Assert.Same(transaction, context.Database.CurrentTransaction);
            }
            using (var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
            {
                if (async) await Assert.ThrowsAsync<NotSupportedException>(() => context.Database.EnsureDeletedAsync());
                else Assert.Throws<NotSupportedException>(() => context.Database.EnsureDeleted());
            }
            Assert.Equal(0, calls);
            Assert.Empty(capture.Commands);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Direct_create_and_delete_use_a_separate_connection_without_a_selected_database(bool async, bool delete)
        {
            var capture = new Capture { NamespaceExists = delete };
            await using var context = Create(capture);
            var target = context.Database.GetDbConnection();
            context.Database.SetCommandTimeout(17);
            var creator = context.GetService<IRelationalDatabaseCreator>();
            if (delete)
            {
                if (async) await creator.DeleteAsync();
                else creator.Delete();
            }
            else
            {
                if (async) await creator.CreateAsync();
                else creator.Create();
            }
            var command = Assert.Single(capture.Commands);
            Assert.Equal(delete ? "DROP DATABASE `owned`" : "CREATE DATABASE `owned`", command.Sql);
            Assert.Equal("", command.Connection.Database);
            Assert.NotSame(target, command.Connection);
            Assert.Equal(async, command.Async);
            Assert.Equal(17, command.Timeout);
            Assert.Null(command.Transaction);
            Assert.Equal("owned", target.Database);
            Assert.DoesNotContain(target, capture.Opens);
            Assert.Single(capture.Disposals);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task EnsureDeleted_binds_the_name_and_classifies_only_unknown_database_as_absence(bool async, bool exists)
        {
            var capture = new Capture { NamespaceExists = exists };
            await using var context = Create(capture);
            Assert.Equal(exists, async ? await context.Database.EnsureDeletedAsync() : context.Database.EnsureDeleted());
            Assert.Equal(2, capture.Commands.Count);
            var probe = capture.Commands[0];
            Assert.Contains("information_schema.schemata", probe.Sql);
            Assert.Contains("SCHEMA_NAME = ?", probe.Sql);
            Assert.DoesNotContain("owned", probe.Sql);
            Assert.Equal("owned", Assert.Single(probe.Values));
            Assert.All(capture.Commands, command => Assert.Equal("", command.Connection.Database));
            Assert.Equal("DROP DATABASE `owned`", capture.Commands[1].Sql);
            Assert.Single(capture.Disposals);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task EnsureCreated_creates_namespace_then_tables_and_preserves_target_configuration(bool async, bool exists)
        {
            var capture = new Capture { NamespaceExists = exists };
            await using var context = Create(capture);
            var target = context.Database.GetDbConnection();
            using var source = new CancellationTokenSource();
            Assert.True(async ? await context.Database.EnsureCreatedAsync(source.Token) : context.Database.EnsureCreated());
            Assert.Equal(4, capture.Commands.Count);
            Assert.Contains("information_schema.schemata", capture.Commands[0].Sql);
            if (!exists) Assert.Equal("CREATE DATABASE `owned`", capture.Commands[1].Sql);
            var targetCommands = capture.Commands.Where(command => ReferenceEquals(command.Connection, target)).ToArray();
            Assert.Equal(exists ? 3 : 2, targetCommands.Length);
            Assert.Equal("SELECT DATABASE()", targetCommands[0].Sql);
            Assert.StartsWith("CREATE TABLE", targetCommands[^1].Sql);
            Assert.All(capture.Commands, command =>
            {
                Assert.Equal(async, command.Async);
                if (async) Assert.Equal(source.Token, command.Token);
            });
            Assert.Equal("owned", target.Database);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Populated_schemas_are_preserved_and_empty_models_can_create_an_empty_namespace(bool async)
        {
            var populated = new Capture { NamespaceExists = true, HasTables = true };
            await using (var context = Create(populated))
                Assert.False(async ? await context.Database.EnsureCreatedAsync() : context.Database.EnsureCreated());
            Assert.Equal(3, populated.Commands.Count);
            Assert.All(populated.Commands, command => Assert.StartsWith("SELECT", command.Sql));
            var empty = new Capture();
            await using (var context = Create(empty, empty: true))
                Assert.True(async ? await context.Database.EnsureCreatedAsync() : context.Database.EnsureCreated());
            Assert.Equal(2, empty.Commands.Count);
            Assert.Equal("CREATE DATABASE `owned`", empty.Commands[1].Sql);
            Assert.Single(empty.Opens.Distinct());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Missing_ownership_Oracle_and_supplied_connections_reject_without_management_open(bool async)
        {
            foreach (var profile in new[] { "table-only", "oracle", "supplied", "mismatch" })
            {
                var capture = new Capture();
                await using var context = Create(capture, profile);
                var failure = await Record.ExceptionAsync(async () =>
                {
                    if (async) await context.Database.EnsureDeletedAsync();
                    else context.Database.EnsureDeleted();
                });
                if (profile == "mismatch") Assert.IsType<InvalidOperationException>(failure);
                else Assert.IsType<NotSupportedException>(failure);
                Assert.Empty(capture.Opens);
                Assert.Empty(capture.Commands);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Scope_spelling_and_catalog_errors_are_not_treated_as_absence(bool async)
        {
            foreach (var result in new object[] { "OWNED", 1 })
            {
                var capture = new Capture { NamespaceExists = true, CatalogValue = result };
                await using var context = Create(capture);
                if (async) await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.EnsureDeletedAsync());
                else Assert.Throws<InvalidOperationException>(() => context.Database.EnsureDeleted());
                Assert.Single(capture.Commands);
                Assert.Single(capture.Disposals);
            }
            var failure = new InvalidOperationException("synthetic catalog access denied");
            var failing = new Capture { Failure = failure, FailPrefix = "SELECT SCHEMA_NAME" };
            await using var failed = Create(failing);
            var actual = async ? await Assert.ThrowsAsync<InvalidOperationException>(() => failed.Database.EnsureDeletedAsync())
                : Assert.Throws<InvalidOperationException>(() => failed.Database.EnsureDeleted());
            Assert.Same(failure, actual);
            Assert.Single(failing.Commands);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Database_identifiers_are_quoted_and_probes_bind_the_entire_name(bool async)
        {
            const string scope = "owned`; DROP DATABASE other; --";
            var capture = new Capture { NamespaceExists = true, CatalogValue = scope };
            await using var context = Create(capture, scope: scope);
            Assert.True(async ? await context.Database.EnsureDeletedAsync() : context.Database.EnsureDeleted());
            Assert.Equal(scope, Assert.Single(capture.Commands[0].Values));
            Assert.Equal("DROP DATABASE `owned``; DROP DATABASE other; --`", capture.Commands[1].Sql);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Ambient_transactions_reject_before_any_open(bool async)
        {
            var capture = new Capture();
            await using var context = Create(capture);
            using var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
            if (async) await Assert.ThrowsAsync<NotSupportedException>(() => context.Database.EnsureDeletedAsync());
            else Assert.Throws<NotSupportedException>(() => context.Database.EnsureDeleted());
            Assert.Empty(capture.Opens);
        }

        [Fact]
        public async Task Precancelled_management_never_opens_and_command_cancellation_keeps_the_original_token()
        {
            var capture = new Capture();
            await using var context = Create(capture);
            using var source = new CancellationTokenSource();
            source.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Database.EnsureDeletedAsync(source.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.GetService<IRelationalDatabaseCreator>().CreateAsync(source.Token));
            Assert.Empty(capture.Opens);
            using var running = new CancellationTokenSource();
            var cancellation = new OperationCanceledException(running.Token);
            capture.Failure = cancellation;
            capture.FailPrefix = "SELECT SCHEMA_NAME";
            capture.BeforeFailure = running.Cancel;
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Database.EnsureDeletedAsync(running.Token));
            Assert.Same(cancellation, error);
            Assert.True(running.IsCancellationRequested);
            Assert.Equal(running.Token, Assert.Single(capture.Commands).Token);
            Assert.Single(capture.Disposals);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Failed_table_creation_does_not_drop_the_new_namespace(bool async)
        {
            var failure = new InvalidOperationException("synthetic table failure");
            var capture = new Capture { Failure = failure, FailPrefix = "CREATE TABLE" };
            await using var context = Create(capture);
            var actual = async ? await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.EnsureCreatedAsync())
                : Assert.Throws<InvalidOperationException>(() => context.Database.EnsureCreated());
            Assert.Same(failure, actual);
            Assert.Contains(capture.Commands, command => command.Sql == "CREATE DATABASE `owned`");
            Assert.DoesNotContain(capture.Commands, command => command.Sql.StartsWith("DROP", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Invisible_catalog_rows_do_not_hide_authorization_errors_or_prevent_owned_deletion(bool async)
        {
            var visible = new Capture { NamespaceExists = true, CatalogVisible = false };
            await using (var context = Create(visible))
                Assert.True(async ? await context.Database.EnsureDeletedAsync() : context.Database.EnsureDeleted());
            var denied = ServerError(1044);
            var capture = new Capture { Failure = denied, FailPrefix = "DROP DATABASE" };
            await using var failed = Create(capture);
            var actual = async ? await Assert.ThrowsAsync<OceanBaseException>(() => failed.Database.EnsureDeletedAsync())
                : Assert.Throws<OceanBaseException>(() => failed.Database.EnsureDeleted());
            Assert.Same(denied, actual);
            Assert.Equal(2, capture.Commands.Count);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Disposal_failures_preserve_the_operation_error_and_propagate_after_success(bool async)
        {
            var primary = new InvalidOperationException("synthetic DDL failure");
            var cleanup = new InvalidOperationException("synthetic dispose failure");
            var capture = new Capture { Failure = primary, FailPrefix = "CREATE DATABASE", CleanupFailure = cleanup };
            await using (var context = Create(capture))
            {
                var creator = context.GetService<IRelationalDatabaseCreator>();
                var actual = async ? await Assert.ThrowsAsync<InvalidOperationException>(() => creator.CreateAsync())
                    : Assert.Throws<InvalidOperationException>(() => creator.Create());
                Assert.Same(primary, actual);
                Assert.Same(cleanup, actual.Data["OceanBase.DatabaseManagementCleanupError"]);
            }
            var successful = new Capture { CleanupFailure = cleanup };
            await using var success = Create(successful);
            var service = success.GetService<IRelationalDatabaseCreator>();
            var disposal = async ? await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync())
                : Assert.Throws<InvalidOperationException>(() => service.Create());
            Assert.Same(cleanup, disposal);
        }

        private static OceanBaseException ServerError(int code)
            => ServerError(code, false);

        private static OceanBaseException ServerError(int code, bool unknownOutcome)
            => (OceanBaseException)Activator.CreateInstance(typeof(OceanBaseException),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                ["synthetic namespace error", code, "42000", unknownOutcome, null, null], null)!;

        [Theory]
        [InlineData(false, 1008, true)]
        [InlineData(true, 1008, true)]
        [InlineData(false, 1044, false)]
        [InlineData(true, 1044, false)]
        [InlineData(false, 2003, false)]
        [InlineData(true, 2003, false)]
        public async Task Unknown_outcomes_permission_and_network_errors_are_not_absence(bool async, int code, bool unknownOutcome)
        {
            var failure = ServerError(code, unknownOutcome);
            var capture = new Capture { Failure = failure, FailPrefix = "DROP DATABASE" };
            await using var context = Create(capture);
            var actual = async ? await Assert.ThrowsAsync<OceanBaseException>(() => context.Database.EnsureDeletedAsync())
                : Assert.Throws<OceanBaseException>(() => context.Database.EnsureDeleted());
            Assert.Same(failure, actual);
            Assert.Single(capture.Disposals);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Direct_delete_keeps_unknown_database_errors_and_failed_open_disposes_the_owned_handle(bool async)
        {
            var capture = new Capture();
            await using var context = Create(capture);
            var creator = context.GetService<IRelationalDatabaseCreator>();
            var missing = async ? await Assert.ThrowsAsync<OceanBaseException>(() => creator.DeleteAsync())
                : Assert.Throws<OceanBaseException>(() => creator.Delete());
            Assert.Equal(1008, missing.ServerErrorCode);
            var opening = new InvalidOperationException("synthetic management open failure");
            capture.OpenFailure = opening;
            var actual = async ? await Assert.ThrowsAsync<InvalidOperationException>(() => creator.CreateAsync())
                : Assert.Throws<InvalidOperationException>(() => creator.Create());
            Assert.Same(opening, actual);
            Assert.Equal(2, capture.Disposals.Count);
            Assert.Single(capture.Commands);
        }

        [Theory]
        [InlineData(false, "replacement")]
        [InlineData(true, "replacement")]
        [InlineData(false, "server")]
        [InlineData(true, "server")]
        [InlineData(false, "password")]
        [InlineData(true, "password")]
        [InlineData(false, "resolver")]
        [InlineData(true, "resolver")]
        [InlineData(false, "credentials")]
        [InlineData(true, "credentials")]
        [InlineData(false, "tls")]
        [InlineData(true, "tls")]
        [InlineData(false, "certificate")]
        [InlineData(true, "certificate")]
        public async Task Runtime_connection_replacement_mutation_and_native_hooks_reject_before_management_open(bool async, string profile)
        {
            var capture = new Capture { NamespaceExists = true };
            await using var context = Create(capture);
            using var replacement = new OceanBaseConnection("Server=offline;Database=owned");
            using var key = System.Security.Cryptography.RSA.Create(2048);
            using var certificate = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                "CN=offline", key, System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pkcs1)
                .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            var target = (OceanBaseConnection)context.Database.GetDbConnection();
            switch (profile)
            {
                case "replacement": context.Database.SetDbConnection(replacement); break;
                case "server": target.ConnectionString = "Server=other;Database=owned"; break;
                case "password": target.ConnectionString = "Server=offline;Database=owned;Password=synthetic"; break;
                case "resolver": target.AddressResolver = new NativeHooks(); break;
                case "credentials": target.CredentialProvider = new NativeHooks(); break;
                case "tls": target.RemoteCertificateValidationCallback = (_, _, _, _) => false; break;
                case "certificate": target.ClientCertificate = certificate; break;
            }
            capture.Disposals.Clear();
            if (async) await Assert.ThrowsAsync<NotSupportedException>(() => context.Database.EnsureDeletedAsync());
            else Assert.Throws<NotSupportedException>(() => context.Database.EnsureDeleted());
            Assert.Empty(capture.Opens);
            Assert.Empty(capture.Commands);
            Assert.Empty(capture.Disposals);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Ef_runtime_connection_string_changes_keep_management_and_target_on_the_same_server(bool async)
        {
            var capture = new Capture { NamespaceExists = true };
            await using var context = Create(capture);
            var target = context.Database.GetDbConnection();
            context.Database.SetConnectionString("Database=owned;Server=changed");
            Assert.True(async ? await context.Database.EnsureDeletedAsync() : context.Database.EnsureDeleted());
            Assert.All(capture.Commands, command => Assert.Equal("changed", command.Connection.DataSource));
            Assert.Equal("changed", target.DataSource);
            Assert.Equal("owned", target.Database);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Caller_transactions_are_preserved_without_opening_a_management_connection(bool async)
        {
            var capture = new Capture();
            await using var context = Create(capture);
            using var native = new CallerTransaction(context.Database.GetDbConnection());
            await using var transaction = context.Database.UseTransaction(native)!;
            var opens = capture.Opens.Count;
            if (async) await Assert.ThrowsAsync<NotSupportedException>(() => context.Database.EnsureDeletedAsync());
            else Assert.Throws<NotSupportedException>(() => context.Database.EnsureDeleted());
            Assert.Same(transaction, context.Database.CurrentTransaction);
            Assert.Equal(opens, capture.Opens.Count);
            Assert.Empty(capture.Commands);
        }

        [Theory]
        [InlineData(false, "cross-scope")]
        [InlineData(true, "cross-scope")]
        [InlineData(false, "sequence")]
        [InlineData(true, "sequence")]
        public async Task Invalid_table_plans_reject_before_creating_a_database(bool async, string profile)
        {
            var capture = new Capture();
            await using var context = Create(capture, profile);
            if (async) await Assert.ThrowsAsync<NotSupportedException>(() => context.Database.EnsureCreatedAsync());
            else Assert.Throws<NotSupportedException>(() => context.Database.EnsureCreated());
            Assert.Empty(capture.Opens);
            Assert.Empty(capture.Commands);
        }

        private sealed class CallerTransaction(DbConnection connection) : DbTransaction
        {
            protected override DbConnection DbConnection => connection;
            public override System.Data.IsolationLevel IsolationLevel => System.Data.IsolationLevel.ReadCommitted;
            public override void Commit() => throw new InvalidOperationException("Must not commit.");
            public override void Rollback() => throw new InvalidOperationException("Must not roll back.");
        }

        private sealed class NativeHooks : IOceanBaseAddressResolver, IOceanBaseCredentialProvider
        {
            public string Resolve(CancellationToken cancellationToken) => throw new InvalidOperationException("Must not resolve.");
            public Task<string> ResolveAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Must not resolve.");
            public OceanBaseCredential GetCredential(OceanBaseCredentialContext context, CancellationToken cancellationToken)
                => throw new InvalidOperationException("Must not request credentials.");
            public Task<OceanBaseCredential> GetCredentialAsync(OceanBaseCredentialContext context, CancellationToken cancellationToken)
                => throw new InvalidOperationException("Must not request credentials.");
        }

        private static TestContext Create(Capture capture, string profile = "ordinary", string scope = "owned", bool empty = false,
            Action<DbContextOptionsBuilder<TestContext>>? configure = null)
        {
            var native = new OceanBaseConnectionStringBuilder { Server = "offline", Database = profile == "mismatch" ? "other" : scope };
            var builder = new DbContextOptionsBuilder<TestContext>().UseOceanBase(native.ConnectionString,
                profile == "oracle" ? OceanBase.EntityFrameworkCore.OceanBaseCompatibilityMode.Oracle : OceanBase.EntityFrameworkCore.OceanBaseCompatibilityMode.MySql)
                .AddInterceptors(capture).ReplaceService<IModelCacheKeyFactory, ShapeKey>();
            if (profile == "table-only") builder.EnableOceanBaseSchemaManagement(scope);
            else builder.EnableOceanBaseDatabaseManagement(scope);
            if (profile == "supplied") builder.UseOceanBase(new OceanBaseConnection(native.ConnectionString), OceanBase.EntityFrameworkCore.OceanBaseCompatibilityMode.MySql);
            configure?.Invoke(builder);
            return new TestContext(builder.Options, empty, profile == "cross-scope", profile == "sequence");
        }

        public sealed class ShapeKey(ModelCacheKeyFactoryDependencies dependencies) : ModelCacheKeyFactory(dependencies)
        {
            public override object Create(DbContext context, bool designTime)
                => (base.Create(context, designTime), ((TestContext)context).Empty, ((TestContext)context).CrossScope, ((TestContext)context).Sequence);
        }

        private sealed class TestContext(DbContextOptions<TestContext> options, bool empty, bool crossScope, bool sequence) : DbContext(options)
        {
            public bool Empty => empty;
            public bool CrossScope => crossScope;
            public bool Sequence => sequence;
            protected override void OnModelCreating(ModelBuilder model)
            {
                if (!Empty) model.Entity<Row>().ToTable("Rows", CrossScope ? "other" : null);
                if (Sequence) model.HasSequence<long>("UnsupportedSequence");
            }
        }
        private sealed class Row { public int Id { get; set; } }
        private sealed record Command(string Sql, DbConnection Connection, bool Async, CancellationToken Token, int Timeout, DbTransaction? Transaction, object[] Values);

        private sealed class Capture : DbConnectionInterceptor, IDbCommandInterceptor
        {
            public List<DbConnection> Opens { get; } = [];
            public List<DbConnection> Disposals { get; } = [];
            public List<Command> Commands { get; } = [];
            public bool NamespaceExists { get; set; }
            public bool CatalogVisible { get; init; } = true;
            public object? CatalogValue { get; init; }
            public bool HasTables { get; init; }
            public Exception? Failure { get; set; }
            public string? FailPrefix { get; set; }
            public Exception? CleanupFailure { get; set; }
            public Exception? OpenFailure { get; set; }
            public Action? BeforeFailure { get; set; }
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            {
                Opens.Add(connection);
                if (OpenFailure is { } failure) throw failure;
                return InterceptionResult.Suppress();
            }
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ConnectionOpening(connection, eventData, result));
            public override void ConnectionDisposed(DbConnection connection, ConnectionEndEventData eventData)
            {
                Disposals.Add(connection);
                if (connection.Database == "" && CleanupFailure is { } error)
                {
                    CleanupFailure = null;
                    throw error;
                }
            }
            public override Task ConnectionDisposedAsync(DbConnection connection, ConnectionEndEventData eventData)
            { ConnectionDisposed(connection, eventData); return Task.CompletedTask; }
            public InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
                => InterceptionResult<object>.SuppressWithResult(Scalar(command, false, default));
            public ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<object> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(InterceptionResult<object>.SuppressWithResult(Scalar(command, true, cancellationToken)));
            private object Scalar(DbCommand command, bool async, CancellationToken token)
            {
                Record(command, async, token);
                if (command.CommandText.StartsWith("SELECT SCHEMA_NAME", StringComparison.Ordinal))
                    return NamespaceExists && CatalogVisible ? CatalogValue ?? "owned" : DBNull.Value;
                if (command.CommandText.StartsWith("SELECT COUNT", StringComparison.Ordinal)) return HasTables ? 1 : 0;
                return command.Connection!.Database;
            }
            public InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
                => Execute(command, false, default);
            public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<int> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(Execute(command, true, cancellationToken));
            private InterceptionResult<int> Execute(DbCommand command, bool async, CancellationToken token)
            {
                Record(command, async, token);
                if (command.CommandText.StartsWith("CREATE DATABASE", StringComparison.Ordinal)) NamespaceExists = true;
                if (command.CommandText.StartsWith("DROP DATABASE", StringComparison.Ordinal))
                {
                    if (!NamespaceExists) throw ServerError(1008);
                    NamespaceExists = false;
                }
                return InterceptionResult<int>.SuppressWithResult(0);
            }
            private void Record(DbCommand command, bool async, CancellationToken token)
            {
                Commands.Add(new Command(command.CommandText, command.Connection!, async, token, command.CommandTimeout, command.Transaction,
                    command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value!).ToArray()));
                if (Failure is not null && FailPrefix is not null && command.CommandText.StartsWith(FailPrefix, StringComparison.Ordinal))
                {
                    BeforeFailure?.Invoke();
                    throw Failure;
                }
            }
        }
    }
}
