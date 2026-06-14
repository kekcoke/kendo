using System.Text.Json;

namespace Kendo.Shared.Messaging;

/// <summary>
/// Static helper for serializing and deserializing <c>KendoMessage</c> instances
/// for storage in the transactional outbox table.
/// </summary>
public static class KendoMessageSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Serializes a <c>KendoMessage</c> to its JSON payload for outbox storage.
    /// </summary>
    public static string Serialize(KendoMessage message)
    {
        return JsonSerializer.Serialize(message, message.GetType(), JsonOptions);
    }

    /// <summary>
    /// Returns the short-form assembly-qualified type name for the message
    /// (strips version, culture, and public-key token for forward compatibility).
    /// </summary>
    public static string GetMessageType(KendoMessage message)
    {
        var type = message.GetType();
        return $"{type.FullName}, {type.Assembly.GetName().Name}";
    }

    /// <summary>
    /// Deserializes a <c>KendoMessage</c> from its stored type name and JSON payload.
    /// Returns null if the type cannot be resolved or deserialization fails.
    /// </summary>
    public static KendoMessage? Deserialize(string messageType, string payload)
    {
        var type = Type.GetType(messageType);
        if (type is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize(payload, type, JsonOptions) as KendoMessage;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
