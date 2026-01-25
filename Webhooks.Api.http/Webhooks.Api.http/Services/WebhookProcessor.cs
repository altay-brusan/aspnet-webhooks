using System.Diagnostics;
using System.Threading.Channels;
using Webhooks.Api.http.OpenTelemetry;

namespace Webhooks.Api.http.Services
{
    internal sealed class WebhookProcessor(
        IServiceScopeFactory serviceScopeFactory,
        Channel<WebhookDispatch> webhooksChannel
        ) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (WebhookDispatch webhookDispatch in webhooksChannel.Reader.ReadAllAsync())
            {
                using Activity? activity =
                    DiagnosticConfig.source.StartActivity($"{webhookDispatch.EventType} process webhook", ActivityKind.Internal, parentId: webhookDispatch.ParentActivityId);
                using IServiceScope serviceScope = serviceScopeFactory.CreateScope();
                var dispatcher = serviceScope.ServiceProvider.GetRequiredService<WebhookDispatcher>();
                await dispatcher.DispatchAsync(webhookDispatch.EventType, webhookDispatch.Data);
            }
        }
    }
}
