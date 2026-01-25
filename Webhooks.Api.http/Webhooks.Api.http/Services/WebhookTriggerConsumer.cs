using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Threading;
using Webhooks.Api.http.Data;
using Webhooks.Api.http.Models;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Webhooks.Api.http.Services
{
    internal sealed class WebhookTriggerConsumer(IHttpClientFactory httpClientFactory, 
        WebhooksDbContext dbContext) : IConsumer<WebhookTriggered>
    {
        public async Task Consume(ConsumeContext<WebhookTriggered> context)
        {
            var subscriptions = await dbContext.WebhookSubscriptions
               .AsNoTracking()
               .Where(ws => ws.EventType == context.Message.EventType)
               .ToListAsync();


            foreach (var subscription in subscriptions)
            {
                using var httpClient = httpClientFactory.CreateClient();

                var payload = new WebhookPayload
                {
                    Id = Guid.NewGuid(),
                    EventType = context.Message.EventType,
                    SubscriptionId = subscription.Id,
                    Data = context.Message.Data,
                    Timestamp = DateTime.UtcNow
                };
                var jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
                try
                {
                    var response = await httpClient.PostAsJsonAsync(subscription.WebhookUrl, payload);
                    response.EnsureSuccessStatusCode();
                    var attempt = new WebhookDeliveryAttempt
                    {
                        Id = Guid.NewGuid(),
                        WebhookSubscriptionId = subscription.Id,
                        Timestamp = DateTime.UtcNow,
                        ResponseStatusCode = (int)response.StatusCode,
                        Success = response.IsSuccessStatusCode,
                        Payload = jsonPayload
                    };
                    dbContext.WebhookDeliveryAttempts.Add(attempt);

                    await dbContext.SaveChangesAsync();
                }
                catch (Exception)
                {
                    var attempt = new WebhookDeliveryAttempt
                    {
                        Id = Guid.NewGuid(),
                        WebhookSubscriptionId = subscription.Id,
                        Timestamp = DateTime.UtcNow,
                        ResponseStatusCode = null,
                        Success = false,
                        Payload = jsonPayload
                    };
                    dbContext.WebhookDeliveryAttempts.Add(attempt);
                    await dbContext.SaveChangesAsync();
                }
            }
        }
    }
}
