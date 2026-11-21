using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using SmartLost.BuildingBlocks.AspNetCore;
using SmartLost.MatchingService.Api.Diagnostics;
using SmartLost.MatchingService.Application;
using SmartLost.MatchingService.Application.Profiling;
using SmartLost.MatchingService.Infrastructure;
using SmartLost.MatchingService.Infrastructure.Persistence;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddBuildingBlocksApi();

MatchingProfilingOptions profiling = builder.Configuration.GetSection("Matching:Profiling").Get<MatchingProfilingOptions>()
    ?? new MatchingProfilingOptions();
builder.Services.AddApplication(profilingOptions: profiling);
builder.Services.AddInfrastructure(builder.Configuration);
// Must follow AddInfrastructure, which registers the (mock) AI adapters being decorated.
builder.Services.AddMatchingAiProfiling();

// Recent logs back the development-only GET /api/matching/demo/logs endpoint.
builder.Services.AddSingleton<RecentLogBuffer>();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<ILoggerProvider, RecentLogProvider>();
}

WebApplication app = builder.Build();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("../openapi/v1.json", "SmartLost MatchingService v1");
        options.DocumentTitle = "SmartLost MatchingService API";
    });
}
else
{
    app.UseHttpsRedirection();
}

app.MapControllers();
// Liveness runs no checks, so a database outage never restarts the process.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
// Readiness only reads connectivity and EF migration history.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains(MatchingDatabaseHealthCheck.ReadinessTag)
});

app.Run();
