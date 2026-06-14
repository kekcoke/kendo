using Kendo.Shared.ErrorHandling;
using Kendo.Shared.Http;
using Kendo.Shared.Messaging;
using Kendo.Shared.Observability;
using Kendo.Shared.Resilience;
using Kendo.UserService.Data;
using Kendo.Worker;
using Kendo.Worker.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

builder.Services.AddDbContext<WorkerDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<WorkerResilientDbContext>();

builder.Services.AddHttpClient("default")
    .AddHttpMessageHandler<ResilienceDelegatingHandler>();

builder.Services.AddHostedService<WorkerBackgroundService>();

var app = builder.Build();

app.UseKendoErrorHandling();

app.MapControllers();

app.Run();