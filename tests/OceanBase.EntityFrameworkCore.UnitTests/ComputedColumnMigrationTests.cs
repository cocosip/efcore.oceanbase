using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class ComputedColumnMigrationTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MySql_virtual_nullability_model_diffs_preserve_expression_type_and_comments(bool changeComment)
        {
            using var context = Create(OceanBaseCompatibilityMode.MySql);
            var nullable = Model(context, required: false, "original");
            var required = Model(context, required: true, changeComment ? "changed" : "original");
            var differ = context.GetService<IMigrationsModelDiffer>();
            var generator = context.GetService<IMigrationsSqlGenerator>();
            var upgrade = differ.GetDifferences(nullable.GetRelationalModel(), required.GetRelationalModel());
            var alter = Assert.IsType<AlterColumnOperation>(Assert.Single(upgrade));
            Assert.Null(alter.OldColumn.DefaultValue);
            Assert.Equal(0, alter.DefaultValue);
            var up = Assert.Single(generator.Generate(upgrade, required));
            var down = Assert.Single(generator.Generate(differ.GetDifferences(required.GetRelationalModel(), nullable.GetRelationalModel()), nullable));
            Assert.Equal("ALTER TABLE `Rows` MODIFY COLUMN `Computed` INT GENERATED ALWAYS AS (BaseValue + 1) VIRTUAL NOT NULL COMMENT '"
                + (changeComment ? "changed" : "original") + "';", up.CommandText.Trim());
            Assert.Equal("ALTER TABLE `Rows` MODIFY COLUMN `Computed` INT GENERATED ALWAYS AS (BaseValue + 1) VIRTUAL COMMENT 'original';",
                down.CommandText.Trim());
            Assert.True(up.TransactionSuppressed);
            Assert.True(down.TransactionSuppressed);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "expression")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "type")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "storage")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "stored")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "ordinary")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "identity")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "default")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "zero-default")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "nullable")]
        public void Nullability_change_does_not_allow_other_computed_definition_changes(OceanBaseCompatibilityMode mode, string change)
        {
            using var context = Create(mode);
            var operation = new AlterColumnOperation
            {
                Table = "Rows", Name = "Computed", ClrType = typeof(int), ColumnType = "INT",
                ComputedColumnSql = "BaseValue + 1", IsStored = false, IsNullable = false,
                OldColumn = new AddColumnOperation
                {
                    ClrType = typeof(int), ColumnType = "INT", ComputedColumnSql = "BaseValue + 1", IsStored = false, IsNullable = true
                }
            };
            switch (change)
            {
                case "expression": operation.ComputedColumnSql = "BaseValue + 2"; break;
                case "type": operation.ColumnType = "BIGINT"; break;
                case "storage": operation.IsStored = true; break;
                case "stored": operation.IsStored = operation.OldColumn.IsStored = true; break;
                case "ordinary": operation.ComputedColumnSql = null; break;
                case "identity": operation["OceanBase:Identity"] = true; break;
                case "default": operation.DefaultValue = 7; break;
                case "zero-default": operation.DefaultValue = 0; break;
            }
            Assert.Throws<NotSupportedException>(() => context.GetService<IMigrationsSqlGenerator>().Generate([operation]));
        }

        [Theory]
        [InlineData(typeof(string), "VARCHAR(50)")]
        [InlineData(typeof(byte[]), "VARBINARY(50)")]
        public void MySql_reference_type_computed_columns_ignore_only_synthetic_backfill_defaults(Type type, string storeType)
        {
            using var context = Create(OceanBaseCompatibilityMode.MySql);
            var nullable = Model(context, required: false, "original", type);
            var required = Model(context, required: true, "original", type);
            var differ = context.GetService<IMigrationsModelDiffer>();
            var generator = context.GetService<IMigrationsSqlGenerator>();
            var upgrade = differ.GetDifferences(nullable.GetRelationalModel(), required.GetRelationalModel());
            var alter = Assert.IsType<AlterColumnOperation>(Assert.Single(upgrade));
            Assert.Null(alter.OldColumn.DefaultValue);
            if (type == typeof(string)) Assert.Equal(string.Empty, alter.DefaultValue);
            else Assert.Empty(Assert.IsType<byte[]>(alter.DefaultValue));
            Assert.Equal($"ALTER TABLE `Rows` MODIFY COLUMN `Computed` {storeType} GENERATED ALWAYS AS (BaseValue) VIRTUAL NOT NULL COMMENT 'original';",
                Assert.Single(generator.Generate(upgrade, required)).CommandText.Trim());
            Assert.Equal($"ALTER TABLE `Rows` MODIFY COLUMN `Computed` {storeType} GENERATED ALWAYS AS (BaseValue) VIRTUAL COMMENT 'original';",
                Assert.Single(generator.Generate(differ.GetDifferences(required.GetRelationalModel(), nullable.GetRelationalModel()), nullable)).CommandText.Trim());
            Assert.Throws<NotSupportedException>(() => generator.Generate(upgrade));
            alter.DefaultValue = type == typeof(string) ? "explicit" : new byte[] { 1 };
            Assert.Throws<NotSupportedException>(() => generator.Generate(upgrade, required));
        }

        private static DbContext Create(OceanBaseCompatibilityMode mode)
            => new(new DbContextOptionsBuilder().UseOceanBase("Server=offline;Database=app", mode).EnableServiceProviderCaching(false).Options);

        private static IModel Model(DbContext context, bool required, string comment, Type? computedType = null)
        {
            var builder = new ModelBuilder(context.GetService<IConventionSetBuilder>().CreateConventionSet());
            var row = builder.Entity("Rows");
            row.Property<int>("Id");
            row.HasKey("Id");
            if (computedType is null)
            {
                row.Property<int?>("BaseValue");
                row.Property<int?>("Computed").IsRequired(required).HasComputedColumnSql("BaseValue + 1", stored: false).HasComment(comment);
            }
            else
            {
                row.Property(computedType, "BaseValue").HasMaxLength(50);
                row.Property(computedType, "Computed").HasMaxLength(50).IsRequired(required)
                    .HasComputedColumnSql("BaseValue", stored: false).HasComment(comment);
            }
            return context.GetService<IModelRuntimeInitializer>().Initialize(builder.FinalizeModel(), designTime: true);
        }
    }
}
