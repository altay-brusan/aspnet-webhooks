using System.Diagnostics;

namespace Webhooks.Api.http.OpenTelemetry
{
    internal static  class DiagnosticConfig
    {
        internal static readonly ActivitySource source = new("webhooks-api");
    }
}
