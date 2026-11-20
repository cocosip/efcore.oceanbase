using System;
using Microsoft.EntityFrameworkCore.Query;

namespace Microsoft.EntityFrameworkCore
{
    /// <summary>Provides OceanBase SQL functions for use in LINQ queries.</summary>
    public static class OceanBaseDbFunctionsExtensions
    {
        /// <summary>Extracts a JSON value as text using OceanBase's JSON_VALUE function.</summary>
        /// <remarks>
        /// The path must be constant for each query. Server JSON path, conversion, error and NULL
        /// semantics apply. The default result limit is 512 characters in MySQL mode and 4000
        /// bytes in Oracle mode; overflow returns NULL. MySQL mode also returns objects and arrays
        /// as JSON text, whereas Oracle mode returns NULL for nonscalars. This method has no client implementation.
        /// </remarks>
        public static string? OceanBaseJsonValue(this DbFunctions _, string? document, [NotParameterized] string path)
            => throw new InvalidOperationException("OceanBaseJsonValue can only be used in an EF Core query.");
    }
}
