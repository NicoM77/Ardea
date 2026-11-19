using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Ardea.Platform.Telemetry;

/// <summary>
/// OpenTelemetry for traces, metrics and logs (master plan 12.7). Nothing leaves the instance unless
/// the operator sets <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>; the application never reports to its
/// developers (master plan 9.4). The trace context travels inside jobs, so one trace runs from the
/// source to the delivery.
/// </summary>
public static class TelemetryServices
{
    public const string SourcePrefix = "Ardea.";

    public static bool IsExportConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));

    public static IServiceCollection AddArdeaTelemetry(this IServiceCollection services, ILoggingBuilder logging, IConfiguration configuration, string role, string version)
    {
        if (!IsExportConfigured(configuration))
        {
            return services;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService("ardea", serviceVersion: version, serviceInstanceId: $"{Environment.MachineName}-{role}")
                .AddAttributes([new KeyValuePair<string, object>("ardea.role", role)]))
            .WithTracing(tracing => tracing
                .AddSource($"{SourcePrefix}*")
                .AddAspNetCoreInstrumentation(o => o.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                // Push endpoints and chat webhooks carry their secret in the address (ADR 041): no span for them.
                .AddHttpClientInstrumentation(o => o.FilterHttpRequestMessage = request =>
                    !(request.Options.TryGetValue(Http.OutboundAddresses.Untraced, out var untraced) && untraced))
                .AddNpgsql()
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddMeter($"{SourcePrefix}*")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter());

        logging.AddOpenTelemetry(o =>
        {
            o.IncludeScopes = true;
            o.IncludeFormattedMessage = true;
            o.AddOtlpExporter();
        });
        return services;
    }
}
