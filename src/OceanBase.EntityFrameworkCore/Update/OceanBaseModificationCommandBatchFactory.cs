using System;
using Microsoft.EntityFrameworkCore.Update;

namespace OceanBase.EntityFrameworkCore.Update;

// shortcut: update SQL is implemented in Task 4; fail before any write can be attempted.
internal sealed class OceanBaseModificationCommandBatchFactory : IModificationCommandBatchFactory
{
    public ModificationCommandBatch Create()
        => throw new NotSupportedException("OceanBase update SQL is not implemented yet.");
}
