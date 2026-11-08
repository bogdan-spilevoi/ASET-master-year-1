using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Infrastructure.Authentication;
using SmartLost.AuthService.Infrastructure.Outbox;
using SmartLost.AuthService.Infrastructure.Persistence;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Infrastructure;
using SmartLost.BuildingBlocks.Infrastructure.Outbox;

namespace SmartLost.AuthService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthPersistence(configuration);
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => !string.IsNullOrWhiteSpace(options.SigningKey) && Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
                "Jwt:SigningKey must contain at least 32 UTF-8 bytes.")
            .ValidateOnStart();
        services.AddScoped<IUserAccountRepository, UserAccountRepository>();
        services.AddScoped<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
        services.AddScoped<IPasswordService, PasswordService>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<IAuthenticationSessionService, AuthenticationSessionService>();
        return services;
    }

    public static IServiceCollection AddAuthOutboxWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthPersistence(configuration);
        services.AddOptions<AuthOutboxDeliveryOptions>()
            .Bind(configuration.GetSection(AuthOutboxDeliveryOptions.SectionName))
            .Validate(settings => settings.IsValid(), "Configure real Auth Outbox routes and a service key of at least 32 characters.")
            .ValidateOnStart();
        services.AddBuildingBlocksOutboxProcessor<AuthDbContext>();
        services.Configure<OutboxOptions>(configuration.GetSection("Outbox"));
        services.AddHttpClient<IOutboxMessagePublisher, AuthOutboxHttpPublisher>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        return services;
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
