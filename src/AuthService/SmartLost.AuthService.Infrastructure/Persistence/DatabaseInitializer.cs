using Microsoft.Extensions.DependencyInjection;

namespace SmartLost.AuthService.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    public static async Task EnsureCreatedAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = serviceProvider.CreateScope();
        AuthDbContext dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }
}
