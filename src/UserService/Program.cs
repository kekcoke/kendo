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
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Set the kendo.embedding_dim GUC from environment for pgvector dimension check
var embeddingDim = builder.Configuration["KENDO__EVENT_EMBEDDING__DIM"] ?? "1024";
var embeddingModel = builder.Configuration["KENDO__EVENT_EMBEDDING__MODEL"] ?? "BGE-large-en-v1.5";

// Register pgvector type mapper for Vector<> properties
NpgsqlConnection.GlobalTypeMapper.UseVector();

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
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsqlOptions => npgsqlOptions.UseAdminDatabase("kendo_users")));

builder.Services.AddScoped<ResilientAppDbContext>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<OutboxRepository>();
builder.Services.AddScoped<AdminWriterConnectionFactory>();
builder.Services.AddHostedService<OutboxRelayService>();

var app = builder.Build();

// Apply kendo.embedding_dim GUC for pgvector dimension check constraint
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrEmpty(connectionString))
{
    try
    {
        using var setupConn = new NpgsqlConnection(connectionString);
        setupConn.Open();
        using var setCmd = setupConn.CreateCommand();
        setCmd.CommandText = $"ALTER DATABASE kendo_users SET kendo.embedding_dim = '{embeddingDim}'";
        setCmd.ExecuteNonQuery();
    }
    catch (Exception ex)
    {
        // Graceful skip — dimension enforcement is best-effort at startup
        // The check constraint will be validated at INSERT time by Postgres
        Console.Error.WriteLine($"Warning: Could not set kendo.embedding_dim GUC: {ex.Message}");
    }
}

app.UseMiddleware<GracefulShutdownMiddleware>();
app.UseKendoErrorHandling();
app.UseKendoReplicaIdentity();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();