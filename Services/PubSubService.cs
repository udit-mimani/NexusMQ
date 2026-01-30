using PlivoPubSub.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PlivoPubSub.Services;

public interface IPubSubService
{
    bool CreateTopic(string name);
    bool DeleteTopic(string name);
    List<string> GetTopics();
    TopicStats? GetTopicStats(string name);
    Dictionary<string, object> GetGlobalStats();

    Task HandleClientAsync(WebSocket webSocket);
}

public class PubSubService : IPubSubService
{
    // Thread-safe dictionary for topics. Key: Topic Name
    private readonly ConcurrentDictionary<string, TopicState> _topics = new();
    private readonly ILogger<PubSubService> _logger;

    public PubSubService(ILogger<PubSubService> logger)
    {
        _logger = logger;
    }

    public bool CreateTopic(string name)
    {
        return _topics.TryAdd(name, new TopicState(name));
    }

    public bool DeleteTopic(string name)
    {
        if (_topics.TryRemove(name, out var topicState))
        {
            topicState.Close(); // Disconnect all subscribers
            return true;
        }
        return false;
    }

    public List<string> GetTopics() => _topics.Keys.ToList();

    public TopicStats? GetTopicStats(string name)
    {
        if (_topics.TryGetValue(name, out var topic))
        {
            return new TopicStats
            {
                MessagesCount = topic.MessageCount,
                SubscriberCount = topic.Subscribers.Count
            };
        }
        return null;
    }

    public Dictionary<string, object> GetGlobalStats()
    {
        return new Dictionary<string, object>
        {
            { "uptime_sec",  (DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds },
            { "topics", _topics.Count },
            { "subscribers", _topics.Values.Sum(t => t.Subscribers.Count) }
        };
    }

    public async Task HandleClientAsync(WebSocket webSocket)
    {
        var buffer = new byte[1024 * 4];
        var handlerId = Guid.NewGuid().ToString();

        // Track subscriptions for this specific connection so we can cleanup on disconnect
        var mySubscriptions = new List<string>();

        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    break;
                }

                var messageJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
                ClientMessage? msg;
                try
                {
                    msg = JsonSerializer.Deserialize<ClientMessage>(messageJson);
                }
                catch
                {
                    await SendErrorAsync(webSocket, null, "BAD_REQUEST", "Invalid JSON");
                    continue;
                }

                if (msg == null) continue;

                switch (msg.Type)
                {
                    case "ping":
                        await SendJsonAsync(webSocket, new ServerMessage { Type = "pong", RequestId = msg.RequestId });
                        break;

                    case "subscribe":
                        if (string.IsNullOrEmpty(msg.Topic))
                        {
                            await SendErrorAsync(webSocket, msg.RequestId, "BAD_REQUEST", "Topic required");
                            break;
                        }
                        if (!_topics.ContainsKey(msg.Topic))
                        {
                            await SendErrorAsync(webSocket, msg.RequestId, "TOPIC_NOT_FOUND", "Topic does not exist");
                            break;
                        }

                        var topic = _topics[msg.Topic];
                        var sub = new Subscriber(msg.ClientId ?? handlerId, webSocket);

                        if (topic.Subscribers.TryAdd(sub.Id, sub))
                        {
                            mySubscriptions.Add(msg.Topic);
                            // Start a background task to push messages to this socket
                            _ = ProcessSubscriptionAsync(sub, topic, webSocket);
                        }

                        await SendJsonAsync(webSocket, new ServerMessage { Type = "ack", RequestId = msg.RequestId, Topic = msg.Topic, Status = "ok" });
                        break;

                    case "publish":
                        if (string.IsNullOrEmpty(msg.Topic) || msg.Message == null)
                        {
                            await SendErrorAsync(webSocket, msg.RequestId, "BAD_REQUEST", "Topic and Message required");
                            break;
                        }
                        if (!_topics.TryGetValue(msg.Topic, out var pubTopic))
                        {
                            await SendErrorAsync(webSocket, msg.RequestId, "TOPIC_NOT_FOUND", "Topic does not exist");
                            break;
                        }

                        // Broadcast
                        await pubTopic.BroadcastAsync(msg.Message);
                        await SendJsonAsync(webSocket, new ServerMessage { Type = "ack", RequestId = msg.RequestId, Topic = msg.Topic, Status = "ok" });
                        break;

                    case "unsubscribe":
                        // Logic to remove subscriber from topic
                        if (!string.IsNullOrEmpty(msg.Topic) && _topics.TryGetValue(msg.Topic, out var unsubTopic))
                        {
                            // Simple removal logic (in production, need robust mapping)
                            // For this demo, we rely on the specific instance management or ID
                            unsubTopic.RemoveSubscriber(msg.ClientId ?? handlerId);
                            mySubscriptions.Remove(msg.Topic);
                            await SendJsonAsync(webSocket, new ServerMessage { Type = "ack", RequestId = msg.RequestId, Topic = msg.Topic, Status = "ok" });
                        }
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebSocket error");
        }
        finally
        {
            // Cleanup subscriptions
            foreach (var topicName in mySubscriptions)
            {
                if (_topics.TryGetValue(topicName, out var t))
                {
                    t.RemoveSubscriber(handlerId);
                }
            }
        }
    }

