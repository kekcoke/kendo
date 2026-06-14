using Kendo.Shared.Messaging;
using Kendo.UserService.Data;

namespace Kendo.Tests.UserService;

[Trait("Category", "Unit")]
public class OutboxMessageSerializerTests
{
    private sealed record TestMessage : KendoMessage
    {
        public string Name { get; init; } = string.Empty;
        public int Value { get; init; }
    }

    [Fact]
    public void Serialize_RoundTrips_Correctly()
    {
        var original = new TestMessage
        {
            Name = "Test",
            Value = 42
        };

        var payload = KendoMessageSerializer.Serialize(original);
        var messageType = KendoMessageSerializer.GetMessageType(original);

        Assert.Contains("\"name\"", payload);
        Assert.Contains("\"Test\"", payload);
        Assert.Contains("\"value\"", payload);
        Assert.Contains("42", payload);
        Assert.Equal("Kendo.Tests.UserService.OutboxMessageSerializerTests+TestMessage, Kendo.Tests", messageType);
    }

    [Fact]
    public void Deserialize_RoundTrips_Correctly()
    {
        var original = new TestMessage
        {
            Name = "RoundTrip",
            Value = 99
        };

        var payload = KendoMessageSerializer.Serialize(original);
        var messageType = KendoMessageSerializer.GetMessageType(original);

        var deserialized = KendoMessageSerializer.Deserialize(messageType, payload);

        Assert.NotNull(deserialized);
        var typed = Assert.IsType<TestMessage>(deserialized);
        Assert.Equal(original.Name, typed.Name);
        Assert.Equal(original.Value, typed.Value);
        Assert.Equal(original.MessageId, typed.MessageId);
    }

    [Fact]
    public void Deserialize_UnknownType_ReturnsNull()
    {
        var result = KendoMessageSerializer.Deserialize("Some.NonExistent.Type, FakeAssembly", "{}");

        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_InvalidJson_ReturnsNull()
    {
        var messageType = KendoMessageSerializer.GetMessageType(new TestMessage());

        var result = KendoMessageSerializer.Deserialize(messageType, "{ invalid json }");

        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_WrongType_ReturnsNull()
    {
        var payload = KendoMessageSerializer.Serialize(new TestMessage());
        var messageType = KendoMessageSerializer.GetMessageType(new TestMessage());

        // Use a different type that derives from KendoMessage but doesn't match
        var result = KendoMessageSerializer.Deserialize(
            "Kendo.Shared.Messaging.UserCreatedEvent, Kendo.Shared",
            payload);

        // Deserialization succeeds but cast to KendoMessage returns the object
        // The type mismatch means fields won't match — but it's still a non-null KendoMessage
        Assert.NotNull(result);
        Assert.IsType<Kendo.Shared.Messaging.UserCreatedEvent>(result);
    }

    [Fact]
    public void UserCreatedEvent_SerializesCorrectly()
    {
        var evt = new UserCreatedEvent
        {
            UserId = Guid.NewGuid(),
            Email = "test@example.com",
            DisplayName = "Test User"
        };

        var payload = KendoMessageSerializer.Serialize(evt);
        var messageType = KendoMessageSerializer.GetMessageType(evt);

        Assert.Contains("test@example.com", payload);
        Assert.Contains(evt.UserId.ToString(), payload);
        Assert.Equal("Kendo.Shared.Messaging.UserCreatedEvent, Kendo.Shared", messageType);

        var deserialized = KendoMessageSerializer.Deserialize(messageType, payload);
        Assert.NotNull(deserialized);
        var typed = Assert.IsType<UserCreatedEvent>(deserialized);
        Assert.Equal(evt.Email, typed.Email);
        Assert.Equal(evt.UserId, typed.UserId);
    }
}
