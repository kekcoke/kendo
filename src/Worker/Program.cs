using Kendo.Shared.Caching;
using Kendo.Shared.ErrorHandling;
using Kendo.Shared.Http;
using Kendo.Shared.Messaging;
using Kendo.Shared.Observability;
using Kendo.Shared.GracefulShutdown;
using Kendo.Shared.Resilience;
using Kendo.UserService.Data;
using Kendo.Worker;
using Kendo.Worker.Data;
using Kendo.Worker.Handlers;
using Kendo.Worker.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rebus.Handlers;
using Rebus.ServiceProvider;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .ConfigureApplicationPartManager(apm =>
    {
        apm.ApplicationParts.Clear();
        apm.ApplicationParts.Add(new Microsoft.AspNetCore.Mvc.ApplicationParts.AssemblyPart(typeof(Program).Assembly));
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
    options.SuppressModelStateInvalidFilter = true);

builder.Services.AddKendoResilience(builder.Configuration);
builder.Services.AddKendoObservability(builder.Configuration, "kendo-worker");
builder.Services.AddKendoDistributedCache(builder.Configuration);
builder.Services.AddKendoErrorHandling();
builder.Services.AddKendoRebus(builder.Configuration, "consumer");
builder.Services.AddKendoRebusAiConsumer(builder.Configuration);
builder.Services.AddKendoGracefulShutdown(builder.Configuration);

// Register Rebus handlers from the Worker assembly
builder.Services.AddTransient<IHandleMessages<UserCreatedEvent>, UserCreatedEventHandler>();

builder.Services.AddDbContext<WorkerDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<WorkerResilientDbContext>();

builder.Services.AddHttpClient("default")
    .AddHttpMessageHandler<ResilienceDelegatingHandler>();

builder.Services.AddKendoRebusDlqConsumer(builder.Configuration);
builder.Services.AddHostedService<WorkerBackgroundService>();
builder.Services.AddHostedService<DlqDepthMonitor>();

var app = builder.Build();

app.UseMiddleware<GracefulShutdownMiddleware>();
app.UseKendoErrorHandling();
app.UseKendoReplicaIdentity();

app.MapControllers();

app.Run();