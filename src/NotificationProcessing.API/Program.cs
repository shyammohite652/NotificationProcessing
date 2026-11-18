using System.Text.Json.Serialization;
using NotificationProcessing.API.Middleware;
using NotificationProcessing.Application;
using NotificationProcessing.Application.Options;
using NotificationProcessing.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();

// Injected everywhere "now" is needed, so tests can control the clock.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddOptions<NotificationOptions>()
    .Bind(builder.Configuration.GetSection(NotificationOptions.SectionName))
    .Validate(o => o.DuplicateDetectionWindow > TimeSpan.Zero, "Notifications:DuplicateDetectionWindow must be positive.")
    .Validate(o => o.ProcessingBatchSize is > 0 and <= 1000, "Notifications:ProcessingBatchSize must be between 1 and 1000.")
    .ValidateOnStart();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                       // /openapi/v1.json
    app.UseSwaggerUI(o =>                   // /swagger
    {
        o.SwaggerEndpoint("/openapi/v1.json", "Notification Processing API v1");
        o.RoutePrefix = "swagger";
    });
}

app.MapControllers();

if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await app.Services.ApplyDatabaseMigrationsAsync();
}

await app.RunAsync();

// Makes the entry point visible to a future WebApplicationFactory-based integration test project.
public partial class Program;
