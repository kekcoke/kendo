using System.Net;
using System.Text.Json;
using Kendo.Shared.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Kendo.Tests.Http;

[Trait("Category", "Notification")]
public class NotificationSummarizationClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static NotificationSummarizationOptions CreateOptions(
        int timeoutSeconds = 30,
        int cbFailures = 3,
        int cbBreakSeconds = 30)
    {
        return new NotificationSummarizationOptions
        {
            BaseUrl = "http://fastapi:8000",
            TimeoutSeconds = timeoutSeconds,
            CircuitBreakerFailures = cbFailures,
            CircuitBreakerBreakSeconds = cbBreakSeconds
        };
    }

    private static string BuildSseEvent(string eventType, object data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        return $"event: {eventType}\ndata: {json}\n\n";
    }

    private static string BuildSseStream(params (string eventType, object data)[] events)
    {
        return string.Join("", events.Select(e => BuildSseEvent(e.eventType, e.data)));
    }

    [Fact]
    public async Task SummarizeStreamAsync_ParsesChunkEvents()
    {
        // Arrange
        var chunk1 = new { text = "Hello ", token_count = 1 };
        var chunk2 = new { text = "world!", token_count = 2 };
        var doneData = new
        {
            notification_body = "Hello world!",
            prompt_version = "w7-notification-v1",
            total_tokens = 2,
            trace_id = "trace-123"
        };

        var sseBody = BuildSseStream(
            ("chunk", chunk1),
            ("chunk", chunk2),
            ("done", doneData)
        );

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(sseBody)
            });

        var httpClient = new HttpClient(handlerMock.Object);
        httpClient.BaseAddress = new Uri("http://fastapi:8000");

        var options = Options.Create(CreateOptions());
        var logger = Mock.Of<ILogger<NotificationSummarizationClient>>();
        var client = new NotificationSummarizationClient(httpClient, options, logger);

        var request = new NotificationSummarizationRequest
        {
            EventId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TemplateId = "event.confirmation",
            Tone = "friendly"
        };

        // Act
        var results = new List<NotificationSummarizationChunk>();
        await foreach (var chunk in client.SummarizeStreamAsync(request, CancellationToken.None))
        {
            results.Add(chunk);
        }

        // Assert
        Assert.Equal(3, results.Count);

        // First chunk
        Assert.Equal("chunk", results[0].Event);
        Assert.Equal("Hello ", results[0].Text);
        Assert.Equal(1, results[0].TokenCount);

        // Second chunk
        Assert.Equal("chunk", results[1].Event);
        Assert.Equal("world!", results[1].Text);
        Assert.Equal(2, results[1].TokenCount);

        // Done event
        Assert.Equal("done", results[2].Event);
        Assert.Equal("Hello world!", results[2].NotificationBody);
        Assert.Equal("w7-notification-v1", results[2].PromptVersion);
        Assert.Equal(2, results[2].TotalTokens);
        Assert.Equal("trace-123", results[2].TraceId);
    }

    [Fact]
    public async Task SummarizeStreamAsync_ParsesErrorEvent()
    {
        // Arrange
        var errorData = new
        {
            type = "about:blank",
            title = "Summarization Failed",
            status = 503,
            detail = "Azure OpenAI circuit breaker open"
        };

        var sseBody = BuildSseEvent("error", errorData);

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(sseBody)
            });

        var httpClient = new HttpClient(handlerMock.Object);
        httpClient.BaseAddress = new Uri("http://fastapi:8000");

        var client = new NotificationSummarizationClient(
            httpClient,
            Options.Create(CreateOptions()),
            Mock.Of<ILogger<NotificationSummarizationClient>>());

        var request = new NotificationSummarizationRequest
        {
            EventId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Tone = "friendly"
        };

        // Act
        var results = new List<NotificationSummarizationChunk>();
        await foreach (var chunk in client.SummarizeStreamAsync(request, CancellationToken.None))
        {
            results.Add(chunk);
        }

        // Assert
        Assert.Single(results);
        Assert.Equal("error", results[0].Event);
        Assert.Equal("Summarization Failed", results[0].Title);
        Assert.Equal(503, results[0].Status);
        Assert.Equal("Azure OpenAI circuit breaker open", results[0].Detail);
    }

    [Fact]
    public async Task SummarizeStreamAsync_HandlesEmptyStream()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("")
            });

        var httpClient = new HttpClient(handlerMock.Object);
        httpClient.BaseAddress = new Uri("http://fastapi:8000");

        var client = new NotificationSummarizationClient(
            httpClient,
            Options.Create(CreateOptions()),
            Mock.Of<ILogger<NotificationSummarizationClient>>());

        var request = new NotificationSummarizationRequest
        {
            EventId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Tone = "friendly"
        };

        // Act
        var results = new List<NotificationSummarizationChunk>();
        await foreach (var chunk in client.SummarizeStreamAsync(request, CancellationToken.None))
        {
            results.Add(chunk);
        }

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SummarizeStreamAsync_DoesNotYieldOnBlankLines()
    {
        // Arrange
        var sseBody = "\n\n\n"; // Only blank lines
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(sseBody)
            });

        var httpClient = new HttpClient(handlerMock.Object);
        httpClient.BaseAddress = new Uri("http://fastapi:8000");

        var client = new NotificationSummarizationClient(
            httpClient,
            Options.Create(CreateOptions()),
            Mock.Of<ILogger<NotificationSummarizationClient>>());

        var request = new NotificationSummarizationRequest
        {
            EventId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Tone = "friendly"
        };

        // Act
        var results = new List<NotificationSummarizationChunk>();
        await foreach (var chunk in client.SummarizeStreamAsync(request, CancellationToken.None))
        {
            results.Add(chunk);
        }

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SummarizeStreamAsync_ServerErrorTriggersRetry()
    {
        // Arrange — return 503 each time (factory to avoid ObjectDisposedException on retry)
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(() => Task.FromResult(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.ServiceUnavailable,
                Content = new ByteArrayContent([])
            }));

        var httpClient = new HttpClient(handlerMock.Object);
        httpClient.BaseAddress = new Uri("http://fastapi:8000");

        var options = Options.Create(CreateOptions(timeoutSeconds: 5));
        var logger = Mock.Of<ILogger<NotificationSummarizationClient>>();
        var client = new NotificationSummarizationClient(httpClient, options, logger);

        var request = new NotificationSummarizationRequest
        {
            EventId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Tone = "friendly"
        };

        // Act & Assert — after 3 retries, CB opens and throws NotificationSummarizationClientException
        var ex = await Assert.ThrowsAsync<NotificationSummarizationClientException>(async () =>
        {
            await foreach (var _ in client.SummarizeStreamAsync(request, CancellationToken.None))
            {
            }
        });
        Assert.Equal(503, ex.StatusCode);
        Assert.Equal("Service Unavailable", ex.ErrorTitle);
    }

    [Fact]
    public async Task NotificationSummarizationClientException_ContainsErrorData()
    {
        // Arrange & Act
        var ex = new NotificationSummarizationClientException(
            "Service Unavailable",
            "Circuit breaker is open",
            503);

        // Assert
        Assert.Equal(503, ex.StatusCode);
        Assert.Equal("Service Unavailable", ex.ErrorTitle);
        Assert.Equal("Circuit breaker is open", ex.Message);
    }
}
