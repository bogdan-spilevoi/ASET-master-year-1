using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using SmartLost.AuthService.Application.IntegrationEvents;
using SmartLost.AuthService.Infrastructure.Authentication;
using SmartLost.BuildingBlocks.Application.Emails;

namespace SmartLost.AuthService.Infrastructure.Outbox;

public sealed class AuthEmailComposer(ConfirmationTokenProtector protector, IOptions<EmailConfirmationOptions> options)
{
    public EmailMessage ComposeConfirmation(Guid id, ConfirmationEmailRequested request)
    {
        string token = protector.Unprotect(request.EncryptedToken, id, request.Recipient, request.ExpiresAtUtc);
        string url = QueryHelpers.AddQueryString(options.Value.ConfirmationBaseUrl.Trim(), "token", token);
        string expires = request.ExpiresAtUtc.UtcDateTime.ToString("u", CultureInfo.InvariantCulture);
        string text = $"Confirm your SmartLost email address:\n{url}\n\nThis link expires at {expires}.";
        string html = $"<p><a href=\"{WebUtility.HtmlEncode(url)}\">Confirm your SmartLost email address</a></p>" +
            $"<p>This link expires at {expires}.</p>";
        return new EmailMessage(request.Recipient, "Confirm your SmartLost email address", text, html);
    }
}
