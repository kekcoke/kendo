using Kendo.Shared.Caching;
using Kendo.Shared.ErrorHandling;
using Kendo.Shared.Http;
using Kendo.Shared.Messaging;
using Kendo.Shared.Observability;
using Kendo.Shared.RateLimiting;
using Kendo.Shared.Resilience;
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

builder.Services.AddKendoResilience(builder.Configuration);
builder.Services.AddKendoObservability(builder.Configuration, "kendo-gateway");
builder.Services.AddKendoDistributedCache(builder.Configuration);
builder.Services.AddKendoRateLimiting(builder.Configuration);
builder.Services.AddKendoErrorHandling();
builder.Services.AddKendoRebus(builder.Configuration, "producer");

builder.Services.AddHttpClient("default")
    .AddHttpMessageHandler<ResilienceDelegatingHandler>();

var app = builder.Build();

app.UseKendoLoadShedding();
app.UseKendoRateLimiter();
app.UseKendoErrorHandling();
app.UseKendoReplicaIdentity();

app.MapControllers();

app.Run();