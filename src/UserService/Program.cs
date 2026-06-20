using Kendo.Shared.Authentication;
using Kendo.Shared.Caching;
using Kendo.Shared.ErrorHandling;
using Kendo.Shared.Messaging;
using Kendo.Shared.Observability;
using Kendo.Shared.GracefulShutdown;
using Kendo.Shared.Resilience;
using Kendo.UserService.Data;
using Kendo.UserService.Services;
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
builder.Services.AddKendoJwt(builder.Configuration);
builder.Services.AddKendoAdminScopePolicies();
builder.Services.AddKendoObservability(builder.Configuration, "kendo-userservice");
builder.Services.AddKendoDistributedCache(builder.Configuration);
builder.Services.AddKendoErrorHandling();
builder.Services.AddKendoRebus(builder.Configuration, "producer");
builder.Services.AddKendoRebusAiProducer(builder.Configuration);
builder.Services.AddKendoGracefulShutdown(builder.Configuration);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<ResilientAppDbContext>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<OutboxRepository>();
builder.Services.AddScoped<AdminWriterConnectionFactory>();
builder.Services.AddHostedService<OutboxRelayService>();

var app = builder.Build();

app.UseMiddleware<GracefulShutdownMiddleware>();
app.UseKendoErrorHandling();
app.UseKendoReplicaIdentity();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();