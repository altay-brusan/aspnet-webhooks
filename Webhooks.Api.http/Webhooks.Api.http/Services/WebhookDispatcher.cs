using MassTransit;
using System.Diagnostics;
using Webhooks.Api.http.OpenTelemetry;

namespace Webhooks.Api.http.Services
{
    internal sealed record WebhookDispatched(string EventType, object Data);

    internal sealed class WebhookDispatcher
    {
        private readonly IPublishEndpoint _publishEndpoint;

        public WebhookDispatcher(IPublishEndpoint publishEndpoint)
        {
            _publishEndpoint = publishEndpoint;
        }

        public async Task DispatchAsync<T>(string eventType, T data) where T : notnull
        {
            using Activity? activity = DiagnosticConfig.source.StartActivity($"{eventType} dispatch webhook");
            activity?.AddTag("event.type", eventType);

            await _publishEndpoint.Publish(new WebhookDispatched(eventType, data));
        }
    }
}