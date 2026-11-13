using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Linq.Expressions;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using OceanBaseConnector;
using Mode = OceanBase.EntityFrameworkCore.OceanBaseCompatibilityMode;

namespace OceanBase.EntityFrameworkCore.UnitTests;

public sealed class CompiledModelTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public void Actual_EF_generator_preserves_provider_mappings_without_internal_type_references(OceanBaseCompatibilityMode mode, bool hints)
    {
        using var context = Create(mode, hints: hints);
        var (model, code) = Compile(context, mode);
        using var compiled = Create(mode, model);
        var expected = context.Model.FindEntityType(typeof(Row))!;
        var actual = compiled.Model.FindEntityType(typeof(Row))!;
        var values = new Dictionary<string, object>
        {
            [nameof(Row.Id)] = 3, [nameof(Row.Name)] = "caption", [nameof(Row.Amount)] = 123.45m,
            [nameof(Row.Token)] = Guid.NewGuid(), [nameof(Row.Day)] = new DateOnly(2024, 2, 29),
            [nameof(Row.Clock)] = new TimeOnly(12, 34, 56), [nameof(Row.Active)] = true,
            [nameof(Row.Reading)] = 1.25f, [nameof(Row.Code)] = new Code("sample"),
            [nameof(Row.Buffer)] = new byte[] { 1, 2, 3 }, [nameof(Row.Duration)] = TimeSpan.FromHours(-25),
            [nameof(Row.Zoned)] = new DateTimeOffset(2024, 2, 29, 12, 34, 56, TimeSpan.FromHours(8))
        };
        foreach (var property in expected.GetProperties())
        {
            var source = property.GetRelationalTypeMapping();
            var target = actual.FindProperty(property.Name)!.GetRelationalTypeMapping();
            Assert.Equal(source.GetType(), target.GetType());
            Assert.Equal(source.StoreType, target.StoreType);
            Assert.Equal(source.Size, target.Size);
            Assert.Equal(source.Precision, target.Precision);
            Assert.Equal(source.Scale, target.Scale);
            Assert.Equal(source.Converter?.ProviderClrType, target.Converter?.ProviderClrType);
            Assert.Equal(property.Sentinel, actual.FindProperty(property.Name)!.Sentinel);
            using var command = new OceanBaseCommand();
            var original = source.CreateParameter(command, "original", values[property.Name]);
            var generated = target.CreateParameter(command, "generated", values[property.Name]);
            Assert.Equal(original.Value, generated.Value);
            Assert.Equal(original.DbType, generated.DbType);
            Assert.Equal(original.Size, generated.Size);
            Assert.Equal(original.Precision, generated.Precision);
            Assert.Equal(original.Scale, generated.Scale);
            if (target.Converter is { } converter)
                Assert.Equal(converter.ConvertFromProvider(converter.ConvertToProvider(values[property.Name])),
                    source.Converter!.ConvertFromProvider(source.Converter.ConvertToProvider(values[property.Name])));
        }
        var value = new Code("sample");
        Assert.Equal("sample", actual.FindProperty(nameof(Row.Code))!.GetTypeMapping().Converter!.ConvertToProvider(value));
        Assert.Equal(context.Set<Row>().Where(r => r.Id == 4).ToQueryString(), compiled.Set<Row>().Where(r => r.Id == 4).ToQueryString());
        Assert.DoesNotContain("new OceanBaseIntegerTypeMapping", code);
        Assert.DoesNotContain("new OceanBaseStringTypeMapping", code);
        Assert.Throws<ArgumentException>(() => ((OceanBase.EntityFrameworkCore.Storage.IOceanBaseWriteValidation)
            actual.FindProperty(nameof(Row.Buffer))!.GetTypeMapping()).ValidateWriteValue(new byte[5]));
        Assert.Throws<ArgumentException>(() => ((OceanBase.EntityFrameworkCore.Storage.IOceanBaseWriteValidation)
            actual.FindProperty(nameof(Row.Code))!.GetTypeMapping()).ValidateWriteValue(new Code(new string('x', 13))));
    }

    [Theory]
    [InlineData(Mode.MySql, false)]
    [InlineData(Mode.MySql, true)]
    [InlineData(Mode.Oracle, false)]
    [InlineData(Mode.Oracle, true)]
    public void Unsupported_ordinary_or_named_query_filters_fail_visibly(Mode mode, bool named)
    {
        using var context = new FilteredContext(new DbContextOptionsBuilder<Context>()
            .UseOceanBase("Server=offline", mode).EnableServiceProviderCaching(false).Options, named);
        var error = Assert.Throws<InvalidOperationException>(() => Compile(context, mode));
        Assert.Contains("query filter", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(true, 123, 123456789)]
    [InlineData(false, 999999999, 999999900)]
    public void Interval_CSharp_literals_preserve_sign_range_and_nanoseconds(bool negative, int days, int nanos)
    {
        using var context = Create(Mode.Oracle);
        var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(OceanBaseIntervalDayToSecond))!;
        var value = new OceanBaseIntervalDayToSecond(negative, days, 23, 59, 58, nanos, 9, 9);
        var expression = mapping.GenerateCodeLiteral(value);
        var copy = Expression.Lambda<Func<OceanBaseIntervalDayToSecond>>(expression).Compile()();
        Assert.Equal(value, copy);
        Assert.Equal(value.LeadingPrecision, copy.LeadingPrecision);
        Assert.Equal(value.Scale, copy.Scale);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Compiled_model_rejects_the_other_compatibility_mode_before_use(OceanBaseCompatibilityMode mode)
    {
        using var context = Create(mode);
        var (model, _) = Compile(context, mode);
        using var matching = Create(mode, model);
        _ = matching.Model;
        using var other = Create(mode == OceanBaseCompatibilityMode.MySql ? OceanBaseCompatibilityMode.Oracle : OceanBaseCompatibilityMode.MySql, model);
        var error = Assert.Throws<InvalidOperationException>(() => other.Model);
        Assert.Contains("compatibility mode", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Context Create(OceanBaseCompatibilityMode mode, IModel? model = null, bool hints = true)
    {
        var options = new DbContextOptionsBuilder<Context>().UseOceanBase("Server=offline", mode);
        if (model is not null) options.UseModel(model);
        return hints ? new Context(options.Options) : new UnhintedContext(options.Options);
    }

    private static (IModel Model, string Code) Compile(Context context, OceanBaseCompatibilityMode mode)
    {
        using var services = (ServiceProvider)new DesignTimeServicesBuilder(typeof(Context).Assembly,
            typeof(Context).Assembly, new OperationReporter(null), []).Build(context);
        var options = new CompiledModelCodeGenerationOptions { ContextType = typeof(Context), ModelNamespace = "Compiled.Sample." + mode, Language = "C#" };
        var generator = services.GetRequiredService<ICompiledModelCodeGeneratorSelector>().Select(options);
        var files = generator.GenerateModel(context.GetService<IDesignTimeModel>().Model, options);
        Assert.NotEmpty(files);
        var code = string.Join("\n", files.Select(file => file.Code));
        Assert.Contains("RuntimeModel", code);
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location)).Select(a => a.Location)).Distinct();
        var compilation = CSharpCompilation.Create("CompiledModel_" + Guid.NewGuid().ToString("N"),
            files.Select(file => CSharpSyntaxTree.ParseText(file.Code)), paths.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(stream.ToArray());
        var model = (IModel)assembly.GetType(options.ModelNamespace + ".ContextModel", throwOnError: true)!
            .GetProperty("Instance")!.GetValue(null)!;
        return (model, code);
    }

    public class Context(DbContextOptions<Context> options) : DbContext(options)
    {
        protected virtual bool UseMappingHints => true;
        protected override void OnModelCreating(ModelBuilder model)
        {
            var row = model.Entity<Row>();
            row.ToTable("CompiledRows");
            row.Property(r => r.Name).HasMaxLength(32);
            row.Property(r => r.Amount).HasPrecision(12, 2);
            row.Property(r => r.Code).HasConversion(new ValueConverter<Code, string>(
                value => value.Value, value => new Code(value), new ConverterMappingHints(size: 12)));
            row.Property(r => r.Buffer).HasMaxLength(4);
            Hints(r => r.Id); Hints(r => r.Amount); Hints(r => r.Token); Hints(r => r.Day); Hints(r => r.Clock);
            Hints(r => r.Active); Hints(r => r.Reading); Hints(r => r.Buffer); Hints(r => r.Duration); Hints(r => r.Zoned);
            void Hints<T>(Expression<Func<Row, T>> property)
                => row.Property(property).HasConversion(new ValueConverter<T, T>(value => value, value => value,
                    UseMappingHints ? new ConverterMappingHints() : null));
        }
    }
    public sealed class UnhintedContext(DbContextOptions<Context> options) : Context(options)
    {
        protected override bool UseMappingHints => false;
    }
    public sealed class FilteredContext(DbContextOptions<Context> options, bool named) : Context(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            base.OnModelCreating(model);
            if (named) model.Entity<Row>().HasQueryFilter("Enabled", row => row.Active);
            else model.Entity<Row>().HasQueryFilter(row => row.Active);
        }
    }
    public sealed class Row
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public decimal Amount { get; set; }
        public Guid Token { get; set; }
        public DateOnly Day { get; set; }
        public TimeOnly Clock { get; set; }
        public bool Active { get; set; }
        public float Reading { get; set; }
        public Code Code { get; set; } = new("");
        public byte[] Buffer { get; set; } = [];
        public TimeSpan Duration { get; set; }
        public DateTimeOffset Zoned { get; set; }
    }
    public sealed record Code(string Value);
}
