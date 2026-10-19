using SmartLost.AuthService.Application.IntegrationEvents;

namespace SmartLost.AuthService.Infrastructure.Outbox;

public sealed class AuthOutboxDeliveryOptions
{
    public const string SectionName = "OutboxDelivery";

    public Dictionary<string, string> Routes
    {
        get;
        set;
    } = new(StringComparer.Ordinal);

    public string ServiceKey
    {
        get;
        set;
    } = string.Empty;

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(ServiceKey) && ServiceKey.Length >= 32 &&
            !ServiceKey.Any(character => character is < '!' or > '~') && Routes is not null &&
            !ServiceKey.StartsWith("replace-", StringComparison.OrdinalIgnoreCase) &&
            HasValidRoute(AuthIntegrationEventTypes.UserCreated) &&
            HasValidRoute(AuthIntegrationEventTypes.UserIdentityChanged);
    }

    private bool HasValidRoute(string type)
    {
        return Routes.TryGetValue(type, out string? value) && Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
            string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment) &&
            !uri.Host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase) &&
            (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
    }
}
