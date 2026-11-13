using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Storage;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Design;

#pragma warning disable EF1001 // EF10's provider extension seam for compiled mapping construction.
internal sealed class OceanBaseRuntimeAnnotationCodeGenerator(CSharpRuntimeAnnotationCodeGeneratorDependencies dependencies,
    RelationalCSharpRuntimeAnnotationCodeGeneratorDependencies relationalDependencies)
    : RelationalCSharpRuntimeAnnotationCodeGenerator(dependencies, relationalDependencies)
{
    public override void Generate(IProperty property, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        base.Generate(property, parameters);
        // EF otherwise emits a composed converter and re-infers storage from its provider CLR type.
        if (!parameters.IsRuntime && !parameters.ForNativeAot && property.GetValueConverter() is not null
            && property.GetTypeMapping() is IOceanBaseWriteValidation && property.GetTypeMapping().Converter?.MappingHints is null)
        {
            parameters.MainBuilder.Append(parameters.TargetName).Append(".TypeMapping = ");
            ((ICSharpRuntimeAnnotationCodeGenerator)this).Create(property.GetTypeMapping(), property, parameters);
            parameters.MainBuilder.AppendLine(";");
        }
    }

    protected override CoreTypeMapping? CreateDefaultTypeMapping(CoreTypeMapping mapping,
        CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        if (mapping is not RelationalTypeMapping relational || mapping is not IOceanBaseWriteValidation provider)
            return base.CreateDefaultTypeMapping(mapping, parameters);

        var type = mapping.GetType();
        var underlying = type.IsGenericType ? type.GenericTypeArguments[0] : null;
        var mode = provider.CompatibilityMode;
        var store = relational.StoreType;
        object?[] facets = [relational.Precision, relational.Scale, relational.Size, relational.IsFixedLength];
        string method;
        object?[] arguments;
        if (underlying is not null && type.GetGenericTypeDefinition() == typeof(OceanBaseIntegerTypeMapping<>))
            (method, arguments) = (nameof(OceanBaseCompiledModelTypeMappings.Integer), [store, relational.DbType, mode, .. facets]);
        else if (underlying is not null && type.GetGenericTypeDefinition() == typeof(OceanBaseFloatingPointTypeMapping<>))
            (method, arguments) = (nameof(OceanBaseCompiledModelTypeMappings.FloatingPoint), [store, mode, .. facets]);
        else if (mapping is OceanBaseDecimalTypeMapping)
            (method, arguments) = (nameof(OceanBaseCompiledModelTypeMappings.Decimal), [store, .. facets, mode]);
        else if (mapping is OceanBaseStringTypeMapping)
            (method, arguments) = (nameof(OceanBaseCompiledModelTypeMappings.Text),
                [store, relational.DbType, relational.IsUnicode, relational.Size, relational.IsFixedLength, mode]);
        else if (mapping is OceanBaseByteArrayTypeMapping)
            (method, arguments) = (nameof(OceanBaseCompiledModelTypeMappings.Binary), [store, relational.Size, mode, relational.IsFixedLength]);
        else if (underlying is not null && type.GetGenericTypeDefinition() == typeof(OceanBaseTemporalTypeMapping<>))
        {
            var leading = underlying == typeof(OceanBaseIntervalDayToSecond)
                ? (byte)OceanBaseTemporalTypeMapping.ReadFacet(OceanBaseTemporalTypeMapping.MatchDeclaration(
                    store, mode, false, true, false), "days", 2) : (byte)0;
            (method, arguments) = (nameof(OceanBaseCompiledModelTypeMappings.Temporal), [store, relational.DbType, relational.Precision, mode, leading]);
        }
        else return base.CreateDefaultTypeMapping(mapping, parameters);

        var code = Dependencies.CSharpHelper;
        AddNamespace(typeof(OceanBaseCompiledModelTypeMappings), parameters.Namespaces);
        if (underlying is not null) AddNamespace(underlying, parameters.Namespaces);
        AddNamespace(typeof(System.Data.DbType), parameters.Namespaces);
        AddNamespace(typeof(OceanBaseCompatibilityMode), parameters.Namespaces);
        parameters.MainBuilder.Append("OceanBaseCompiledModelTypeMappings.").Append(method);
        var factory = typeof(OceanBaseCompiledModelTypeMappings).GetMethod(method)!;
        if (underlying is not null)
        {
            parameters.MainBuilder.Append("<").Append(code.Reference(underlying)).Append(">");
            factory = factory.MakeGenericMethod(underlying);
        }
        parameters.MainBuilder.Append("(").Append(string.Join(", ", arguments.Select(value => value is OceanBaseCompatibilityMode compatibility
            ? code.Reference(typeof(OceanBaseCompatibilityMode), fullName: true) + "." + compatibility : code.UnknownLiteral(value)))).Append(")");
        // EF compares this unconverted baseline and emits the original converters/comparers in Clone.
        return (RelationalTypeMapping)factory.Invoke(null, arguments)!;
    }
}
#pragma warning restore EF1001
