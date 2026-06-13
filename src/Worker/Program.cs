using Kendo.Worker;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHostedService<WorkerBackgroundService>();

var app = builder.Build();

app.MapControllers();

app.Run();
