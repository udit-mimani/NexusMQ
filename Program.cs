using System.Net.WebSockets;
using Microsoft.AspNetCore.Mvc;
using PlivoPubSub.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddSingleton<IPubSubService, PubSubService>();

var app = builder.Build();

// Enable WebSockets
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

[cite_start]// REST API: Create Topic [cite: 163]
app.MapPost("/topics", ([FromBody] CreateTopicRequest req, IPubSubService pubSub) =>
{
    if (pubSub.CreateTopic(req.Name))
        return Results.Created($"/topics/{req.Name}", new { status = "created", topic = req.Name });

    return Results.Conflict();
});

[cite_start]// REST API: Delete Topic [cite: 168]
app.MapDelete("/topics/{name}", (string name, IPubSubService pubSub) =>
{
    if (pubSub.DeleteTopic(name))
        return Results.Ok(new { status = "deleted", topic = name });

    return Results.NotFound();
});

[cite_start]// REST API: List Topics [cite: 172]
app.MapGet("/topics", (IPubSubService pubSub) =>
{
    var topics = pubSub.GetTopics().Select(name =>
    {
        var stats = pubSub.GetTopicStats(name);
        return new { name, subscribers = stats?.SubscriberCount ?? 0 };
    });
    return Results.Ok(new { topics });
});

[cite_start]// REST API: Health [cite: 183]
app.MapGet("/health", (IPubSubService pubSub) =>
{
    return Results.Ok(pubSub.GetGlobalStats());
});

[cite_start]// REST API: Stats [cite: 190]
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

// DTO for creation
public record CreateTopicRequest(string Name);