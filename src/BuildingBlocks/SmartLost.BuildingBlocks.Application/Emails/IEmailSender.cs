namespace SmartLost.BuildingBlocks.Application.Emails;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
