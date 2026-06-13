using Kendo.Shared.Http;
using Kendo.Shared.Observability;
using Kendo.Shared.Resilience;
using Kendo.Worker;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHostedService<WorkerBackgroundService>();

builder.Services.AddKendoResilience(builder.Configuration);
builder.Services.AddKendoObservability(builder.Configuration, "kendo-worker");

builder.Services.AddHttpClient("default")
    .AddHttpMessageHandler<ResilienceDelegatingHandler>();

var app = builder.Build();

app.MapControllers();

app.Run();
