using Kendo.UserService.Data;
using Kendo.Shared.Observability;
using Kendo.Shared.Resilience;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddKendoResilience(builder.Configuration);
builder.Services.AddKendoObservability(builder.Configuration, "kendo-userservice");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<ResilientAppDbContext>();

var app = builder.Build();

app.MapControllers();

app.Run();
