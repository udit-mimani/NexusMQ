using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models; // REQUIRED for OpenApiInfo
using PlivoPubSub.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<IPubSubService, PubSubService>();

// Swagger Configuration
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Plivo Pub/Sub API",
        Version = "v1",
        Description = "In-memory Pub/Sub system with WebSocket and REST interfaces"
    });
});

var app = builder.Build();

// Enable Swagger UI
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Plivo Pub/Sub API v1");
    c.RoutePrefix = "swagger";
});

app.UseWebSockets();

// WebSocket Endpoint
app.Map("/ws", async (HttpContext context, IPubSubService pubSub) =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await pubSub.HandleClientAsync(webSocket);
    }
    else
    {
        context.Response.StatusCode = 400;
    }
});

// REST APIs
app.MapPost("/topics", ([FromBody] CreateTopicRequest req, IPubSubService pubSub) =>
{
    if (pubSub.CreateTopic(req.Name))
        return Results.Created($"/topics/{req.Name}", new { status = "created", topic = req.Name });

    return Results.Conflict();
});

app.MapDelete("/topics/{name}", (string name, IPubSubService pubSub) =>
{
    if (pubSub.DeleteTopic(name))
        return Results.Ok(new { status = "deleted", topic = name });

    return Results.NotFound();
});

app.MapGet("/topics", (IPubSubService pubSub) =>
{
    var topics = pubSub.GetTopics().Select(name =>
    {
        var stats = pubSub.GetTopicStats(name);
        return new { name, subscribers = stats?.SubscriberCount ?? 0 };
    });
    return Results.Ok(new { topics });
});

app.MapGet("/health", (IPubSubService pubSub) =>
{
    return Results.Ok(pubSub.GetGlobalStats());
});

app.MapGet("/stats", (IPubSubService pubSub) =>
{
    var topics = pubSub.GetTopics().ToDictionary(
        name => name,
        name => {
            var s = pubSub.GetTopicStats(name);
            return new { messages = s?.MessagesCount ?? 0, subscribers = s?.SubscriberCount ?? 0 };
        }
    );
    return Results.Ok(new { topics });
});

app.Run();

public record CreateTopicRequest(string Name);