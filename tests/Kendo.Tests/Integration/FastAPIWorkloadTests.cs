using System.Net;
using System.Text.Json;
using Kendo.Shared.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Kendo.Tests.Integration;

[Trait("Category", "FastAPIW1")]
public class FastAPIWorkloadTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static IOptions<FastApiOptions> CreateOptions(
        int timeoutSeconds = 30,
        int cbFailures = 3,
        int cbBreakSeconds = 30)
    {
        return Options.Create(new FastApiOptions
        {
            BaseUrl = "http://fastapi:8000",
            TimeoutSeconds = timeoutSeconds,
            CircuitBreakerFailures = cbFailures,
            CircuitBreakerBreakSeconds = cbBreakSeconds
        });
    }

    private static Mock<HttpMessageHandler> CreateMockHandler(
        HttpStatusCode statusCode,
        object? responseBody = null)
    {
        var mock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        mock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = responseBody is not null
                    ? new StringContent(JsonSerializer.Serialize(responseBody, JsonOptions))
                    : new StringContent("")
            });

        return mock;
    }

    // ========================================================================
    // W1 — IngestEventAsync
    // ========================================================================

    [Fact]
    public async Task IngestEventAsync_ValidRequest_ReturnsStructuredEvent()
    {
        // Arrange
        var expected = new EventIngestionResult(
            EventId: "evt-001",
            StructuredJson: """{"name":"Birthday Party","date":"2026-06-25"}""");

        var handler = CreateMockHandler(HttpStatusCode.OK, expected);
        var http = new HttpClient(handler.Object);
        var client = new FastAPIClient(
            http, CreateOptions(), new FastApiExceptionMapper(),
            Mock.Of<ILogger<FastAPIClient>>());

        // Act
        var result = await client.IngestEventAsync(
            new EventIngestionRequest("Birthday party at 7pm"), CancellationToken.None);

        // Assert
        Assert.Equal(expected.EventId, result.EventId);
        Assert.Equal(expected.StructuredJson, result.StructuredJson);
    }

    [Fact]
    public async Task IngestEventAsync_FastApi503_AfterRetriesThrows()
    {
        // Arrange
        var problemResponse = new
        {
            type = "about:blank",
            title = "Service Unavailable",
            status = 503,
            detail = "upstream unavailable",
            trace_id = "trace-503"
        };

        // Must return fresh response on each call to survive Polly retries
        var json = JsonSerializer.Serialize(problemResponse, JsonOptions);
        var mock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        mock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(() => Task.FromResult(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.ServiceUnavailable,
                Content = new StringContent(json)
            }));

        var httpClient = new HttpClient(mock.Object) { BaseAddress = new Uri("http://fastapi:8000") };
        var client = new FastAPIClient(httpClient, CreateOptions(), new FastApiExceptionMapper(), Mock.Of<ILogger<FastAPIClient>>());

        // Act & Assert
        // After 3 retries + circuit breaker opens, FastApiClientException is thrown
        var ex = await Assert.ThrowsAsync<FastApiClientException>(() =>
            client.IngestEventAsync(
                new EventIngestionRequest("Test event"), CancellationToken.None));

        Assert.Contains("Service Unavailable", ex.Message);
    }

    [Fact]
    public async Task IngestEventAsync_EmptyText_Throws()
    {
        // Arrange
        var handler = CreateMockHandler(HttpStatusCode.BadRequest);
        var http = new HttpClient(handler.Object);
        var client = new FastAPIClient(
            http, CreateOptions(), new FastApiExceptionMapper(),
            Mock.Of<ILogger<FastAPIClient>>());

        // Act & Assert
        // FastAPIClient calls EnsureSuccessStatusCode which throws on non-2xx
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.IngestEventAsync(
                new EventIngestionRequest(""), CancellationToken.None));
    }

    // ========================================================================
    // W1 — IngestEventStreamAsync
    // ========================================================================

    [Fact]
    public async Task IngestEventStreamAsync_ValidRequest_YieldsChunks()
    {
        // Arrange
        var chunks = new[]
        {
            """{"Token":"Structured","IsFinal":false}""",
            """{"Token":"Event","IsFinal":false}""",
            """{"Token":" complete","IsFinal":true}"""
        };
        var streamContent = string.Join("\n", chunks);

        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(streamContent)
            });

        var http = new HttpClient(handler.Object);
        var client = new FastAPIClient(
            http, CreateOptions(), new FastApiExceptionMapper(),
            Mock.Of<ILogger<FastAPIClient>>());

        var received = new List<EventIngestionStreamChunk>();

        // Act
        await foreach (var chunk in client.IngestEventStreamAsync(
            new EventIngestionRequest("Party"), CancellationToken.None))
        {
            received.Add(chunk);
        }

        // Assert
        Assert.Equal(3, received.Count);
        Assert.False(received[0].IsFinal);
        Assert.True(received[^1].IsFinal);
    }

    [Fact]
    public async Task IngestEventStreamAsync_CircuitBreakerOpen_Throws()
    {
        // Arrange
        // Simulate a circuit breaker by making the handler throw
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var http = new HttpClient(handler.Object);
        var client = new FastAPIClient(
            http, CreateOptions(timeoutSeconds: 1),
            new FastApiExceptionMapper(),
            Mock.Of<ILogger<FastAPIClient>>());

        // Act & Assert
        var ex = await Assert.ThrowsAsync<FastApiClientException>(() =>
        {
            var enumerable = client.IngestEventStreamAsync(
                new EventIngestionRequest("Test"), CancellationToken.None);
            return enumerable.GetAsyncEnumerator().MoveNextAsync().AsTask();
        });

        Assert.Contains("Service Unavailable", ex.Message);
    }

    // ========================================================================
    // W3 — SearchUsersAsync
    // ========================================================================

    [Fact]
    [Trait("Category", "FastAPIW3")]
    public async Task SearchUsersAsync_ValidQuery_ReturnsResults()
    {
        // Arrange
        var expected = new UserSearchResult(
            UserIds: ["user-001", "user-002"],
            Relevance: [0.92, 0.85]);

        var handler = CreateMockHandler(HttpStatusCode.OK, expected);
        var http = new HttpClient(handler.Object);
        var client = new FastAPIClient(
            http, CreateOptions(), new FastApiExceptionMapper(),
            Mock.Of<ILogger<FastAPIClient>>());

        // Act
        var result = await client.SearchUsersAsync(
            new UserSearchRequest("developer"), CancellationToken.None);

        // Assert
        Assert.Equal(2, result.UserIds.Length);
        Assert.Equal("user-001", result.UserIds[0]);
        Assert.Equal(0.92, result.Relevance[0]);
    }

    [Fact]
    [Trait("Category", "FastAPIW3")]
    public async Task SearchUsersAsync_FastApi503_AfterRetriesThrows()
    {
        // Arrange
        var problemResponse = new
        {
            type = "about:blank",
            title = "Service Unavailable",
            status = 503,
            detail = "search unavailable",
            trace_id = "trace-503"
        };

        var json = JsonSerializer.Serialize(problemResponse, JsonOptions);
        var mock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        mock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(() => Task.FromResult(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.ServiceUnavailable,
                Content = new StringContent(json)
            }));

        var httpClient = new HttpClient(mock.Object) { BaseAddress = new Uri("http://fastapi:8000") };
        var client = new FastAPIClient(
            httpClient, CreateOptions(), new FastApiExceptionMapper(),
            Mock.Of<ILogger<FastAPIClient>>());

        // Act & Assert
        var ex = await Assert.ThrowsAsync<FastApiClientException>(() =>
            client.SearchUsersAsync(
                new UserSearchRequest("developer"), CancellationToken.None));

        Assert.Contains("Service Unavailable", ex.Message);
    }

    [Fact]
    [Trait("Category", "FastAPIW3")]
    public async Task SearchUsersAsync_EmptyQuery_ReturnsResults()
    {
        // Arrange
        var expected = new UserSearchResult(
            UserIds: Array.Empty<string>(),
            Relevance: Array.Empty<double>());

        var handler = CreateMockHandler(HttpStatusCode.OK, expected);
        var http = new HttpClient(handler.Object);
        var client = new FastAPIClient(
            http, CreateOptions(), new FastApiExceptionMapper(),
            Mock.Of<ILogger<FastAPIClient>>());

        // Act
        var result = await client.SearchUsersAsync(
            new UserSearchRequest("zzz_unknown"), CancellationToken.None);

        // Assert
        Assert.Empty(result.UserIds);
        Assert.Empty(result.Relevance);
    }
}
