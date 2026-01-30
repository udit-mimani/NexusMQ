using System;
using System.Text.Json.Serialization;

namespace PlivoPubSub.Models;

// Incoming message from Client
public class ClientMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // subscribe, unsubscribe, publish, ping

    [JsonPropertyName("topic")]
    public string? Topic { get; set; }

    [JsonPropertyName("client_id")]
    public string? ClientId { get; set; }

    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    [JsonPropertyName("message")]
    public MessagePayload? Message { get; set; }

    [JsonPropertyName("last_n")]
    public int? LastN { get; set; }
}

public class MessagePayload
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public object? Content { get; set; }
}

// Outgoing message to Client
public class ServerMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // ack, event, error, pong, info

    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    [JsonPropertyName("topic")]
    public string? Topic { get; set; }

    [JsonPropertyName("message")]
    public MessagePayload? Message { get; set; }

    [JsonPropertyName("error")]
    public ErrorPayload? Error { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("ts")]
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("O");

    [JsonPropertyName("msg")]
    public string? InfoMsg { get; set; }
}

public class ErrorPayload
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}