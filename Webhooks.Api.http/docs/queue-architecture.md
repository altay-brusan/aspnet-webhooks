# Queue Architecture: MassTransit + RabbitMQ

This document explains the messaging pipeline used in the Webhooks.Api.http project.

## Overview

The project uses **MassTransit** as an abstraction layer over **RabbitMQ** to handle webhook delivery asynchronously. When an order is created via the API, the actual webhook delivery happens in the background through a two-stage message pipeline.

## Message Flow

```
API Request (POST /orders)
    |
    v
WebhookDispatcher              publishes to bus
    |
    v
+-------------------------+
|   RabbitMQ Exchange     |    WebhookDispatched message
+-------------------------+
    |
    v
WebhookDispatchedConsumer      fan-out: 1 event -> N subscriptions
    |
    v  (one message per matching subscription)
+-------------------------+
|   RabbitMQ Exchange     |    WebhookTriggered message
+-------------------------+
    |
    v
WebhookTriggerConsumer         delivers HTTP POST + logs attempt to DB
```

## Stage 1: WebhookDispatched

**Publisher:** `WebhookDispatcher` (`Services/WebhookDispatcher.cs`)

When an order is created, the API endpoint calls `WebhookDispatcher.DispatchAsync()`. This publishes a single `WebhookDispatched` message to RabbitMQ containing:

- `EventType` — e.g., `"order.created"`
- `Data` — the serialized order object

The publish call returns immediately. The API response is sent back to the client without waiting for any webhook delivery.

**Consumer:** `WebhookDispatchedConsumer` (`Services/WebhookDispatchedConsumer.cs`)

This consumer picks up the `WebhookDispatched` message and:

1. Queries PostgreSQL for all subscriptions matching the event type
2. Publishes a separate `WebhookTriggered` message for **each** matching subscription

This is the **fan-out** stage — one event can result in many webhook deliveries.

## Stage 2: WebhookTriggered

**Message:** `WebhookTriggered` (`Services/WebhookTriggered.cs`)

Each message contains everything needed for delivery:

- `SubscriptionId` — which subscription this delivery is for
- `EventType` — the event type string
- `WebhookUrl` — the subscriber's endpoint URL
- `Data` — the payload to deliver

**Consumer:** `WebhookTriggerConsumer` (`Services/WebhookTriggerConsumer.cs`)

This consumer:

1. Builds a `WebhookPayload` with a unique ID, timestamp, and the event data
2. Makes an HTTP POST to the subscriber's URL with the JSON payload
3. Records a `WebhookDeliveryAttempt` in PostgreSQL (success or failure)
4. On failure, logs the error via `ILogger`

## Why Two Stages?

| Benefit | Explanation |
|---------|-------------|
| **Decoupling** | The API endpoint returns instantly. Webhook delivery happens asynchronously in the background. |
| **Independent scaling** | The fan-out stage (DB query) and delivery stage (HTTP calls) can be scaled separately. |
| **Resilience** | If delivery fails, the message stays in RabbitMQ. Retry policies handle transient failures automatically. |
| **Audit trail** | Every delivery attempt is logged to the database with status codes and timestamps. |

## MassTransit Configuration

Configured in `Program.cs`:

```csharp
builder.Services.AddMassTransit(busConfig =>
{
    busConfig.SetKebabCaseEndpointNameFormatter();
    busConfig.AddConsumer<WebhookDispatchedConsumer>();
    busConfig.AddConsumer<WebhookTriggerConsumer>();
    busConfig.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("rabbitmq"));
        cfg.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15)));
        cfg.ConfigureEndpoints(context);
    });
});
```

### What each line does

- **`SetKebabCaseEndpointNameFormatter()`** — Queue names are auto-generated from consumer class names in kebab-case (e.g., `webhook-dispatched-consumer`).
- **`AddConsumer<T>()`** — Registers each consumer. MassTransit creates a queue and binds it to the appropriate exchange for each one.
- **`UsingRabbitMq`** — Configures RabbitMQ as the transport. The connection string comes from Aspire service discovery.
- **`UseMessageRetry`** — Retries failed messages 3 times at increasing intervals (1s, 5s, 15s) before moving the message to an error queue (`_error` suffix).
- **`ConfigureEndpoints`** — Auto-wires all registered consumers to their queues.

## Aspire Orchestration

Configured in `AppHost.cs`:

```csharp
var queue = builder.AddRabbitMQ("rabbitmq")
                   .WithDataVolume("rabbitmq-data")
                   .WithManagementPlugin();

builder.AddProject<Projects.Webhooks_Api_http>("webhooks-api-http")
       .WithReference(queue)
       .WaitFor(queue);
```

- RabbitMQ is declared as an infrastructure resource with a **persistent data volume**
- The **management plugin** is enabled, providing a web UI at port `15672` (default credentials: `guest`/`guest`)
- `.WaitFor(queue)` ensures the API project doesn't start until RabbitMQ is healthy
- Aspire automatically injects the RabbitMQ connection string into the API project's configuration

## RabbitMQ Queues at Runtime

When the application starts, MassTransit automatically creates these queues in RabbitMQ:

| Queue | Consumer | Purpose |
|-------|----------|---------|
| `webhook-dispatched-consumer` | `WebhookDispatchedConsumer` | Receives dispatched events, fans out to subscriptions |
| `webhook-trigger-consumer` | `WebhookTriggerConsumer` | Receives per-subscription triggers, delivers HTTP POST |
| `webhook-dispatched-consumer_error` | (none) | Dead-letter queue for failed dispatched messages |
| `webhook-trigger-consumer_error` | (none) | Dead-letter queue for failed trigger messages |

## OpenTelemetry Tracing

The messaging pipeline is fully traced:

```csharp
builder.Services
    .AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource(DiagnosticConfig.source.Name)               // custom "webhooks-api" traces
               .AddSource(MassTransit.Logging.DiagnosticHeaders.DefaultListenerName)  // MassTransit traces
               .AddNpgsql();                                          // PostgreSQL query traces
    });
```

- `WebhookDispatcher` creates a custom Activity span for each dispatch
- MassTransit automatically creates spans for publish, send, and consume operations
- Traces flow through both stages, giving end-to-end visibility in the Aspire dashboard
