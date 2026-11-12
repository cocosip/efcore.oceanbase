using System;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Storage.Json;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Storage;

internal sealed class OceanBaseIntervalJsonReaderWriter : JsonValueReaderWriter<OceanBaseIntervalDayToSecond>
{
    private static readonly PropertyInfo InstanceProperty = typeof(OceanBaseIntervalJsonReaderWriter)
        .GetProperty(nameof(Instance))!;
    private static readonly Regex Pattern = new(
        @"\A(?<sign>[+-])(?<days>\d{1,9}) (?<hours>\d{2}):(?<minutes>\d{2}):(?<seconds>\d{2})(?:\.(?<fraction>\d{1,9}))?\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static OceanBaseIntervalJsonReaderWriter Instance { get; } = new();

    public override Expression ConstructorExpression => Expression.Property(null, InstanceProperty);

    public override OceanBaseIntervalDayToSecond FromJsonTyped(
        ref Utf8JsonReaderManager manager, object? existingObject = null)
    {
        var text = manager.CurrentReader.GetString();
        var match = text is null ? null : Pattern.Match(text);
        if (match is null || !match.Success)
            throw new JsonException("The OceanBase interval JSON value has an invalid format.");

        var fraction = match.Groups["fraction"].Value;
        var nanosecond = fraction.Length == 0
            ? 0
            : int.Parse(fraction.PadRight(9, '0'), CultureInfo.InvariantCulture);
        return new OceanBaseIntervalDayToSecond(
            match.Groups["sign"].Value[0] == '-',
            int.Parse(match.Groups["days"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["hours"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["seconds"].Value, CultureInfo.InvariantCulture),
            nanosecond,
            checked((byte)match.Groups["days"].Value.Length),
            checked((byte)fraction.Length));
    }

    public override void ToJsonTyped(Utf8JsonWriter writer, OceanBaseIntervalDayToSecond value)
        => writer.WriteStringValue(value.ToString());
}
