using System;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    internal static class LiveTestConfiguration
    {
        internal static string ConnectionString(string configured)
            => ApplyTransport(configured, Environment.GetEnvironmentVariable("OCEANBASE_LIVE_DISABLE_TLS") == "1");

        internal static string ApplyTransport(string configured, bool disableTls)
            => disableTls ? new OceanBaseConnectionStringBuilder(configured) { TlsMode = OceanBaseTlsMode.Disabled }.ConnectionString : configured;
    }
}
