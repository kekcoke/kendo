using System.Net;
using System.Text.Json;
using Kendo.Shared.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Kendo.Tests.Http;

[Trait("Category", "Http")]
public class FastAPISummarizationClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static FastApiSummarizationOptions CreateOptions(
        int timeoutSeconds = 15,
        int cbFailures = 3,
        int cbBreakSeconds = 30)
    {
        return new FastApiSummarizationOptions
        {
            BaseUrl = "http://fastapi:8000",
            TimeoutSeconds = timeoutSeconds,
            CircuitBreakerFailures = cbFailures,
            CircuitBreakerBreakSeconds = cbBreakSeconds
        };
    }

    [Fact]
    public async Task SummarizeStreamAsync_ParsesSseChunks()
    {
        // Arrange
        var chunks = new[]
        {
            new SummarizationStreamChunk { Text = "Hello ", PromptVersion = "v1" },
            new SummarizationStreamChunk { Text = "world!", PromptVersion = "v1" }
        };
        var sseBody = string.Join("\n", chunks.Select(c =>
            JsonSerializer.Serialize(c, JsonOptions))) + "\n";

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
        var logger = Mock.Of<ILogger<FastAPISummarizationClient>>();
        var client = new FastAPISummarizationClient(httpClient, options, logger);

        var request = new SummarizationRequest
        {
            EventId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TemplateId = "event.conflict",
            Tone = "neutral"
        };

        // Act
        var results = new List<SummarizationStreamChunk>();
        await foreach (var chunk in client.SummarizeStreamAsync(request, CancellationToken.None))
        {
            results.Add(chunk);
        }

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal("Hello ", results[0].Text);
        Assert.Equal("world!", results[1].Text);
        Assert.Equal("v1", results[0].PromptVersion);
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

        var client = new FastAPISummarizationClient(
            httpClient,
            Options.Create(CreateOptions()),
            Mock.Of<ILogger<FastAPISummarizationClient>>());

        var request = new SummarizationRequest
        {
            UserId = Guid.NewGuid(),
            TemplateId = "test",
            Tone = "neutral"
        };

        // Act
        var results = new List<SummarizationStreamChunk>();
        await foreach (var chunk in client.SummarizeStreamAsync(request, CancellationToken.None))
        {
            results.Add(chunk);
        }

        // Assert
        Assert.Empty(results);
    }
}
