using Kendo.Shared.Authentication;
using static Kendo.Shared.Authentication.AdminScopePoliciesExtensions;
using Kendo.Shared.Caching;
using Kendo.Shared.ErrorHandling;
using Kendo.Shared.Http;
using Kendo.Shared.Messaging;
using Kendo.Shared.Observability;
using Kendo.Shared.GracefulShutdown;
using Kendo.Shared.RateLimiting;
using Kendo.Shared.Resilience;
using Kendo.Gateway.Middleware;
using Kendo.Gateway.WellKnown;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
    options.SuppressModelStateInvalidFilter = true);

// JWT authentication
builder.Services.AddKendoJwt(builder.Configuration);

// Token issuance service (user JWT minting)
builder.Services.AddSingleton<ITokenService, TokenService>();

// Authorization policies (shared with day_20)
builder.Services.AddKendoAdminScopePolicies();

// FastAPI service client (W1-W7 workloads)
builder.Services.AddKendoFastApiClient(builder.Configuration);

builder.Services.AddKendoResilience(builder.Configuration);
builder.Services.AddKendoObservability(builder.Configuration, "kendo-gateway");
builder.Services.AddKendoDistributedCache(builder.Configuration);
builder.Services.AddKendoRateLimiting(builder.Configuration);
builder.Services.AddKendoErrorHandling();
builder.Services.AddKendoRebus(builder.Configuration, "producer");
builder.Services.AddKendoRebusAiProducer(builder.Configuration);
builder.Services.AddKendoGracefulShutdown(builder.Configuration);

builder.Services.AddHttpClient("default")
    .AddHttpMessageHandler<ResilienceDelegatingHandler>();

var app = builder.Build();

app.UseMiddleware<GracefulShutdownMiddleware>();
app.UseKendoLoadShedding();
app.UseKendoRateLimiter();
app.UseKendoErrorHandling();
app.UseKendoReplicaIdentity();

app.UseAuthentication();
app.UseAuthorization();

// W4 — Intent Advisory (fail-open; FastAPI outage → fallback)
app.UseMiddleware<IntentAdvisoryMiddleware>();

app.MapJwksEndpoint();
app.MapControllers();

app.Run();