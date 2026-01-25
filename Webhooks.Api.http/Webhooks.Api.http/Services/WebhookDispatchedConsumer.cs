using MassTransit;
using Microsoft.EntityFrameworkCore;
using Webhooks.Api.http.Data;

namespace Webhooks.Api.http.Services
{
    internal sealed class WebhookDispatchedConsumer(WebhooksDbContext dbContext) : IConsumer<WebhookDispatched>
    {
        public async Task Consume(ConsumeContext<WebhookDispatched> context)
        {
            var message = context.Message;

            var subscriptions = await dbContext.WebhookSubscriptions
                .AsNoTracking()
                .Where(ws => ws.EventType == message.EventType)
                .ToListAsync(context.CancellationToken);

            foreach (var subscription in subscriptions) 
            {
                await context.Publish(new WebhookTriggered(
                    SubscriptionId: subscription.Id,
                    EventType: message.EventType,
                    WebhookUrl: subscription.WebhookUrl,
                    Data: message.Data
                ), context.CancellationToken);

            }



        }
    }


}
