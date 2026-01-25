namespace Webhooks.Api.http.Services
{
    public sealed class WebhookPayload
    {
        public Guid Id { get; init; }
        public string EventType { get; init; }
        public Guid SubscriptionId { get; init; }
        public DateTime Timestamp { get; init; }
        public object Data { get; init; }
    }
}
