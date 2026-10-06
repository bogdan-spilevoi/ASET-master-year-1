using SmartLost.AuthService.Api.Mapping;

namespace SmartLost.AuthService.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddApiMapping(this IServiceCollection services)
    {
        return services.AddAutoMapper(configuration =>
        {
            configuration.ShouldUseConstructor = constructor => constructor.IsPublic;
        }, typeof(AuthMappingProfile));
    }
}
