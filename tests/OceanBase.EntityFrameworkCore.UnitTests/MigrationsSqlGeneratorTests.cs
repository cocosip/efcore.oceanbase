using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using OceanBase.EntityFrameworkCore.Internal;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class MigrationsSqlGeneratorTests
    {
        [Fact]
        public void Database_collation_migrations_are_explicit_and_reversible_in_an_owned_MySql_database()
        {
            var options = new DbContextOptionsBuilder().UseOceanBase("Server=offline;Database=app", OceanBaseCompatibilityMode.MySql)
                .EnableOceanBaseDatabaseManagement("app");
            using var owned = new DbContext(options.Options);
            using var existing = Create(OceanBaseCompatibilityMode.MySql, scoped: true);
            using var oracle = Create(OceanBaseCompatibilityMode.Oracle, scoped: true);
            var upgrade = new AlterDatabaseOperation { Collation = "utf8mb4_bin", OldDatabase = { Collation = "utf8mb4_general_ci" } };
            var rollback = new AlterDatabaseOperation { Collation = "utf8mb4_general_ci", OldDatabase = { Collation = "utf8mb4_bin" } };

            Assert.Contains("ALTER DATABASE `app` DEFAULT COLLATE utf8mb4_bin;", Sql(owned, [upgrade]));
            Assert.Contains("ALTER DATABASE `app` DEFAULT COLLATE utf8mb4_general_ci;", Sql(owned, [rollback]));
            Assert.True(Assert.Single(owned.GetService<IMigrationsSqlGenerator>().Generate([upgrade])).TransactionSuppressed);
            Assert.Throws<NotSupportedException>(() => Sql(existing, [upgrade]));
            Assert.Throws<NotSupportedException>(() => Sql(oracle, [upgrade]));
            Assert.Throws<NotSupportedException>(() => Sql(owned, [new AlterDatabaseOperation { OldDatabase = { Collation = "utf8mb4_bin" } }]));
            Assert.Throws<NotSupportedException>(() => Sql(owned, [new AlterDatabaseOperation { Collation = "utf8mb4_bin;SELECT" }]));
            Assert.Throws<NotSupportedException>(() => Sql(owned, [new AlterDatabaseOperation
                { Collation = "utf8mb4_bin", OldDatabase = { Collation = "bad collation" } }]));
        }

        public static IEnumerable<object[]> Modes()
            => Enum.GetValues<OceanBaseCompatibilityMode>().Select(mode => new object[] { mode });

        [Theory]
        [MemberData(nameof(Modes))]
        public void Identity_toggle_requires_an_unchanged_single_integer_primary_key_in_MySql(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode, scoped: true);
            var assigned = Model(context, evolved: false, assignedKey: true);
            var generated = Model(context, evolved: false);
            var differ = context.GetService<IMigrationsModelDiffer>();
            var generator = context.GetService<IMigrationsSqlGenerator>();
            var up = Assert.IsType<AlterColumnOperation>(Assert.Single(differ.GetDifferences(assigned.GetRelationalModel(), generated.GetRelationalModel())));
            var down = Assert.IsType<AlterColumnOperation>(Assert.Single(differ.GetDifferences(generated.GetRelationalModel(), assigned.GetRelationalModel())));
            if (mode == OceanBaseCompatibilityMode.Oracle)
            {
                Assert.Throws<NotSupportedException>(() => generator.Generate([up], generated));
                Assert.Throws<NotSupportedException>(() => generator.Generate([down], assigned));
                return;
            }

            Assert.Contains("MODIFY COLUMN `Id` INT NOT NULL AUTO_INCREMENT", Assert.Single(generator.Generate([up], generated)).CommandText);
            Assert.Contains("MODIFY COLUMN `Id` INT NOT NULL;", Assert.Single(generator.Generate([down], assigned)).CommandText);
            Assert.Throws<NotSupportedException>(() => generator.Generate([up]));
            Assert.Throws<NotSupportedException>(() => generator.Generate([up], Model(context, evolved: false, compositeKey: true)));
            Assert.Throws<NotSupportedException>(() => generator.Generate(
                [new DropPrimaryKeyOperation { Table = "Rows", Schema = "app", Name = "PK_Rows" }, up], generated));
            Assert.Throws<NotSupportedException>(() => generator.Generate(
                [new RenameTableOperation { Name = "PreviousRows", NewName = "Rows" }, up], generated));
            Assert.Throws<NotSupportedException>(() => generator.Generate([up], assigned));
            using var unscoped = Create(mode);
            Assert.Throws<NotSupportedException>(() => unscoped.GetService<IMigrationsSqlGenerator>().Generate(
                [new DropPrimaryKeyOperation { Table = "Rows", Schema = "app", Name = "PK_Rows" }, up], generated));
            up.OldColumn.ColumnType = "BIGINT";
            Assert.Throws<NotSupportedException>(() => generator.Generate([up], generated));
        }

        [Fact]
        public void Database_collation_model_differences_generate_initial_upgrade_and_explicit_rollback_sql()
        {
            var options = new DbContextOptionsBuilder().UseOceanBase("Server=offline;Database=app", OceanBaseCompatibilityMode.MySql)
                .EnableOceanBaseDatabaseManagement("app");
            using var context = new DbContext(options.Options);
            var implicitDefault = Model(context, evolved: false);
            var original = Model(context, evolved: false, collation: "utf8mb4_general_ci");
            var changed = Model(context, evolved: false, collation: "utf8mb4_bin");
            var differ = context.GetService<IMigrationsModelDiffer>();
            var generator = context.GetService<IMigrationsSqlGenerator>();

            var initial = generator.Generate(differ.GetDifferences(null, changed.GetRelationalModel()), changed, MigrationsSqlGenerationOptions.Script);
            Assert.StartsWith("ALTER DATABASE `app` DEFAULT COLLATE utf8mb4_bin;", initial[0].CommandText);
            Assert.Contains(initial, command => command.CommandText.StartsWith("CREATE TABLE", StringComparison.Ordinal));
            var initialDown = generator.Generate(differ.GetDifferences(changed.GetRelationalModel(), null), options: MigrationsSqlGenerationOptions.Script);
            Assert.StartsWith("DROP TABLE", Assert.Single(initialDown).CommandText);

            var up = generator.Generate(differ.GetDifferences(original.GetRelationalModel(), changed.GetRelationalModel()), changed, MigrationsSqlGenerationOptions.Script);
            Assert.StartsWith("ALTER DATABASE `app` DEFAULT COLLATE utf8mb4_bin;", Assert.Single(up).CommandText);
            var down = generator.Generate(differ.GetDifferences(changed.GetRelationalModel(), original.GetRelationalModel()), original, MigrationsSqlGenerationOptions.Script);
            Assert.StartsWith("ALTER DATABASE `app` DEFAULT COLLATE utf8mb4_general_ci;", Assert.Single(down).CommandText);
            Assert.Throws<NotSupportedException>(() => generator.Generate(
                differ.GetDifferences(implicitDefault.GetRelationalModel(), changed.GetRelationalModel()), changed,
                MigrationsSqlGenerationOptions.Script));
            Assert.Throws<NotSupportedException>(() => generator.Generate(
                differ.GetDifferences(changed.GetRelationalModel(), implicitDefault.GetRelationalModel()), implicitDefault,
                MigrationsSqlGenerationOptions.Script));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Real_model_differences_preserve_identity_defaults_and_schema_evolution(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var initial = Model(context, evolved: false);
            var evolved = Model(context, evolved: true);
            var differ = context.GetService<IMigrationsModelDiffer>();
            var generator = context.GetService<IMigrationsSqlGenerator>();
            var create = generator.Generate(differ.GetDifferences(null, initial.GetRelationalModel()), initial);
            var sql = string.Concat(create.Select(c => c.CommandText));
            Assert.Contains("CREATE TABLE", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "AUTO_INCREMENT" : "GENERATED BY DEFAULT AS IDENTITY", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "VARCHAR(32)" : "NVARCHAR2(32)", sql);
            Assert.Contains("DEFAULT", sql);
            Assert.Contains("can't".Replace("'", "''", StringComparison.Ordinal), sql);
            Assert.All(create, c => Assert.True(c.TransactionSuppressed));

            var change = generator.Generate(differ.GetDifferences(initial.GetRelationalModel(), evolved.GetRelationalModel()), evolved);
            var changeSql = string.Concat(change.Select(c => c.CommandText));
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "MODIFY COLUMN" : "MODIFY (", changeSql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "VARCHAR(64)" : "NVARCHAR2(64)", changeSql);
            Assert.Contains("ADD", changeSql);
            if (mode == OceanBaseCompatibilityMode.Oracle) Assert.DoesNotContain("NOT NULL", changeSql);
            var reverse = generator.Generate(differ.GetDifferences(evolved.GetRelationalModel(), initial.GetRelationalModel()), initial);
            Assert.Contains("DROP COLUMN", string.Concat(reverse.Select(c => c.CommandText)));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Constraints_indexes_and_renames_use_each_dialect(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var builder = new MigrationBuilder(context.Database.ProviderName!);
            builder.AddPrimaryKey("PK_Rows", "Rows", "Id");
            builder.AddUniqueConstraint("AK_Rows", "Rows", "Code");
            builder.AddForeignKey("FK_Rows", "Rows", "ParentId", "Parents", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            builder.AddCheckConstraint("CK_Rows", "Rows", "Id > 0");
            builder.CreateIndex("IX_Rows", "Rows", new[] { "Code", "Id" }, unique: true, descending: new[] { true, false });
            builder.RenameColumn("Code", "Rows", "NewCode");
            builder.RenameIndex("IX_Rows", "IX_New", "Rows");
            builder.RenameTable("Rows", newName: "NewRows");
            builder.DropIndex("IX_New", "NewRows");
            builder.DropForeignKey("FK_Rows", "NewRows");
            builder.DropUniqueConstraint("AK_Rows", "NewRows");
            builder.DropPrimaryKey("PK_Rows", "NewRows");
            builder.DropCheckConstraint("CK_Rows", "NewRows");
            builder.DropTable("NewRows");
            var sql = Sql(context, builder.Operations);
            Assert.Contains("PRIMARY KEY", sql);
            Assert.Contains("FOREIGN KEY", sql);
            Assert.Contains("ON DELETE CASCADE", sql);
            Assert.Contains("DESC", sql);
            Assert.Contains("RENAME COLUMN", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "RENAME INDEX" : "ALTER INDEX", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "DROP PRIMARY KEY" : "DROP CONSTRAINT", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "DROP FOREIGN KEY" : "DROP CONSTRAINT", sql);
            Assert.Contains("DROP TABLE", sql);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Seed_data_uses_literals_and_never_generated_value_readback(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var builder = new MigrationBuilder(context.Database.ProviderName!);
            builder.InsertData("Rows", new[] { "Id", "Code" }, new[] { "int", "string" }, new object[,] { { 1, "can't\\break" }, { 2, "two" } });
            // Use real store declarations, not CLR names, when no target model is supplied.
            var insert = (InsertDataOperation)builder.Operations[0];
            insert.ColumnTypes = mode == OceanBaseCompatibilityMode.MySql ? new[] { "INT", "VARCHAR(32)" } : new[] { "NUMBER(10)", "NVARCHAR2(32)" };
            builder.UpdateData("Rows", new[] { "Id" }, new[] { mode == OceanBaseCompatibilityMode.MySql ? "INT" : "NUMBER(10)" }, new object[] { 1 },
                new[] { "Code" }, new[] { mode == OceanBaseCompatibilityMode.MySql ? "VARCHAR(32)" : "NVARCHAR2(32)" }, new object[] { "changed" });
            builder.DeleteData("Rows", "Id", mode == OceanBaseCompatibilityMode.MySql ? "INT" : "NUMBER(10)", 2);
            var sql = Sql(context, builder.Operations);
            Assert.Contains("INSERT INTO", sql);
            Assert.Contains("UPDATE", sql);
            Assert.Contains("DELETE FROM", sql);
            Assert.DoesNotContain("LAST_INSERT_ID", sql);
            Assert.DoesNotContain("RETURNING", sql);
            var commands = context.GetService<IMigrationsSqlGenerator>().Generate(builder.Operations);
            Assert.Equal(4, commands.Count);
            Assert.Equal(2, commands.Count(c => c.CommandText.StartsWith("INSERT", StringComparison.Ordinal)));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Alter_column_preserves_default_and_explicit_default_removal(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var type = mode == OceanBaseCompatibilityMode.MySql ? "VARCHAR(40)" : "NVARCHAR2(40)";
            var operation = new AlterColumnOperation
            {
                Name = "Code", Table = "Rows", ClrType = typeof(string), ColumnType = type, IsNullable = true,
                OldColumn = new AddColumnOperation { ClrType = typeof(string), ColumnType = type, DefaultValue = "old" }
            };
            var sql = Sql(context, new[] { operation });
            Assert.Contains("NULL", sql);
            Assert.DoesNotContain("old", sql);
            if (mode == OceanBaseCompatibilityMode.Oracle) Assert.Contains("DEFAULT NULL", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "CURRENT_TIMESTAMP(6)", null, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "CURRENT_TIMESTAMP(3)", null, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "CURRENT_TIMESTAMP(6)", "current_timestamp(3)", false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "CURRENT_TIMESTAMP(6); SELECT 1", null, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "CURRENT_TIMESTAMP(6)", null, false)]
        public void Alter_column_preserves_only_supported_on_update_clauses(OceanBaseCompatibilityMode mode, string expression,
            string? defaultSql, bool supported)
        {
            using var context = Create(mode);
            var operation = new AlterColumnOperation
            {
                Name = "Updated", Table = "Rows", ClrType = typeof(DateTime), ColumnType = "DATETIME(6)", IsNullable = true, DefaultValueSql = defaultSql,
                OldColumn = new AddColumnOperation { ClrType = typeof(DateTime), ColumnType = "DATETIME(6)", IsNullable = true }
            };
            operation["OceanBase:OnUpdateSql"] = expression;
            if (!supported)
            {
                Assert.Throws<NotSupportedException>(() => Sql(context, new[] { operation }));
                return;
            }
            Assert.Contains("MODIFY COLUMN `Updated` DATETIME(6) NULL ON UPDATE CURRENT_TIMESTAMP(6)", Sql(context, new[] { operation }));
            operation.OldColumn["OceanBase:OnUpdateSql"] = expression;
            operation.RemoveAnnotation("OceanBase:OnUpdateSql");
            Assert.DoesNotContain("ON UPDATE", Sql(context, new[] { operation }));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Requiring_an_update_only_column_does_not_invent_an_insert_default(bool alter)
        {
            using var context = Create(OceanBaseCompatibilityMode.MySql);
            IModel Build(bool includeColumn, bool required)
            {
                var builder = new ModelBuilder(context.GetService<IConventionSetBuilder>().CreateConventionSet());
                var row = builder.Entity("Rows");
                row.Property<int>("Id");
                row.HasKey("Id");
                if (includeColumn)
                    row.Property<DateTime?>("Updated").IsRequired(required).HasColumnType("DATETIME(6)").ValueGeneratedOnUpdate()
                        .HasAnnotation("OceanBase:OnUpdateSql", "CURRENT_TIMESTAMP(6)");
                return context.GetService<IModelRuntimeInitializer>().Initialize(builder.FinalizeModel(), designTime: true);
            }
            var from = Build(alter, required: false);
            var to = Build(true, required: true);
            var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(from.GetRelationalModel(), to.GetRelationalModel());
            var operation = Assert.IsAssignableFrom<ColumnOperation>(Assert.Single(operations));
            Assert.Equal(alter ? typeof(AlterColumnOperation) : typeof(AddColumnOperation), operation.GetType());
            Assert.Equal(DateTime.MinValue, operation.DefaultValue);
            var error = Assert.Throws<NotSupportedException>(() => context.GetService<IMigrationsSqlGenerator>().Generate(operations, to));
            Assert.Contains("backfill", error.Message);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Model_diff_detects_prefix_length_changes_and_rejects_unsafe_unique_replacement(bool unique, bool rename)
        {
            using var context = Create(OceanBaseCompatibilityMode.MySql);
            IModel Build(int length, string name = "IX_Code")
            {
                var builder = new ModelBuilder(context.GetService<IConventionSetBuilder>().CreateConventionSet());
                var row = builder.Entity("Rows");
                row.Property<int>("Id");
                row.HasKey("Id");
                row.Property<string>("Code").HasMaxLength(64);
                row.HasIndex("Code", "Id").HasDatabaseName(name).IsUnique(unique).IsDescending(true, false)
                    .HasAnnotation("OceanBase:IndexPrefixLengths", new[] { length, 0 });
                return context.GetService<IModelRuntimeInitializer>().Initialize(builder.FinalizeModel(), designTime: true);
            }
            var from = Build(12);
            var to = Build(9, rename ? "IX_NewCode" : "IX_Code");
            var differ = context.GetService<IMigrationsModelDiffer>();
            Assert.Empty(differ.GetDifferences(from.GetRelationalModel(), Build(12).GetRelationalModel()));
            var operations = differ.GetDifferences(from.GetRelationalModel(), to.GetRelationalModel());
            Assert.Collection(operations, operation => Assert.IsType<DropIndexOperation>(operation), operation => Assert.IsType<CreateIndexOperation>(operation));
            if (unique)
                Assert.Contains("unique prefix indexes", Assert.Throws<NotSupportedException>(() => Sql(context, operations)).Message);
            else
                Assert.Contains("(`Code`(9) DESC, `Id`)", Sql(context, operations));
            if (rename)
                Assert.Contains("CREATE UNIQUE INDEX `IX_NewCode`", Sql(context, operations.Reverse().ToArray()));
            using var script = new DbContext(new DbContextOptionsBuilder().UseOceanBase("Server=offline;Database=app", OceanBaseCompatibilityMode.MySql)
                .UseModel(to).Options);
            Assert.Contains("(`Code`(9) DESC, `Id`)", script.Database.GenerateCreateScript());
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Computed_columns_and_hostile_identifiers_are_quoted(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var operation = new AddColumnOperation
            {
                Name = "x`\"", Table = "t`\"", ClrType = typeof(int), ComputedColumnSql = "1 + 2", IsNullable = false
            };
            var sql = Sql(context, new[] { operation });
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "`x``\"`" : "\"x`\"\"\"", sql);
            Assert.Contains("GENERATED ALWAYS AS (1 + 2) VIRTUAL", sql);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Unsupported_operations_and_cross_scope_targets_reject_explicitly(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode, scoped: true);
            foreach (var operation in new MigrationOperation[]
            {
                new CreateSequenceOperation { Name = "Sequence" },
                new DropSchemaOperation { Name = "app" },
                new EnsureSchemaOperation { Name = "other" },
                new DropTableOperation { Name = "Rows", Schema = "other" },
                new DropColumnOperation { Name = "Code", Table = "Rows", Schema = "other" },
                new AddForeignKeyOperation { Table = "Rows", Name = "FK", Columns = new[] { "Id" }, PrincipalTable = "Other", PrincipalSchema = "other", PrincipalColumns = new[] { "Id" } },
                new CreateIndexOperation { Table = "Rows", Name = "IX", Columns = new[] { "Id" }, Filter = "Id > 0" },
                new RenameTableOperation { Name = "Rows", NewName = "Rows", NewSchema = "other" },
                new AlterColumnOperation { Table = "Rows", Name = "Id", ClrType = typeof(int), ComputedColumnSql = "2" }
            }) Assert.Throws<NotSupportedException>(() => Sql(context, new[] { operation }));
            Assert.Empty(context.GetService<IMigrationsSqlGenerator>().Generate(new[] { new EnsureSchemaOperation { Name = "app" } }));
        }

        [Fact]
        public void Oracle_restrict_foreign_keys_omit_the_clause_and_update_actions_reject()
        {
            using var context = Create(OceanBaseCompatibilityMode.Oracle);
            var operation = new AddForeignKeyOperation
            {
                Name = "FK", Table = "Rows", Columns = new[] { "ParentId" }, PrincipalTable = "Parents",
                PrincipalColumns = new[] { "Id" }, OnDelete = ReferentialAction.Restrict
            };
            Assert.DoesNotContain("ON DELETE", Sql(context, new[] { operation }));
            operation.OnUpdate = ReferentialAction.Cascade;
            Assert.Throws<NotSupportedException>(() => Sql(context, new[] { operation }));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Seed_data_cannot_use_a_model_default_schema_outside_the_selected_scope(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode, scoped: true);
            var builder = new ModelBuilder(context.GetService<IConventionSetBuilder>().CreateConventionSet());
            builder.HasDefaultSchema("other");
            var model = context.GetService<IModelRuntimeInitializer>().Initialize(builder.FinalizeModel(), designTime: true);
            var operation = new InsertDataOperation
            {
                Table = "Rows", Columns = new[] { "Id" },
                ColumnTypes = new[] { mode == OceanBaseCompatibilityMode.MySql ? "INT" : "NUMBER(10)" },
                Values = new object[,] { { 1 } }
            };
            Assert.Throws<NotSupportedException>(() => context.GetService<IMigrationsSqlGenerator>().Generate(new[] { operation }, model));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Tpc_model_differences_reject_independent_identity_counters(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var builder = new ModelBuilder(context.GetService<IConventionSetBuilder>().CreateConventionSet());
            builder.Entity<Animal>().UseTpcMappingStrategy();
            builder.Entity<Animal>().Property(a => a.Id).ValueGeneratedOnAdd();
            builder.Entity<Cat>().ToTable("Cats");
            builder.Entity<Dog>().ToTable("Dogs");
            var model = context.GetService<IModelRuntimeInitializer>().Initialize(builder.FinalizeModel(), designTime: true);
            var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
            Assert.Throws<NotSupportedException>(() => context.GetService<IMigrationsSqlGenerator>().Generate(operations, model));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Unbacked_generated_properties_reject_instead_of_creating_unwritable_columns(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var builder = new ModelBuilder(context.GetService<IConventionSetBuilder>().CreateConventionSet());
            builder.Entity<Row>().Property<DateTime>("CreatedAt").ValueGeneratedOnAdd();
            var model = context.GetService<IModelRuntimeInitializer>().Initialize(builder.FinalizeModel(), designTime: true);
            var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
            Assert.Throws<NotSupportedException>(() => context.GetService<IMigrationsSqlGenerator>().Generate(operations, model));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void History_names_preserve_quoted_text_and_reject_cross_scope_configuration(OceanBaseCompatibilityMode mode)
        {
            var name = "CREATE TABLE Audit`\"";
            var options = new DbContextOptionsBuilder().UseOceanBase("Server=localhost;Database=app", mode)
                .EnableOceanBaseSchemaManagement("app");
            var extension = options.Options.FindExtension<OceanBaseOptionsExtension>()!
                .WithMigrationsHistoryTableName(name).WithMigrationsHistoryTableSchema("app");
            ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(extension);
            using (var context = new DbContext(options.Options))
            {
                var history = context.GetService<IHistoryRepository>();
                var sql = history.GetCreateIfNotExistsScript();
                var quoted = mode == OceanBaseCompatibilityMode.MySql ? "`CREATE TABLE Audit``\"`" : "\"CREATE TABLE Audit`\"\"\"";
                Assert.Contains(quoted, sql);
                Assert.Contains(quoted, history.GetInsertScript(new HistoryRow("id'\\", "10.0.0")));
                if (mode == OceanBaseCompatibilityMode.MySql) Assert.Contains("CREATE TABLE IF NOT EXISTS", sql);
                else
                {
                    Assert.Contains("SELECT COUNT(*) INTO ob_count FROM ALL_TABLES WHERE OWNER = N'app'", sql);
                    Assert.Contains("IF ob_count = 0 THEN", sql);
                }
            }
            ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(extension.WithMigrationsHistoryTableSchema("other"));
            using var other = new DbContext(options.Options);
            Assert.Throws<NotSupportedException>(() => other.GetService<IHistoryRepository>().GetCreateIfNotExistsScript());
        }

        [Fact]
        public void Mysql_scripted_raw_sql_gets_a_statement_separator_without_changing_runtime_text()
        {
            using var context = Create(OceanBaseCompatibilityMode.MySql);
            var generator = context.GetService<IMigrationsSqlGenerator>();
            var operations = new[] { new SqlOperation { Sql = "UPDATE Rows SET Code = 'two'" } };
            Assert.Equal(operations[0].Sql, generator.Generate(operations)[0].CommandText.Trim());
            Assert.EndsWith(";", generator.Generate(operations, options: MigrationsSqlGenerationOptions.Script)[0].CommandText.Trim());
            foreach (var comment in new[] { " -- explanation", " -- explanation;" })
            {
                operations[0].Sql = "UPDATE Rows SET Code = 'two'" + comment;
                var script = generator.Generate(operations, options: MigrationsSqlGenerationOptions.Script)[0].CommandText;
                Assert.Contains(comment + "\n;", script);
            }
        }

        [Fact]
        public void Mysql_text_and_blob_literal_defaults_use_expressions()
        {
            using var context = Create(OceanBaseCompatibilityMode.MySql);
            var operations = new MigrationOperation[]
            {
                new AddColumnOperation { Name = "Description", Table = "Rows", ClrType = typeof(string), DefaultValue = "text" },
                new AddColumnOperation { Name = "Payload", Table = "Rows", ClrType = typeof(byte[]), DefaultValue = new byte[] { 1, 2 } }
            };
            var sql = Sql(context, operations);
            Assert.Contains("LONGTEXT DEFAULT ('text')", sql);
            Assert.Contains("LONGBLOB DEFAULT (", sql);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Foreign_provider_annotations_reject_instead_of_silently_ignoring_schema_semantics(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var operation = new CreateIndexOperation { Name = "IX", Table = "Rows", Columns = new[] { "Id" } };
            operation.AddAnnotation("SqlServer:Clustered", true);
            Assert.Throws<NotSupportedException>(() => Sql(context, new[] { operation }));
        }

        private static DbContext Create(OceanBaseCompatibilityMode mode, bool scoped = false)
        {
            var options = new DbContextOptionsBuilder().UseOceanBase("Server=localhost;Database=app;User ID=test", mode);
            if (scoped) options.EnableOceanBaseSchemaManagement("app");
            return new DbContext(options.Options);
        }

        private static string Sql(DbContext context, IReadOnlyList<MigrationOperation> operations)
            => string.Concat(context.GetService<IMigrationsSqlGenerator>().Generate(operations).Select(c => c.CommandText));

        [Theory]
        [MemberData(nameof(Modes))]
        public void Model_diffs_create_change_and_clear_table_and_column_comments(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var initial = Model(context, evolved: false, comment: "title's text");
            var updated = Model(context, evolved: false, comment: "new comment");
            var bare = Model(context, evolved: false);
            var differ = context.GetService<IMigrationsModelDiffer>();
            var generator = context.GetService<IMigrationsSqlGenerator>();
            var create = generator.Generate(differ.GetDifferences(null, initial.GetRelationalModel()), initial);
            var sql = string.Join("\n", create.Select(command => command.CommandText));
            Assert.Contains("title''s text", sql);
            Assert.Contains("base title''s text", sql);
            Assert.All(create, command => Assert.True(command.TransactionSuppressed));
            if (mode == OceanBaseCompatibilityMode.Oracle)
            {
                Assert.Contains("COMMENT ON TABLE \"Rows\" IS 'title''s text'", sql);
                Assert.Contains("COMMENT ON COLUMN \"Rows\".\"Code\" IS 'base title''s text'", sql);
            }
            else
            {
                Assert.Contains(" COMMENT = 'title''s text'", sql);
                Assert.Contains(" COMMENT 'base title''s text'", sql);
            }

            var changeOps = differ.GetDifferences(initial.GetRelationalModel(), updated.GetRelationalModel());
            var change = string.Join("\n", generator.Generate(changeOps, updated, MigrationsSqlGenerationOptions.Script)
                .Select(command => command.CommandText));
            Assert.Contains("new comment", change);
            Assert.Contains("base new comment", change);
            if (mode == OceanBaseCompatibilityMode.Oracle) Assert.DoesNotContain("MODIFY", change);

            var clear = string.Join("\n", generator.Generate(differ.GetDifferences(updated.GetRelationalModel(), bare.GetRelationalModel()), bare)
                .Select(command => command.CommandText));
            Assert.Contains("''", clear);
            Assert.DoesNotContain("new comment", clear);
            var rollback = string.Join("\n", generator.Generate(differ.GetDifferences(bare.GetRelationalModel(), initial.GetRelationalModel()), initial)
                .Select(command => command.CommandText));
            Assert.Contains("base title''s text", rollback);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Schema_comment_literals_reject_unrepresentable_inputs(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var operation = new AlterTableOperation { Name = "Rows", Comment = "invalid\0comment" };
            Assert.Throws<NotSupportedException>(() => Sql(context, [operation]));
            operation.Comment = "one\\two";
            if (mode == OceanBaseCompatibilityMode.MySql)
                Assert.Throws<NotSupportedException>(() => Sql(context, [operation]));
            else
                Assert.Contains("'one\\two'", Sql(context, [operation]));
        }

        private static IModel Model(DbContext context, bool evolved, string? comment = null, string? collation = null,
            bool assignedKey = false, bool compositeKey = false)
        {
            var builder = new ModelBuilder(context.GetService<IConventionSetBuilder>().CreateConventionSet());
            if (collation is not null) builder.UseCollation(collation);
            var row = builder.Entity<Row>();
            row.ToTable("Rows");
            if (assignedKey) row.Property(entity => entity.Id).ValueGeneratedNever();
            if (compositeKey) row.HasKey(entity => new { entity.Id, entity.Code });
            row.Property(r => r.Code).HasMaxLength(evolved ? 64 : 32).HasDefaultValue("can't");
            if (comment is not null)
            {
                row.ToTable(table => table.HasComment(comment));
                row.Property(r => r.Code).HasComment("base " + comment);
            }
            if (evolved) row.Property<int?>("Revision");
            return context.GetService<IModelRuntimeInitializer>().Initialize(builder.FinalizeModel(), designTime: true);
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public string Code { get; set; } = "";
        }
        private abstract class Animal { public int Id { get; set; } }
        private sealed class Cat : Animal { public string Name { get; set; } = ""; }
        private sealed class Dog : Animal { public string Name { get; set; } = ""; }
    }
}
