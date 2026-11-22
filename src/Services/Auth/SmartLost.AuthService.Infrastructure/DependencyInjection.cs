using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.AuthService.Application.IntegrationEvents;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Infrastructure.Authentication;
using SmartLost.AuthService.Infrastructure.Outbox;
using SmartLost.AuthService.Infrastructure.Persistence;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Infrastructure;
using SmartLost.BuildingBlocks.Infrastructure.Emails;
using SmartLost.BuildingBlocks.Infrastructure.Outbox;

namespace SmartLost.AuthService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthPersistence(configuration);
        services.AddBuildingBlocksQueryCache(configuration, "auth");
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => !string.IsNullOrWhiteSpace(options.SigningKey) && Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
                "Jwt:SigningKey must contain at least 32 UTF-8 bytes.")
            .ValidateOnStart();
        AddEmailConfirmation(services, configuration);
        services.AddScoped<IUserAccountRepository, UserAccountRepository>();
        services.AddScoped<IUnitOfWork, AuthUnitOfWork>();
        services.AddScoped<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
        services.AddScoped<IPasswordService, PasswordService>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<IAuthenticationSessionService, AuthenticationSessionService>();
        services.AddScoped<IEmailConfirmationService, EmailConfirmationService>();
        return services;
    }

    public static IServiceCollection AddAuthOutboxWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthPersistence(configuration);
        services.AddOptions<AuthOutboxDeliveryOptions>()
            .Bind(configuration.GetSection(AuthOutboxDeliveryOptions.SectionName))
            .Validate(settings => settings.IsValid(), "Configure real Auth Outbox routes and a service key of at least 32 characters.")
            .ValidateOnStart();
        services.AddBuildingBlocksOutboxProcessor<AuthDbContext>(options => ConfigureIdentityOutbox(options, configuration));
        services.AddHttpClient<IOutboxMessagePublisher, AuthOutboxHttpPublisher>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        return services;
    }

    public static IServiceCollection AddAuthIdentityOutboxDelivery(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthPersistence(configuration);
        services.AddBuildingBlocksOutboxDelivery<AuthDbContext>(
            options => ConfigureIdentityOutbox(options, configuration),
            delivery => configuration.GetSection("OutboxDelivery").Bind(delivery));
        return services;
    }

    private static void ConfigureIdentityOutbox(OutboxOptions options, IConfiguration configuration)
    {
        configuration.GetSection("Outbox").Bind(options);
        options.MessageTypes = [AuthIntegrationEventTypes.UserCreated, AuthIntegrationEventTypes.UserIdentityChanged];
    }

    public static IServiceCollection AddAuthEmailOutboxWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthPersistence(configuration);
        AddEmailConfirmation(services, configuration);
        services.AddSingleton<AuthEmailComposer>();
        services.AddBuildingBlocksSmtpEmailSender(options => configuration.GetSection(SmtpEmailOptions.SectionName).Bind(options));
        services.AddBuildingBlocksOutboxDelivery<AuthDbContext>(
            options =>
            {
                configuration.GetSection("Outbox").Bind(options);
                options.MessageTypes = [AuthOutboxMessageTypes.ConfirmationEmailRequested];
            },
            delivery => configuration.GetSection("OutboxDelivery").Bind(delivery));
        services.AddScoped<IOutboxMessagePublisher, AuthOutboxEmailPublisher>();
        return services;
    }

    private static void AddEmailConfirmation(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailConfirmationOptions>()
            .Bind(configuration.GetSection(EmailConfirmationOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => options.IsValid(), "Configure a valid HTTP/HTTPS EmailConfirmation:ConfirmationBaseUrl and TokenExpiryHours between 1 and 168, and a Base64-encoded 32-byte EmailConfirmation:EncryptionKey.")
            .ValidateOnStart();
        services.AddSingleton<ConfirmationTokenProtector>();
    }

    private static void AddAuthPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("AuthDatabase")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:AuthDatabase.");
        services.AddDbContext<AuthDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IOutboxEventMapper, AuthOutboxEventMapper>();
        services.AddBuildingBlocksOutbox<AuthDbContext>();
    }
}