    private async Task ProcessSubscriptionAsync(Subscriber sub, TopicState topic, WebSocket ws)
    {
        try
        {
            await foreach (var msg in sub.Channel.Reader.ReadAllAsync())
            {
                if (ws.State != WebSocketState.Open) break;

                var eventMsg = new ServerMessage
                {
                    Type = "event",
                    Topic = topic.Name,
                    Message = msg
                };
                await SendJsonAsync(ws, eventMsg);
            }
        }
        catch (ChannelClosedException) { } // Channel closed (slow consumer or disconnect)
    }

    private async Task SendJsonAsync(WebSocket ws, ServerMessage msg)
    {
        if (ws.State != WebSocketState.Open) return;
        var json = JsonSerializer.Serialize(msg);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task SendErrorAsync(WebSocket ws, string? reqId, string code, string message)
    {
        await SendJsonAsync(ws, new ServerMessage
        {
            Type = "error",
            RequestId = reqId,
            Error = new ErrorPayload { Code = code, Message = message }
        });
    }
}

// Helper classes for internal state
public class TopicState
{
    public string Name { get; }
    public ConcurrentDictionary<string, Subscriber> Subscribers { get; } = new();
    private long _messageCount = 0;
    public long MessageCount => Interlocked.Read(ref _messageCount);

    public TopicState(string name) { Name = name; }

    public async Task BroadcastAsync(MessagePayload payload)
    {
        Interlocked.Increment(ref _messageCount);
        foreach (var sub in Subscribers.Values)
        {
            // Non-blocking write. If full, we drop (Backpressure policy: Drop Oldest/Newest or Close)
            // Here we use TryWrite. If false, channel is full (Slow Consumer).
            if (!sub.Channel.Writer.TryWrite(payload))
            {
                // Option: Close connection or Drop message. 
                // Assignment says "Overflow drop oldest OR disconnect". Let's drop message for simplicity.
            }
        }
    }

    public void RemoveSubscriber(string id)
    {
        if (Subscribers.TryRemove(id, out var sub))
        {
            sub.Channel.Writer.TryComplete();
        }
    }

    public void Close()
    {
        foreach (var sub in Subscribers.Values)
        {
            sub.Channel.Writer.TryComplete();
        }
        Subscribers.Clear();
    }
}

public class Subscriber
{
    public string Id { get; }
    public WebSocket Socket { get; }
    [cite_start]// Bounded channel for backpressure [cite: 206]
    public Channel<MessagePayload> Channel { get; }

    public Subscriber(string id, WebSocket socket)
    {
        Id = id;
        Socket = socket;
        // Bounded capacity of 50 messages
        Channel = System.Threading.Channels.Channel.CreateBounded<MessagePayload>(new BoundedChannelOptions(50)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }
}

public class TopicStats
{
    public long MessagesCount { get; set; }
    public int SubscriberCount { get; set; }
}