using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Infrastructure.Authentication;
using SmartLost.AuthService.Infrastructure.Persistence;

namespace SmartLost.AuthService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("AuthDatabase")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:AuthDatabase in appsettings.json.");
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => !string.IsNullOrWhiteSpace(options.SigningKey) && Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
                "Jwt:SigningKey must contain at least 32 UTF-8 bytes.")
            .ValidateOnStart();
        services.AddDbContext<AuthDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IUserAccountRepository, UserAccountRepository>();
        services.AddScoped<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
        services.AddScoped<IPasswordService, PasswordService>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<IAuthenticationSessionService, AuthenticationSessionService>();
        return services;
    }
}
