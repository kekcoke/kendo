using System.Net;
using System.Text.Json;
using Kendo.Shared.Http;
using Kendo.Worker.Data;
using Kendo.Worker.Handlers;
using Kendo.Worker.Models;
using Kendo.Worker.Workers;
using Kendo.Shared.Messaging.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace Kendo.Tests.Worker;

[Trait("Category", "Notification")]
public class NotificationSummarizationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static string BuildSseEvent(string eventType, object data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        return $"event: {eventType}\ndata: {json}\n\n";
    }

    /// <summary>
    /// Creates a NotificationRequestedHandler with a mock INotificationSummarizationClient
    /// and an in-memory database for idempotency testing.
    /// </summary>
    private static (
        NotificationRequestedHandler handler,
        WorkerDbContext db,
        NotificationDispatcherChannel channel) CreateHandler(
            Mock<INotificationSummarizationClient>? clientMock = null)
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new WorkerDbContext(options);
        db.Database.EnsureCreated();

        var resilientDb = new WorkerResilientDbContext(db);

        var summarizationClient = (clientMock ?? new Mock<INotificationSummarizationClient>()).Object;
        var channel = new NotificationDispatcherChannel();
        var logger = NullLogger<NotificationRequestedHandler>.Instance;

        var handler = new NotificationRequestedHandler(
            db, resilientDb, summarizationClient, channel, logger);

        return (handler, db, channel);
    }

    /// <summary>
    /// Creates a mock SSE response for the summarization client.
    /// Returns chunk events followed by a done event.
    /// </summary>
    private static Mock<INotificationSummarizationClient> CreateMockClientWithSseResponse(
        string[] chunkTexts,
        string promptVersion = "w7-notification-v1",
        int totalTokens = 10,
        string traceId = "trace-abc")
    {
        var mock = new Mock<INotificationSummarizationClient>();
        var chunks = new List<NotificationSummarizationChunk>();

        for (int i = 0; i < chunkTexts.Length; i++)
        {
            chunks.Add(new NotificationSummarizationChunk
            {
                Event = "chunk",
                Text = chunkTexts[i],
                TokenCount = i + 1
            });
        }

        chunks.Add(new NotificationSummarizationChunk
        {
            Event = "done",
            NotificationBody = string.Concat(chunkTexts),
            PromptVersion = promptVersion,
            TotalTokens = totalTokens,
            TraceId = traceId
        });

        mock.Setup(c => c.SummarizeStreamAsync(
                It.IsAny<NotificationSummarizationRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(chunks.ToAsyncEnumerable());

        return mock;
    }

    [Fact]
    public async Task Handle_HappyPath_EnqueuesNotification()
    {
        // Arrange
        var mock = CreateMockClientWithSseResponse(
            ["Hello ", "Bob, ", "your event is confirmed!"]);
        var (handler, _, channel) = CreateHandler(mock);
        var message = new NotificationRequestedEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "event.confirmation",
            "friendly",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "event");

        // Act
        await handler.Handle(message);

        // Assert — notification was enqueued
        var delivered = await channel.Reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(delivered);
        Assert.Equal(message.UserId, delivered.UserId);
        Assert.Equal(message.TemplateId, delivered.TemplateId);
        Assert.Contains("Hello Bob, your event is confirmed!", delivered.RenderedBody);
        Assert.Equal("w7-notification-v1", delivered.PromptVersion);
    }

    [Fact]
    public async Task Handle_ClientException_ThrowsForRebusDlq()
    {
        // Arrange
        var mock = new Mock<INotificationSummarizationClient>();
        mock.Setup(c => c.SummarizeStreamAsync(
                It.IsAny<NotificationSummarizationRequest>(),
                It.IsAny<CancellationToken>()))
            .Throws(new NotificationSummarizationClientException(
                "Service Unavailable", "Circuit breaker OPEN", 503));

        var (handler, _, _) = CreateHandler(mock);
        var message = new NotificationRequestedEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "event.confirmation",
            "friendly",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "event");

        // Act & Assert — exception is re-thrown so Rebus DLQs
        var ex = await Assert.ThrowsAsync<NotificationSummarizationClientException>(
            () => handler.Handle(message));
        Assert.Equal(503, ex.StatusCode);
        Assert.Equal("Service Unavailable", ex.ErrorTitle);
    }

    [Fact]
    public async Task Handle_SseStreamError_FallsBackToTemplate()
    {
        // Arrange — client throws a generic exception mid-stream
        var mock = new Mock<INotificationSummarizationClient>();
        var chunks = new List<NotificationSummarizationChunk>
        {
            new() { Event = "chunk", Text = "Partial ", TokenCount = 1 }
        };
        var asyncEnumerable = chunks.ToAsyncEnumerable()
            .Concat(GetErrorThrowingEnumerable());

        mock.Setup(c => c.SummarizeStreamAsync(
                It.IsAny<NotificationSummarizationRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(asyncEnumerable);

        var (handler, _, channel) = CreateHandler(mock);
        var message = new NotificationRequestedEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "event.test",
            "friendly",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "event");

        // Act
        await handler.Handle(message);

        // Assert — falls back to template, does NOT throw
        var delivered = await channel.Reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(delivered);
        Assert.Contains("[Template: event.test]", delivered.RenderedBody);
        Assert.Equal("template-fallback", delivered.PromptVersion);
    }

    private static async IAsyncEnumerable<NotificationSummarizationChunk> GetErrorThrowingEnumerable()
    {
        await Task.Yield();
        throw new InvalidOperationException("SSE stream connection lost");
    }

    [Fact]
    public async Task Handle_DuplicateMessage_SilentlyDiscards()
    {
        // Arrange — first call succeeds
        var mock = CreateMockClientWithSseResponse(["Hello!"]);
        var (handler, db, channel) = CreateHandler(mock);
        var message = new NotificationRequestedEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "event.test",
            "friendly",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "event");

        await handler.Handle(message);
        var firstDelivered = await channel.Reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(firstDelivered);

        // Act — same message delivered again
        await handler.Handle(message);

        // Assert — no second notification enqueued
        var hasSecond = await channel.Reader.WaitToReadAsync(TimeSpan.FromMilliseconds(100));
        Assert.False(hasSecond);
    }
}
