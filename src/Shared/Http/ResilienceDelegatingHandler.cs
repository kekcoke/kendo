using Kendo.Shared.Resilience;

namespace Kendo.Shared.Http;

/// <summary>
/// DelegatingHandler that wraps outgoing HTTP requests through the Polly Retry + Circuit Breaker pipeline.
/// Register via: services.AddHttpClient("name").AddHttpMessageHandler<ResilienceDelegatingHandler>();
/// </summary>
public class ResilienceDelegatingHandler : DelegatingHandler
{
    private readonly IResiliencePipeline _resilience;

    public ResilienceDelegatingHandler(IResiliencePipeline resilience)
    {
        _resilience = resilience;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return await _resilience.ExecuteAsync(
            async ct => await base.SendAsync(request, ct),
            cancellationToken);
    }
}
