using Kendo.Shared.ErrorHandling;
using Kendo.Shared.Http;
using Kendo.Shared.Messaging;
using Kendo.Shared.Observability;
using Kendo.Shared.Resilience;
using Kendo.Worker;
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
builder.Services.AddKendoObservability(builder.Configuration, "kendo-worker");
builder.Services.AddKendoErrorHandling();
builder.Services.AddKendoRebus(builder.Configuration, "consumer");

builder.Services.AddHttpClient("default")
    .AddHttpMessageHandler<ResilienceDelegatingHandler>();

builder.Services.AddHostedService<WorkerBackgroundService>();

var app = builder.Build();

app.UseKendoErrorHandling();

app.MapControllers();

app.Run();