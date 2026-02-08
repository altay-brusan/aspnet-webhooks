using MassTransit;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Webhooks.Api.http.Data;
using Webhooks.Api.http.Extensions;
using Webhooks.Api.http.Models;
using Webhooks.Api.http.OpenTelemetry;
using Webhooks.Api.http.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Register IHttpClientFactory and the dispatcher as a scoped service
builder.Services.AddHttpClient();                 // provides IHttpClientFactory
//This line is different from the original content!
builder.Services.AddScoped<WebhookDispatcher>();  // scoped because it depends on DbContext

builder.Services.AddDbContext<WebhooksDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("webhooks")));

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

builder.Services
    .AddOpenTelemetry()
    .WithTracing(
    tracing => 
    {
        tracing.AddSource(DiagnosticConfig.source.Name)
               .AddSource(MassTransit.Logging.DiagnosticHeaders.DefaultListenerName)
               .AddNpgsql();
    });


var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "OpenAPI V1");
    });
    await app.ApplyMigrationAsync();
}

app.UseHttpsRedirection();
app.MapPost("webhooks/subscription", async (CreateWebhookRequest request, WebhooksDbContext dbContext) =>
{
    if (string.IsNullOrWhiteSpace(request.EventType))
        return Results.BadRequest("EventType is required.");

    if (!Uri.TryCreate(request.WebhookUrl, UriKind.Absolute, out var uri)
        || (uri.Scheme != "http" && uri.Scheme != "https"))
        return Results.BadRequest("WebhookUrl must be a valid absolute HTTP(S) URL.");

    WebhookSubscription subscription = new(
        Guid.NewGuid(),
        request.EventType,
        request.WebhookUrl,
        DateTime.UtcNow);
    dbContext.WebhookSubscriptions.Add(subscription);
    await dbContext.SaveChangesAsync();
    return Results.Ok(subscription);
});

app.MapPost("/orders", async (CreateOrderRequest request, WebhooksDbContext dbContext, WebhookDispatcher dispatcher) =>
{
    if (string.IsNullOrWhiteSpace(request.CustomerName))
        return Results.BadRequest("CustomerName is required.");

    if (request.Amount <= 0)
        return Results.BadRequest("Amount must be greater than zero.");

    var order = new Order(
        Guid.NewGuid(),
        request.CustomerName,
        request.Amount,
        DateTime.UtcNow);
    dbContext.Add(order);
    await dbContext.SaveChangesAsync();
    await dispatcher.DispatchAsync("order.created", order);
    return Results.Ok(order);
}).WithTags("Orders");

app.MapGet("/orders", async (WebhooksDbContext dbContext) => {
    return Results.Ok(await dbContext.Orders.ToListAsync());
}).WithTags("Orders");

app.Run();