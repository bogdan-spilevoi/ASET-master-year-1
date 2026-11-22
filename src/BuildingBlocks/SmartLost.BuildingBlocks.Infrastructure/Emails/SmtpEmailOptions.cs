using System.Net.Mail;

namespace SmartLost.BuildingBlocks.Infrastructure.Emails;

public sealed class SmtpEmailOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    public string FromAddress { get; set; } = "noreply@smartlost.local";

    public string FromDisplayName { get; set; } = "SmartLost";

    public string? UserName
    {
        get; set;
    }

    public string? Password
    {
        get; set;
    }

    public bool EnableSsl
    {
        get; set;
    }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

    public bool IsValid()
    {
        bool hasUserName = !string.IsNullOrWhiteSpace(UserName);
        bool hasPassword = !string.IsNullOrWhiteSpace(Password);
        return !string.IsNullOrWhiteSpace(Host) &&
            Port is >= 1 and <= 65535 &&
            !string.IsNullOrWhiteSpace(FromAddress) &&
            MailAddress.TryCreate(FromAddress.Trim(), out _) &&
            !string.IsNullOrWhiteSpace(FromDisplayName) &&
            Timeout >= TimeSpan.FromSeconds(1) &&
            Timeout <= TimeSpan.FromMinutes(2) &&
            hasUserName == hasPassword;
    }
}
