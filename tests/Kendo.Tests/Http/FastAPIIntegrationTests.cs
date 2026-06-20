using System.Net;
using System.Text.Json;
using Kendo.Shared.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Kendo.Tests.Http;

[Trait("Category", "Unit")]
public class FastAPIIntegrationTests
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

    [Fact]
    public async Task RagQueryAsync_ValidRequest_ReturnsQueryResult()
    {
        // Arrange
        var expected = new RagQueryResult(
            Answer: "The event is a team meeting at 3pm.",
            Contexts: [new RagContext("evt-1", "Team meeting agenda", 0.92, "events")],
            TraceId: "trace-123"
        );

        var mockHandler = CreateMockHandler(HttpStatusCode.OK, expected);
        var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri("http://fastapi:8000") };
        var client = new FastAPIClient(httpClient, CreateOptions(), new FastApiExceptionMapper(), Mock.Of<ILogger<FastAPIClient>>());

        // Act
        var result = await client.RagQueryAsync(new RagQueryRequest("What meetings today?"), CancellationToken.None);

        // Assert
        Assert.Equal(expected.Answer, result.Answer);
        Assert.Single(result.Contexts);
        Assert.Equal("evt-1", result.Contexts[0].Id);
        Assert.Equal(0.92, result.Contexts[0].Score);
        Assert.Equal("trace-123", result.TraceId);
    }

    [Fact]
    public async Task RagQueryAsync_EmptyQuery_UsesEmptyQuery()
    {
        // Arrange
        var expected = new RagQueryResult(
            Answer: "No relevant context found for your query.",
            Contexts: [],
            TraceId: "trace-empty"
        );

        var mockHandler = CreateMockHandler(HttpStatusCode.OK, expected);
        var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri("http://fastapi:8000") };
        var client = new FastAPIClient(httpClient, CreateOptions(), new FastApiExceptionMapper(), Mock.Of<ILogger<FastAPIClient>>());

        // Act
        var result = await client.RagQueryAsync(new RagQueryRequest(""), CancellationToken.None);

        // Assert
        Assert.Empty(result.Contexts);
    }

    [Fact]
    public async Task RagQueryAsync_FastApiReturns503_ThrowsClientException()
    {
        // Arrange
        var problemResponse = new
        {
            type = "about:blank",
            title = "Service Unavailable",
            status = 503,
            detail = "pgvector not reachable",
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
        // Polly retry exhausts after 3 × 503 → circuit breaker opens →
        // ExecuteWithPipelineAsync catches BrokenCircuitException and wraps as FastApiClientException
        var ex = await Assert.ThrowsAsync<FastApiClientException>(
            () => client.RagQueryAsync(new RagQueryRequest("test"), CancellationToken.None));

        Assert.Equal(503, ex.Error.StatusCode);
        Assert.Contains("circuit breaker is open", ex.Error.Detail);
    }

    [Fact]
    public async Task RagQueryAsync_TopK_DefaultsToFive()
    {
        // Arrange
        var expected = new RagQueryResult(
            Answer: "test",
            Contexts: [],
            TraceId: "trace"
        );

        HttpRequestMessage? capturedRequest = null;
        var mock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        mock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(expected, JsonOptions))
            });

        var httpClient = new HttpClient(mock.Object) { BaseAddress = new Uri("http://fastapi:8000") };
        var client = new FastAPIClient(httpClient, CreateOptions(), new FastApiExceptionMapper(), Mock.Of<ILogger<FastAPIClient>>());

        // Act
        await client.RagQueryAsync(new RagQueryRequest("test"), CancellationToken.None);

        // Assert
        Assert.NotNull(capturedRequest);
        var body = await capturedRequest!.Content!.ReadAsStringAsync();
        var requestBody = JsonSerializer.Deserialize<RagQueryRequest>(body, JsonOptions);
        Assert.NotNull(requestBody);
        Assert.Equal(5, requestBody!.TopK);
    }

    [Fact]
    public async Task RagQueryStreamAsync_ReturnsChunks()
    {
        // Arrange
        var chunks = new[]
        {
            """{"type":"start","text":null,"traceId":"trace-s","isDone":false}""",
            """{"type":"token","text":"Hello","traceId":"trace-s","isDone":false}""",
            """{"type":"token","text":" world","traceId":"trace-s","isDone":false}""",
            """{"type":"done","text":null,"traceId":"trace-s","isDone":true}"""
        };

        var mock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        mock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(string.Join("\n", chunks))
            });

        var httpClient = new HttpClient(mock.Object) { BaseAddress = new Uri("http://fastapi:8000") };
        var client = new FastAPIClient(httpClient, CreateOptions(), new FastApiExceptionMapper(), Mock.Of<ILogger<FastAPIClient>>());

        // Act
        var results = new List<RagStreamChunk>();
        await foreach (var chunk in client.RagQueryStreamAsync(new RagQueryRequest("test"), CancellationToken.None))
        {
            results.Add(chunk);
        }

        // Assert
        Assert.Equal(4, results.Count);
        Assert.Equal("start", results[0].Type);
        Assert.Equal("Hello", results[1].Text);
        Assert.Equal(" world", results[2].Text);
        Assert.True(results[3].IsDone);
    }
}
