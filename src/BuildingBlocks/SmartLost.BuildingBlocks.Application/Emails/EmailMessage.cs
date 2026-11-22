namespace SmartLost.BuildingBlocks.Application.Emails;

/// <summary>A transport-neutral email message payload.</summary>
public sealed record EmailMessage
{
    public EmailMessage(string toAddress, string subject, string textBody, string? htmlBody = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(textBody);
        ToAddress = toAddress.Trim();
        Subject = subject.Trim();
        TextBody = textBody;
        HtmlBody = string.IsNullOrWhiteSpace(htmlBody) ? null : htmlBody;
    }

    public string ToAddress
    {
        get;
    }

    public string Subject
    {
        get;
    }

    public string TextBody
    {
        get;
    }

    public string? HtmlBody
    {
        get;
    }
}
