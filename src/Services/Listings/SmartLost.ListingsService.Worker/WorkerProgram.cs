using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SmartLost.BuildingBlocks.Messaging.RabbitMq;
using SmartLost.ListingsService.Infrastructure;

namespace SmartLost.ListingsService.Worker;

/// <summary>Listings Outbox host with explicit Inspect or RabbitMQ Publish mode.</summary>
public static class WorkerProgram
{
    public static Task Main(string[] args)
    {
        return BuildHost(args).RunAsync();
    }

    public static IHost BuildHost(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        string? settingsFile = builder.Configuration["SettingsFile"];
        builder.Configuration
            .AddJsonFile(settingsFile ?? Path.Combine(AppContext.BaseDirectory, "outboxsettings.json"), optional: settingsFile is null)
            .AddEnvironmentVariables()
            .AddCommandLine(args);
        switch (builder.Configuration["Worker:Mode"] ?? "Inspect")
        {
            case "Inspect":
                builder.Services.AddListingsOutboxInspection(builder.Configuration);
                break;
            case "Publish":
                builder.Services.AddListingsOutboxDelivery(builder.Configuration);
                builder.Services.AddRabbitMqOutboxPublisher(builder.Configuration);
                break;
            default:
                throw new InvalidOperationException("Worker:Mode must be Inspect or Publish.");
        }
        return builder.Build();
    }
}
