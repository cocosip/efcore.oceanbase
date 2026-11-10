namespace OceanBase.EntityFrameworkCore
{
    /// <summary>Specifies the SQL compatibility mode used by an OceanBase tenant.</summary>
    public enum OceanBaseCompatibilityMode
    {
        /// <summary>Use MySQL-compatible SQL and type semantics.</summary>
        MySql,

        /// <summary>Use Oracle-compatible SQL and type semantics.</summary>
        Oracle
    }
}
