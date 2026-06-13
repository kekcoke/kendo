using Kendo.Shared.Http;
using Kendo.Shared.Resilience;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddKendoResilience(builder.Configuration);

builder.Services.AddHttpClient("default")
    .AddHttpMessageHandler<ResilienceDelegatingHandler>();

var app = builder.Build();

app.MapControllers();

app.Run();
