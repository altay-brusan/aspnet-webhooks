using Microsoft.EntityFrameworkCore;
using System.Threading.Channels;
using Webhooks.Api.http.Data;
using Webhooks.Api.http.Extentions;
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

builder.Services.AddHostedService<WebhookProcessor>();

builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddSource(DiagnosticConfig.source.Name));

builder.Services.AddSingleton(_ => { 
 return Channel.CreateBounded<WebhookDispatch>(new BoundedChannelOptions(100)
 {
     FullMode = BoundedChannelFullMode.Wait
 });
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
app.MapPost("webhooks/subscription", (CreateWebhookRequest request, WebhooksDbContext dbContext) =>
{
    WebhookSubscription subscription = new(
        Guid.NewGuid(),
        request.EventType,
        request.WebhookUrl,
        DateTime.UtcNow);
    dbContext.WebhookSubscriptions.Add(subscription);
    return Results.Ok(subscription);
});

app.MapPost("/orders", async (CreateOrderRequest request, WebhooksDbContext dbContext, WebhookDispatcher dispatcher) =>
{
    var order = new Order(
        Guid.NewGuid(),
        request.CustomerName,
        request.Amount,
        DateTime.UtcNow);
    dbContext.Add(order);
    await dispatcher.DispatchAsync("order.created", order);
    return Results.Ok(order);
}).WithTags("Orders");

app.MapGet("/orders", async (WebhooksDbContext dbContext) => {
    return Results.Ok(await dbContext.Orders.ToListAsync());
}).WithTags("Orders");

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}