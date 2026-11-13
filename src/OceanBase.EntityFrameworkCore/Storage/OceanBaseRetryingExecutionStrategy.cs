using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Internal;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Storage;

/// <summary>Retries confirmed transient server failures using EF's operation-level execution strategy.</summary>
/// <remarks>Unknown execution outcomes, cancellation, XA and failed cleanup are never replayed.</remarks>
public class OceanBaseRetryingExecutionStrategy : ExecutionStrategy
{
    private const string UnsafeCleanup = "OceanBase.RetryUnsafeCleanup";
    private readonly HashSet<int> _additionalCodes;
    private readonly OceanBaseCompatibilityMode _mode;

    /// <summary>Creates a retry strategy; null maximum delay uses EF's 30-second default.</summary>
    public OceanBaseRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies, int maxRetryCount = 6,
        TimeSpan? maxRetryDelay = null, IEnumerable<int>? additionalErrorCodes = null)
        : base(dependencies, maxRetryCount, maxRetryDelay ?? DefaultMaxDelay)
    {
        _mode = dependencies.Options.FindExtension<OceanBaseOptionsExtension>()!.CompatibilityMode;
        _additionalCodes = additionalErrorCodes is null ? [] : new HashSet<int>(additionalErrorCodes);
        if (_additionalCodes.Contains(0)) throw new ArgumentException("Code zero identifies client-local errors and cannot be retried.", nameof(additionalErrorCodes));
    }

    /// <inheritdoc />
    protected override bool ShouldRetryOn(Exception exception)
    {
        if (exception is not OceanBaseException { IsExecutionOutcomeUnknown: false, XaErrorCode: null } error
            || error.ServerErrorCode == 0 || error.Data.Contains(UnsafeCleanup)
            || error.SqlState is "70100" or "57014"
            || (_mode == OceanBaseCompatibilityMode.MySql ? error.ServerErrorCode is 1317 or 3024 : error.ServerErrorCode == 1013)
            || Dependencies.CurrentContext.Context.Database.CurrentTransaction is not null)
            return false;
        return error.SqlState == "40001" || _additionalCodes.Contains(error.ServerErrorCode)
            || (_mode == OceanBaseCompatibilityMode.MySql ? error.ServerErrorCode is 1205 or 1213
                : error.ServerErrorCode is 54 or 60 or 8177);
    }

    // Confirmed server failures need replay, while unknown outcomes require application reconciliation.
    /// <inheritdoc />
    protected override bool ShouldVerifySuccessOn(Exception exception) => false;

    internal static void PreventRetry(Exception error)
    {
        // EF unwraps DbUpdateException before classification; preserve the veto on its original cause too.
        for (Exception? current = error; current is not null; current = current.InnerException)
            current.Data[UnsafeCleanup] = true;
    }
}
