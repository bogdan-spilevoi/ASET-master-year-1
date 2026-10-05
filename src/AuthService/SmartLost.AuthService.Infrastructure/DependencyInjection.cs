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
            ?? throw new InvalidOperationException("Connection string 'AuthDatabase' was not found.");

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddDbContext<AuthDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IUserAccountRepository, UserAccountRepository>();
        services.AddScoped<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
        services.AddScoped<IPasswordService, PasswordService>();
        services.AddSingleton<ITokenService, TokenService>();
        return services;
    }
}
