namespace Webhooks.Api.http.Services
{
    public sealed class WebhookPayload
    {
        public required Guid Id { get; init; }
        public required string EventType { get; init; }
        public required Guid SubscriptionId { get; init; }
        public required DateTime Timestamp { get; init; }
        public required object Data { get; init; }
    }
}
