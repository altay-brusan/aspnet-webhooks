namespace Webhooks.Api.http.Services
{
    internal sealed record WebhookDispatched(string EventType, object Data);
}
