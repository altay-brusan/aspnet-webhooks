namespace Webhooks.Api.http.Services
{
    internal sealed record WebhookDispatch(string EventType, object Data, string? ParentActivityId);
}
