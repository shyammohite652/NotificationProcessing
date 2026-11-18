using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NotificationApi.Data;
using NotificationApi.Middleware;
using NotificationApi.Providers;
using NotificationApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErrorHandler>();

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("NotificationDb")));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddSingleton<INotificationProvider, EmailNotificationProvider>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<NotificationProcessor>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Create/upgrade the database automatically in development.
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

app.MapControllers();
app.Run();

public partial class Program;
