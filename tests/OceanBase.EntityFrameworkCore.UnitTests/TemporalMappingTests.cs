using System;
using System.Data;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBase.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class TemporalMappingTests
    {
        public static TheoryData<OceanBaseCompatibilityMode, Type, string> TemporalStorageCases => new()
        {
            { OceanBaseCompatibilityMode.MySql, typeof(DateTime), "DATETIME(3)" },
            { OceanBaseCompatibilityMode.MySql, typeof(DateTimeOffset), "DATETIME(3)" },
            { OceanBaseCompatibilityMode.MySql, typeof(DateOnly), "DATE" },
            { OceanBaseCompatibilityMode.MySql, typeof(TimeOnly), "TIME(3)" },
            { OceanBaseCompatibilityMode.MySql, typeof(TimeSpan), "TIME(3)" },
            { OceanBaseCompatibilityMode.Oracle, typeof(DateTime), "TIMESTAMP(3)" },
            { OceanBaseCompatibilityMode.Oracle, typeof(DateTimeOffset), "TIMESTAMP(3) WITH TIME ZONE" },
            { OceanBaseCompatibilityMode.Oracle, typeof(DateOnly), "DATE" },
            { OceanBaseCompatibilityMode.Oracle, typeof(TimeOnly), "INTERVAL DAY(1) TO SECOND(3)" },
            { OceanBaseCompatibilityMode.Oracle, typeof(TimeSpan), "INTERVAL DAY(9) TO SECOND(3)" }
        };

        [Theory]
        [MemberData(nameof(TemporalStorageCases))]
        public void Temporal_resolution_rejects_length_and_fixed_facets(OceanBaseCompatibilityMode mode, Type type, string storeType)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            Assert.Throws<ArgumentException>(() => source.FindMapping(type, null, size: 4));
            Assert.Throws<ArgumentException>(() => source.FindMapping(type, null, fixedLength: true));
            Assert.Throws<ArgumentException>(() => source.FindMapping(type, storeType, size: 4));
            Assert.Throws<ArgumentException>(() => source.FindMapping(type, storeType, fixedLength: true));
            Assert.NotNull(source.FindMapping(type, storeType, fixedLength: false));
        }

        [Theory]
        [MemberData(nameof(TemporalStorageCases))]
        public void Temporal_property_length_rejects_even_when_equal_to_parsed_precision(
            OceanBaseCompatibilityMode mode, Type type, string storeType)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var property = new ModelBuilder().Entity("TemporalFacet").Property(type, "Value")
                .HasColumnType(storeType).HasMaxLength(3).Metadata;
            Assert.Throws<ArgumentException>(() => source.FindMapping((IProperty)property));
            property.SetMaxLength(null);
            property.SetIsFixedLength(true);
            Assert.Throws<ArgumentException>(() => source.FindMapping((IProperty)property));
            property.SetIsFixedLength(false);
            Assert.NotNull(source.FindMapping((IProperty)property));
        }

        [Theory]
        [MemberData(nameof(TemporalStorageCases))]
        public void Temporal_clones_reject_length_and_fixed_facets_without_breaking_unchanged_clones(
            OceanBaseCompatibilityMode mode, Type type, string storeType)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(type, storeType)!;
            Assert.Throws<ArgumentException>(() => mapping.WithStoreTypeAndSize(storeType, 4));
            Assert.Throws<ArgumentException>(() => mapping.Clone(new RelationalTypeMappingInfo(type, fixedLength: true)));
            var clone = mapping.WithStoreTypeAndSize(storeType, null);
            Assert.Equal(storeType, clone.StoreType);
            Assert.Equal(mapping.Precision, clone.Precision);
            Assert.Equal(mapping.DbType, clone.DbType);
            using var command = new OceanBaseCommand();
            var output = clone.CreateParameter(command, "output", null, direction: ParameterDirection.Output);
            Assert.Equal(mapping.Precision, output.Scale);
            Assert.Equal(mapping.CreateParameter(command, "original", null, direction: ParameterDirection.Output).Precision,
                output.Precision);
            Assert.Equal(mapping.Converter, clone.Converter);
        }

        [Theory]
        [MemberData(nameof(TemporalStorageCases))]
        public void Temporal_clones_cannot_replace_the_existing_transport_DbType(
            OceanBaseCompatibilityMode mode, Type type, string storeType)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(type, storeType)!;
            Assert.Throws<ArgumentException>(() => mapping.Clone(
                new RelationalTypeMappingInfo(type, storeTypeName: storeType, dbType: DbType.String)));
            var clone = mapping.Clone(new RelationalTypeMappingInfo(type, storeTypeName: storeType, dbType: mapping.DbType));
            Assert.Equal(storeType, clone.StoreType);
            Assert.Equal(mapping.DbType, clone.DbType);
            Assert.Equal(mapping.Converter, clone.Converter);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(DateTime), "DATETIME(3)", "DATE", DbType.Date, 0)]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(TimeSpan), "TIME(3)", "DATETIME(3)", DbType.DateTime2, 3)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateTime), "TIMESTAMP(3)", "DATE", DbType.Date, 0)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateTime), "DATE", "TIMESTAMP(3)", DbType.DateTime2, 3)]
        public void Temporal_clones_cannot_change_family_by_also_replacing_DbType(
            OceanBaseCompatibilityMode mode, Type type, string storeType, string invalid, DbType dbType, int precision)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(type, storeType)!;
            Assert.Throws<ArgumentException>(() => mapping.Clone(
                new RelationalTypeMappingInfo(type, storeTypeName: invalid, precision: precision, dbType: dbType)));
        }

        [Theory]
        [InlineData(typeof(DateTimeOffset), "TIMESTAMP(3) WITH TIME ZONE")]
        [InlineData(typeof(TimeSpan), "INTERVAL DAY(9) TO SECOND(3)")]
        public void Temporal_clones_reject_postfixes_that_rewrite_the_validated_declaration(Type type, string storeType)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(type, storeType)!;
            var ignoredPostfix = mapping.Clone(storeTypePostfix: StoreTypePostfix.Precision);
            Assert.Equal(storeType, ignoredPostfix.StoreType);
            Assert.Equal(mapping.DbType, ignoredPostfix.DbType);
            Assert.Throws<ArgumentException>(() => mapping.Clone(
                new RelationalTypeMappingInfo(type), storeTypePostfix: StoreTypePostfix.Precision));
            var clone = mapping.Clone(new RelationalTypeMappingInfo(type), storeTypePostfix: StoreTypePostfix.None);
            Assert.Equal(storeType, clone.StoreType);
            Assert.Equal(mapping.DbType, clone.DbType);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(DateTime), "DATETIME(3)", "VARCHAR(3)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(DateTime), "DATETIME(3)", "TIMESTAMP(3)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(DateTime), "DATETIME(3)", "DATE")]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(DateTimeOffset), "DATETIME(3)", "TIMESTAMP(3) WITH TIME ZONE")]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(DateOnly), "DATE", "DATETIME(0)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(TimeOnly), "TIME(3)", "DATETIME(3)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(TimeSpan), "TIME(3)", "INTERVAL DAY(9) TO SECOND(3)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateTime), "TIMESTAMP(3)", "DATETIME(3)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateTime), "TIMESTAMP(3)", "DATE")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateTime), "DATE", "TIMESTAMP(0)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateOnly), "DATE", "TIMESTAMP(0)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateTimeOffset), "TIMESTAMP(3) WITH TIME ZONE", "TIMESTAMP(3)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(TimeOnly), "INTERVAL DAY(1) TO SECOND(3)", "TIME(3)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(TimeSpan), "INTERVAL DAY(9) TO SECOND(3)", "NUMBER")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateTime), "TIMESTAMP(3)", "TIMESTAMP(3) junk")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(TimeSpan), "INTERVAL DAY(9) TO SECOND(3)", "INTERVAL DAY(9) TO SECOND(3) junk")]
        public void Temporal_clones_reject_incompatible_transport_families_and_suffixes(
            OceanBaseCompatibilityMode mode, Type type, string storeType, string invalid)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(type, storeType)!;
            Assert.Throws<NotSupportedException>(() => mapping.WithStoreTypeAndSize(invalid, null));
            Assert.Throws<NotSupportedException>(() => mapping.Clone(
                new RelationalTypeMappingInfo(type, storeTypeName: invalid, precision: mapping.Precision == 0 ? 0 : 2)));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(DateTime), -1)]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(DateTime), 7)]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(DateOnly), 1)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateTime), -1)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateTime), 10)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateOnly), 1)]
        public void Temporal_precision_clones_reject_invalid_ranges(OceanBaseCompatibilityMode mode, Type type, int precision)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(type)!;
            Assert.Throws<ArgumentOutOfRangeException>(() => mapping.WithPrecisionAndScale(precision, null));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(DateTime), "DATETIME(3)", "DATETIME(4)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(TimeSpan), "TIME(3)", "TIME(4)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(DateTime), "DATETIME(3)", "DATETIME(7)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateTime), "TIMESTAMP(3)", "TIMESTAMP(4)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateTimeOffset), "TIMESTAMP(3) WITH TIME ZONE", "TIMESTAMP(4) WITH TIME ZONE")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(TimeOnly), "INTERVAL DAY(1) TO SECOND(3)", "INTERVAL DAY(2) TO SECOND(3)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(TimeSpan), "INTERVAL DAY(9) TO SECOND(3)", "INTERVAL DAY(9) TO SECOND(4)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(TimeSpan), "INTERVAL DAY(9) TO SECOND(3)", "INTERVAL DAY(10) TO SECOND(3)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateTime), "TIMESTAMP(3)", "TIMESTAMP(999999999999)")]
        public void Temporal_clones_reject_conflicting_fractional_and_leading_precision(
            OceanBaseCompatibilityMode mode, Type type, string storeType, string invalid)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(type, storeType)!;
            Assert.ThrowsAny<ArgumentException>(() => mapping.WithStoreTypeAndSize(invalid, null));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "DATETIME(3)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "TIMESTAMP(3)")]
        public void Temporal_declaration_clones_keep_converter_and_require_matching_explicit_precision(
            OceanBaseCompatibilityMode mode, string storeType)
        {
            using var context = CreateContext(mode);
            var mapping = (RelationalTypeMapping)context.GetService<IRelationalTypeMappingSource>()
                .FindMapping(typeof(DateTime), storeType)!.WithComposedConverter(
                    new ValueConverter<Stamp, DateTime>(stamp => stamp.Value, value => new Stamp(value)));
            var declaration = mode == OceanBaseCompatibilityMode.MySql ? "datetime(2)" : "timestamp(2)";
            var clone = mapping.Clone(new RelationalTypeMappingInfo(typeof(Stamp), storeTypeName: declaration, precision: 2));
            Assert.Equal(declaration, clone.StoreType);
            Assert.Equal(2, clone.Precision);
            Assert.Same(mapping.Converter, clone.Converter);
            using var command = new OceanBaseCommand();
            var value = new DateTime(2026, 11, 10).AddTicks(10);
            Assert.Equal(value, clone.CreateParameter(command, "value", new Stamp(value)).Value);
            Assert.Equal((byte)2, clone.CreateParameter(command, "output", null, direction: ParameterDirection.Output).Scale);
            Assert.Throws<ArgumentException>(() => ((IOceanBaseWriteValidation)clone).ValidateWriteValue(new Stamp(value)));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(DateTime), "DATETIME(6)", 6)]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(DateTimeOffset), "DATETIME(6)", 6)]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(DateOnly), "DATE", 0)]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(TimeOnly), "TIME(6)", 6)]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(TimeSpan), "TIME(6)", 6)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateTime), "TIMESTAMP(7)", 7)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateTimeOffset), "TIMESTAMP(7) WITH TIME ZONE", 7)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateOnly), "DATE", 0)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(TimeOnly), "INTERVAL DAY(1) TO SECOND(7)", 7)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(TimeSpan), "INTERVAL DAY(9) TO SECOND(7)", 7)]
        public void Default_temporal_mappings_configure_fractional_scale(
            OceanBaseCompatibilityMode mode, Type type, string storeType, int precision)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(type)!;
            using var command = new OceanBaseCommand();

            Assert.Equal(storeType, mapping.StoreType);
            Assert.Equal(precision, mapping.Precision);
            var parameter = mapping.CreateParameter(command, "value", null, nullable: true);
            Assert.Equal(DBNull.Value, parameter.Value);
            Assert.Equal(precision, parameter.Scale);
            if (mode == OceanBaseCompatibilityMode.Oracle && (type == typeof(TimeOnly) || type == typeof(TimeSpan)))
            {
                Assert.Equal(OceanBaseDbType.IntervalDayToSecond, ((OceanBaseParameter)parameter).OceanBaseDbType);
                Assert.Equal((byte)9, parameter.Precision);
                Assert.Equal(type == typeof(TimeOnly) ? 1 : 9,
                    mapping.CreateParameter(command, "output", null, direction: ParameterDirection.Output).Precision);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void DateOnly_binds_midnight_and_reads_connector_DateTime(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(DateOnly))!;
            using var command = new OceanBaseCommand();
            var date = new DateOnly(2024, 2, 29);
            var midnight = new DateTime(2024, 2, 29);

            var parameter = mapping.CreateParameter(command, "date", date);

            Assert.Equal(DbType.Date, parameter.DbType);
            Assert.Equal(midnight, parameter.Value);
            Assert.Equal(date, Read(mapping, midnight));
            Assert.Throws<InvalidCastException>(() => Read(mapping, midnight.AddHours(1)));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "TIMESTAMP '2026-11-10 12:34:56.123456'")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "TIMESTAMP '2026-11-10 12:34:56.1234560'")]
        public void DateTime_parameters_and_literals_enforce_unzoned_exact_values(OceanBaseCompatibilityMode mode, string literal)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(DateTime))!;
            using var command = new OceanBaseCommand();
            var date = new DateTime(2026, 11, 10, 12, 34, 56).AddTicks(1234560);

            Assert.Equal(date, mapping.CreateParameter(command, "date", date).Value);
            Assert.Equal(literal, mapping.GenerateSqlLiteral(date));
            Assert.Equal(date, Read(mapping, mode == OceanBaseCompatibilityMode.Oracle ? new OceanBaseTimestamp(date) : date));
            Assert.Throws<ArgumentException>(() => mapping.CreateParameter(command, "date", DateTime.SpecifyKind(date, DateTimeKind.Utc)));
            Assert.Throws<ArgumentException>(() => mapping.GenerateSqlLiteral(DateTime.SpecifyKind(date, DateTimeKind.Local)));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void DateTimeOffset_has_explicit_mode_specific_offset_semantics(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(DateTimeOffset))!;
            using var command = new OceanBaseCommand();
            var date = new DateTimeOffset(2026, 11, 10, 12, 34, 56, TimeSpan.FromMinutes(-330));
            var parameter = mapping.CreateParameter(command, "date", date);

            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Equal(DbType.DateTime2, parameter.DbType);
                Assert.Equal(new DateTime(2026, 11, 10, 18, 4, 56), parameter.Value);
                Assert.Equal(DateTimeKind.Unspecified, Assert.IsType<DateTime>(parameter.Value).Kind);
                var read = Assert.IsType<DateTimeOffset>(Read(mapping, new DateTime(2026, 11, 10, 18, 4, 56)));
                Assert.Equal(TimeSpan.Zero, read.Offset);
                Assert.Equal(date.UtcTicks, read.UtcTicks);
            }
            else
            {
                Assert.Equal(DbType.DateTimeOffset, parameter.DbType);
                Assert.Equal(date, parameter.Value);
                var read = Assert.IsType<DateTimeOffset>(Read(mapping, new OceanBaseTimestampWithTimeZone(date)));
                Assert.Equal(date.Offset, read.Offset);
                Assert.Equal(date.UtcTicks, read.UtcTicks);
                Assert.Equal("TO_TIMESTAMP_TZ('2026-11-10 12:34:56.0000000 -05:30', 'YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM')",
                    mapping.GenerateSqlLiteral(date));
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void TimeOnly_uses_duration_transport_with_a_clock_range(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(TimeOnly))!;
            using var command = new OceanBaseCommand();
            var time = new TimeOnly(12, 34, 56);
            var parameter = mapping.CreateParameter(command, "clock", time);
            object providerValue = mode == OceanBaseCompatibilityMode.Oracle
                ? new OceanBaseIntervalDayToSecond(false, 0, 12, 34, 56, leadingPrecision: 1, scale: 7)
                : new TimeSpan(12, 34, 56);

            Assert.Equal(time, Read(mapping, providerValue));
            Assert.Equal(time.ToTimeSpan(), parameter.Value is OceanBaseIntervalDayToSecond interval ? interval.ToTimeSpan() : parameter.Value);
            object invalid = mode == OceanBaseCompatibilityMode.Oracle
                ? new OceanBaseIntervalDayToSecond(true, 0, 0, 0, 1, leadingPrecision: 1, scale: 7)
                : TimeSpan.FromSeconds(-1);
            Assert.Throws<ArgumentOutOfRangeException>(() => Read(mapping, invalid));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "TIME '-51:04:05.123456'")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "INTERVAL '-000000002 03:04:05.1234560' DAY(9) TO SECOND(7)")]
        public void TimeSpan_retains_signed_multi_day_duration(OceanBaseCompatibilityMode mode, string literal)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(TimeSpan))!;
            using var command = new OceanBaseCommand();
            var duration = -new TimeSpan(2, 3, 4, 5).Add(TimeSpan.FromTicks(1234560));
            var parameter = mapping.CreateParameter(command, "duration", duration);
            object providerValue = mode == OceanBaseCompatibilityMode.Oracle
                ? new OceanBaseIntervalDayToSecond(true, 2, 3, 4, 5, 123456000, 9, 7)
                : duration;

            Assert.Equal(duration, Read(mapping, providerValue));
            Assert.Equal(duration, parameter.Value is OceanBaseIntervalDayToSecond interval ? interval.ToTimeSpan() : parameter.Value);
            Assert.Equal(literal, mapping.GenerateSqlLiteral(duration));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "DATETIME(3)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "TIMESTAMP(3)")]
        public void Explicit_precision_survives_clone_and_rejects_truncation(OceanBaseCompatibilityMode mode, string storeType)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(DateTime), storeType)!
                .WithPrecisionAndScale(3, null);
            using var command = new OceanBaseCommand();
            var date = new DateTime(2026, 11, 10);

            Assert.Equal(storeType, mapping.StoreType);
            Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? 6 : 7, mapping.CreateParameter(command, "date", date).Scale);
            Assert.Equal(date.AddTicks(10), mapping.CreateParameter(command, "date", date.AddTicks(10)).Value);
            Assert.NotEmpty(mapping.GenerateSqlLiteral(date.AddTicks(10)));
            Assert.Throws<ArgumentException>(() => Assert.IsAssignableFrom<IOceanBaseWriteValidation>(mapping)
                .ValidateWriteValue(date.AddTicks(10)));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, 7)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, 10)]
        [InlineData(OceanBaseCompatibilityMode.MySql, -1)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, -1)]
        public void Invalid_temporal_precision_is_rejected(OceanBaseCompatibilityMode mode, int precision)
        {
            using var context = CreateContext(mode);
            Assert.Throws<ArgumentOutOfRangeException>(() => context.GetService<IRelationalTypeMappingSource>()
                .FindMapping(typeof(DateTime), null, precision: precision));
        }

        [Fact]
        public void MySql_duration_rejects_range_and_submicrosecond_loss()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(TimeSpan))!;
            using var command = new OceanBaseCommand();
            var maximum = new TimeSpan(0, 838, 59, 59).Add(TimeSpan.FromTicks(9999990));

            Assert.Equal(maximum, mapping.CreateParameter(command, "duration", maximum).Value);
            Assert.Throws<ArgumentOutOfRangeException>(() => mapping.CreateParameter(command, "duration", maximum.Add(TimeSpan.FromTicks(10))));
            Assert.Throws<ArgumentException>(() => mapping.GenerateSqlLiteral(TimeSpan.FromTicks(1)));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(DateTime), "DATETIME", 0)]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(TimeSpan), "TIME", 0)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateTime), "TIMESTAMP", 6)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateTime), "DATE", 0)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateTimeOffset), "TIMESTAMP WITH TIME ZONE", 6)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(TimeSpan), "INTERVAL DAY TO SECOND", 6)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(TimeSpan), "INTERVAL DAY(3) TO SECOND(9)", 9)]
        public void Explicit_temporal_declarations_use_the_store_defaults(
            OceanBaseCompatibilityMode mode, Type type, string storeType, int precision)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(type, storeType)!;
            using var command = new OceanBaseCommand();

            Assert.Equal(storeType, mapping.StoreType);
            Assert.Equal(precision, mapping.Precision);
            Assert.Equal(Math.Max(precision, mode == OceanBaseCompatibilityMode.MySql ? 6 : 7),
                mapping.CreateParameter(command, "value", null, nullable: true).Scale);
            var output = mapping.CreateParameter(command, "output", null, direction: ParameterDirection.Output);
            Assert.Equal(precision, output.Scale);
            if (type == typeof(TimeSpan) && mode == OceanBaseCompatibilityMode.Oracle)
            {
                Assert.Equal((byte)9, mapping.CreateParameter(command, "value", null).Precision);
                Assert.Equal(precision == 9 ? 3 : 2, output.Precision);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Temporal_declarations_reject_conflicts_and_incompatible_types(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();

            Assert.Throws<NotSupportedException>(() => source.FindMapping(typeof(DateTime), "VARCHAR(32)"));
            Assert.Throws<ArgumentException>(() => source.FindMapping(typeof(DateTime),
                mode == OceanBaseCompatibilityMode.Oracle ? "TIMESTAMP(3)" : "DATETIME(3)", precision: 6));
            Assert.Throws<ArgumentException>(() => source.FindMapping(typeof(DateTime), null, precision: 3, scale: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.FindMapping(typeof(DateOnly), null, precision: 1));
        }

        [Fact]
        public void Oracle_interval_preserves_CLR_endpoints_and_rejects_subtick_results()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(TimeSpan))!;
            using var command = new OceanBaseCommand();

            foreach (var endpoint in new[] { TimeSpan.MinValue, TimeSpan.MaxValue })
            {
                var parameter = mapping.CreateParameter(command, "duration", endpoint);
                Assert.Equal(endpoint, Assert.IsType<OceanBaseIntervalDayToSecond>(parameter.Value).ToTimeSpan());
            }

            Assert.Throws<OverflowException>(() => Read(mapping,
                new OceanBaseIntervalDayToSecond(false, 0, 0, 0, 0, 1)));
            var narrow = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(TimeSpan), "INTERVAL DAY TO SECOND")!;
            Assert.Equal(TimeSpan.FromDays(100), Assert.IsType<OceanBaseIntervalDayToSecond>(
                narrow.CreateParameter(command, "duration", TimeSpan.FromDays(100)).Value).ToTimeSpan());
            Assert.Throws<ArgumentOutOfRangeException>(() => Assert.IsAssignableFrom<IOceanBaseWriteValidation>(narrow)
                .ValidateWriteValue(TimeSpan.FromDays(100)));
        }

        [Fact]
        public void Oracle_timestamp_preserves_ticks_and_rejects_subtick_results()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(DateTime), "TIMESTAMP(9)")!;
            var date = new DateTime(2026, 11, 10);

            Assert.Equal(date.AddTicks(1234567), Read(mapping, new OceanBaseTimestamp(date, 123456700, 9)));
            Assert.Throws<OverflowException>(() => Read(mapping, new OceanBaseTimestamp(date, 1, 9)));
        }

        [Theory]
        [InlineData(typeof(TimeSpan), "INTERVAL DAY(9) TO SECOND(3)", "INTERVAL DAY(9) TO SECOND(7)")]
        [InlineData(typeof(TimeOnly), "INTERVAL DAY(1) TO SECOND(3)", "INTERVAL DAY(1) TO SECOND(7)")]
        public void Oracle_interval_precision_clone_keeps_new_store_and_value_precision(Type type, string original, string expected)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(type, original)!
                .WithPrecisionAndScale(7, null);
            using var command = new OceanBaseCommand();
            object value = type == typeof(TimeOnly) ? new TimeOnly(1) : TimeSpan.FromTicks(1);

            var parameter = mapping.CreateParameter(command, "value", value);

            Assert.Equal(expected, mapping.StoreType);
            Assert.Equal(7, parameter.Scale);
            Assert.Equal(TimeSpan.FromTicks(1), Assert.IsType<OceanBaseIntervalDayToSecond>(parameter.Value).ToTimeSpan());
            Assert.Contains(".0000001'", mapping.GenerateSqlLiteral(value));
            var reduced = mapping.WithPrecisionAndScale(0, null);
            Assert.NotNull(reduced.CreateParameter(command, "value", value).Value);
            Assert.Throws<ArgumentException>(() => Assert.IsAssignableFrom<IOceanBaseWriteValidation>(reduced).ValidateWriteValue(value));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "DATETIME(3)", "DATETIME(6)", 6)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "TIMESTAMP(3)", "TIMESTAMP(7)", 7)]
        public void Timestamp_precision_clone_updates_storage_and_parameter_metadata(
            OceanBaseCompatibilityMode mode, string original, string expected, int precision)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(DateTime), original)!
                .WithPrecisionAndScale(precision, null);
            using var command = new OceanBaseCommand();
            var date = new DateTime(2026, 11, 10).AddTicks(mode == OceanBaseCompatibilityMode.Oracle ? 1 : 10);

            Assert.Equal(expected, mapping.StoreType);
            Assert.Equal(precision, mapping.CreateParameter(command, "value", date).Scale);
        }

        private static object Read(RelationalTypeMapping mapping, object value)
        {
            using var table = new DataTable();
            table.Columns.Add("Value", value.GetType());
            table.Rows.Add(value);
            using var reader = table.CreateDataReader();
            Assert.True(reader.Read());
            var call = Expression.Call(Expression.Constant(reader), mapping.GetDataReaderMethod(), Expression.Constant(0));
            var expression = mapping.CustomizeDataReaderExpression(call);
            var result = Expression.Lambda<Func<object>>(Expression.Convert(expression, typeof(object))).Compile()();
            return mapping.Converter is { } converter ? converter.ConvertFromProvider(result)! : result;
        }

        private static TestContext CreateContext(OceanBaseCompatibilityMode mode)
            => new(new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode).Options);

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options);
        private sealed record Stamp(DateTime Value);
    }
}
