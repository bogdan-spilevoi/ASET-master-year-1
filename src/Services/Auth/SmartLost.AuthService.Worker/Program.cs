using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartLost.AuthService.Infrastructure;
using SmartLost.BuildingBlocks.Messaging.RabbitMq;

namespace SmartLost.AuthService.Worker;

/// <summary>Auth Outbox host supporting finite HTTP Batch mode or continuous Email and RabbitMQ Publish modes.</summary>
public static class WorkerProgram
{
    public static async Task Main(string[] args)
    {
        using IHost host = BuildHost(args);
        AuthOutboxJob? batch = host.Services.GetService<AuthOutboxJob>();
        if (batch is null)
        {
            await host.RunAsync();
            return;
        }

        await host.StartAsync();
        try
        {
            IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            await batch.RunAsync(lifetime.ApplicationStopping);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    public static IHost BuildHost(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        string? settingsFile = builder.Configuration["SettingsFile"];
        builder.Configuration
            .AddJsonFile(settingsFile ?? Path.Combine(AppContext.BaseDirectory, "outboxsettings.json"), optional: settingsFile is null)
            .AddEnvironmentVariables()
            .AddCommandLine(args);
        string mode = builder.Configuration["Worker:Mode"] ?? "Batch";
        switch (mode)
        {
            case "Batch":
                builder.Services.AddAuthOutboxWorker(builder.Configuration);
                builder.Services.AddOptions<AuthOutboxWorkerOptions>().Bind(builder.Configuration.GetSection("Worker"))
                    .Validate(settings => settings.MaximumBatches is >= 1 and <= 100, "Worker:MaximumBatches must be between 1 and 100.")
                    .ValidateOnStart();
                builder.Services.AddSingleton<AuthOutboxJob>();
                break;
            case "Publish":
                builder.Services.AddAuthIdentityOutboxDelivery(builder.Configuration);
                builder.Services.AddRabbitMqOutboxPublisher(builder.Configuration);
                break;
            case "Email":
                builder.Services.AddAuthEmailOutboxWorker(builder.Configuration);
                break;
            default:
                throw new InvalidOperationException("Worker:Mode must be Batch, Publish or Email.");
        }

        return builder.Build();
    }
}
