using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Options;
using SmartLost.BuildingBlocks.Application.Emails;

namespace SmartLost.BuildingBlocks.Infrastructure.Emails;

/// <summary>Sends application emails over SMTP using validated configuration options.</summary>
public sealed class SmtpEmailSender(IOptions<SmtpEmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        SmtpEmailOptions settings = options.Value;

        using MailMessage mail = new()
        {
            From = new MailAddress(settings.FromAddress.Trim(), settings.FromDisplayName.Trim(), Encoding.UTF8),
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            Body = message.TextBody,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };
        mail.To.Add(new MailAddress(message.ToAddress));

        if (message.HtmlBody is not null)
        {
            mail.AlternateViews.Add(
                AlternateView.CreateAlternateViewFromString(message.HtmlBody, Encoding.UTF8, MediaTypeNames.Text.Html));
        }

        using SmtpClient client = new(settings.Host.Trim(), settings.Port);
        client.DeliveryMethod = SmtpDeliveryMethod.Network;
        client.EnableSsl = settings.EnableSsl;
        client.Timeout = (int)settings.Timeout.TotalMilliseconds;
        if (!string.IsNullOrWhiteSpace(settings.UserName))
        {
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(settings.UserName, settings.Password);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(settings.Timeout);
        await client.SendMailAsync(mail, timeoutCts.Token);
    }
}
