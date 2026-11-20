using System;
using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.Extensions.Logging;
using OceanBaseConnector;
using static OceanBase.EntityFrameworkCore.Metadata.OceanBaseRelationalAnnotationProvider;

namespace OceanBase.EntityFrameworkCore.Design
{
    internal sealed partial class OceanBaseDatabaseModelFactory
    {
        private void ReadSequences(OceanBaseConnection connection, string schema, DatabaseModel database)
        {
            using var rows = Schema(connection, "Sequences", schema, OceanBaseCompatibilityMode.Oracle);
            foreach (DataRow row in rows.Rows)
            {
                var name = Text(row, "SEQUENCE_NAME")!;
                if (!TryInt64(row["MINIMUM_VALUE"], out var min) || !TryInt64(row["MAXIMUM_VALUE"], out var max)
                    || !TryInt64(row["INCREMENT"], out var increment) || increment is < int.MinValue or > int.MaxValue or 0
                    || !TryInt64(row["CACHE_SIZE"], out var cache) || cache is < 0 or 1)
                {
                    _logger.LogWarning("Sequence {Schema}.{Sequence} is outside the supported EF Int64/increment/cache range and was omitted.", schema, name);
                    continue;
                }
                var startValue = connection.GetOracleSequenceStartValue(name, schema);
                if (!TryInt64(startValue, out var start))
                {
                    _logger.LogWarning("Sequence {Schema}.{Sequence} has a start outside the EF Int64 range and was omitted.", schema, name);
                    continue;
                }
                Storage.OceanBaseSequenceSql.ValidateStart(start, (int)increment, min, max);
                var sequence = new DatabaseSequence
                {
                    Database = database, Name = name, Schema = schema, StoreType = "NUMBER(18)",
                    StartValue = start, IncrementBy = (int)increment, MinValue = min, MaxValue = max,
                    IsCyclic = SequenceFlag(row, "CYCLE_FLAG")
                };
                sequence[SequenceCacheAnnotation] = cache;
                sequence[SequenceOrderedAnnotation] = SequenceFlag(row, "ORDER_FLAG");
                database.Sequences.Add(sequence);
            }
        }

        private static bool TryInt64(object value, out long result)
            => long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out result);

        private static bool SequenceFlag(DataRow row, string field)
            => Text(row, field) switch { "Y" => true, "N" => false, _ => throw Unsupported("unknown sequence " + field) };
    }
}
