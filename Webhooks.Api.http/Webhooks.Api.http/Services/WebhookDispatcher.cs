using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using System.Diagnostics;
using System.Threading.Channels;
using Webhooks.Api.http.Data;
using Webhooks.Api.http.Models;
using Webhooks.Api.http.OpenTelemetry;


namespace Webhooks.Api.http.Services
{


    internal sealed class WebhookDispatcher
    {
        private readonly IPublishEndpoint _publishEndpoint;

        public WebhookDispatcher(
            IPublishEndpoint publishEndpoint)
        {
            _publishEndpoint = publishEndpoint;
        }

        public async Task DispatchAsync<T>(string eventType, T data) where T : notnull
        {
            using Activity? activity = DiagnosticConfig.source.StartActivity($"{ eventType} dispatch webhook");
            activity?.AddTag("event.type", eventType);

            await _publishEndpoint.Publish(new WebhookDispatched(eventType, data));
        }

    }
}