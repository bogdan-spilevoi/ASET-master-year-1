using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartLost.BuildingBlocks.Application.Emails;
using SmartLost.BuildingBlocks.Infrastructure;
using SmartLost.BuildingBlocks.Infrastructure.Emails;
using Xunit;

namespace SmartLost.BuildingBlocks.UnitTests.Emails;

public sealed class SmtpEmailSenderTests
{
    [Fact]
    public void EmailMessageTrimsMetadataAndNormalizesBlankHtmlBody()
    {
        EmailMessage plain = new("  user@example.com  ", "  Confirm Email  ", "Plain body", "   ");
        Assert.Equal("user@example.com", plain.ToAddress);
        Assert.Equal("Confirm Email", plain.Subject);
        Assert.Equal("Plain body", plain.TextBody);
        Assert.Null(plain.HtmlBody);

        EmailMessage multipart = new("user@example.com", "Subject", "Text", "<p>Html</p>");
        Assert.Equal("<p>Html</p>", multipart.HtmlBody);

        Assert.ThrowsAny<ArgumentException>(() => new EmailMessage(" ", "Subject", "Body"));
        Assert.ThrowsAny<ArgumentException>(() => new EmailMessage("user@example.com", " ", "Body"));
        Assert.ThrowsAny<ArgumentException>(() => new EmailMessage("user@example.com", "Subject", " "));
    }

    [Fact]
    public void SmtpEmailOptionsValidatesHostPortSenderTimeoutAndCredentials()
    {
        SmtpEmailOptions valid = new();
        Assert.True(valid.IsValid());

        SmtpEmailOptions authenticated = new()
        {
            Host = "mailpit",
            Port = 1025,
            FromAddress = "noreply@smartlost.local",
            FromDisplayName = "SmartLost Auth",
            UserName = "smtp-user",
            Password = "smtp-password",
            EnableSsl = false,
            Timeout = TimeSpan.FromSeconds(10)
        };
        Assert.True(authenticated.IsValid());

        Assert.False(new SmtpEmailOptions { Host = " " }.IsValid());
        Assert.False(new SmtpEmailOptions { Port = 0 }.IsValid());
        Assert.False(new SmtpEmailOptions { Port = 65536 }.IsValid());
        Assert.False(new SmtpEmailOptions { FromAddress = "not-an-email" }.IsValid());
        Assert.False(new SmtpEmailOptions { FromDisplayName = " " }.IsValid());
        Assert.False(new SmtpEmailOptions { Timeout = TimeSpan.Zero }.IsValid());
        Assert.False(new SmtpEmailOptions { Timeout = TimeSpan.FromMinutes(3) }.IsValid());
        Assert.False(new SmtpEmailOptions { UserName = "user-only" }.IsValid());
        Assert.False(new SmtpEmailOptions { Password = "password-only" }.IsValid());
    }

    [Fact]
    public async Task AddBuildingBlocksSmtpEmailSenderValidatesAtStartupAndPreservesCustomSender()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddBuildingBlocksSmtpEmailSender());

        using IHost invalidHost = new HostBuilder()
            .ConfigureServices(services => services.AddBuildingBlocksSmtpEmailSender(options => options.Port = 0))
            .Build();
        OptionsValidationException exception = await Assert.ThrowsAsync<OptionsValidationException>(() => invalidHost.StartAsync());
        Assert.Equal(typeof(SmtpEmailOptions), exception.OptionsType);

        RecordingEmailSender customSender = new();
        using IHost customHost = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddScoped<IEmailSender>(_ => customSender);
                services.AddBuildingBlocksSmtpEmailSender();
            })
            .Build();
        await customHost.StartAsync();
        using IServiceScope scope = customHost.Services.CreateScope();
        Assert.Same(customSender, scope.ServiceProvider.GetRequiredService<IEmailSender>());
        await customHost.StopAsync();
    }

    [Fact]
    public async Task SendAsyncRejectsNullAndDeliversPlainAndMultipartAuthenticatedMessages()
    {
        SmtpEmailSender defaultSender = new(Options.Create(new SmtpEmailOptions()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => defaultSender.SendAsync(null!, CancellationToken.None));

        await using FakeSmtpServer plainServer = new(advertiseAuth: false);
        Task<CapturedSmtpSession> plainCapture = plainServer.AcceptSingleAsync();
        SmtpEmailSender plainSender = new(Options.Create(new SmtpEmailOptions
        {
            Host = "127.0.0.1",
            Port = plainServer.Port,
            FromAddress = "noreply@smartlost.local",
            FromDisplayName = "SmartLost",
            Timeout = TimeSpan.FromSeconds(5)
        }));

        await plainSender.SendAsync(
            new EmailMessage("recipient@example.com", "Plain subject", "Confirm link: http://localhost:5080/confirm"),
            CancellationToken.None);

        CapturedSmtpSession plainSession = await plainCapture;
        Assert.Contains("MAIL FROM:<noreply@smartlost.local>", plainSession.Commands, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("RCPT TO:<recipient@example.com>", plainSession.Commands, StringComparer.OrdinalIgnoreCase);
        Assert.False(plainSession.Authenticated);
        string plainDecoded = DecodeMimeBodies(plainSession.Data);
        Assert.Contains("Confirm link: http://localhost:5080/confirm", plainDecoded, StringComparison.Ordinal);

        await using FakeSmtpServer authServer = new(advertiseAuth: true);
        Task<CapturedSmtpSession> authCapture = authServer.AcceptSingleAsync();
        SmtpEmailSender authSender = new(Options.Create(new SmtpEmailOptions
        {
            Host = "127.0.0.1",
            Port = authServer.Port,
            FromAddress = "noreply@smartlost.local",
            FromDisplayName = "SmartLost",
            UserName = "smtp-user",
            Password = "smtp-password",
            Timeout = TimeSpan.FromSeconds(5)
        }));

        await authSender.SendAsync(
            new EmailMessage("alice@example.com", "HTML subject", "Plain fallback", "<p>Confirm <strong>now</strong></p>"),
            CancellationToken.None);

        CapturedSmtpSession authSession = await authCapture;
        Assert.True(authSession.Authenticated);
        string authDecoded = DecodeMimeBodies(authSession.Data);
        Assert.Contains("Plain fallback", authDecoded, StringComparison.Ordinal);
        Assert.Contains("Confirm <strong>now</strong>", authDecoded, StringComparison.Ordinal);
    }

    private static string DecodeMimeBodies(string rawData)
    {
        StringBuilder decoded = new(rawData);
        StringBuilder currentBase64 = new();
        bool inHeaders = true;
        bool isBase64Part = false;
        foreach (string rawLine in rawData.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (rawLine.StartsWith("--", StringComparison.Ordinal))
            {
                FlushBase64(currentBase64, decoded);
                inHeaders = true;
                isBase64Part = false;
                continue;
            }

            if (inHeaders)
            {
                if (rawLine.Length == 0)
                {
                    inHeaders = false;
                }
                else if (rawLine.Contains("Content-Transfer-Encoding: base64", StringComparison.OrdinalIgnoreCase))
                {
                    isBase64Part = true;
                }

                continue;
            }

            if (isBase64Part && !string.IsNullOrWhiteSpace(rawLine))
            {
                currentBase64.Append(rawLine.Trim());
            }
        }

        FlushBase64(currentBase64, decoded);
        return decoded.ToString();
    }

    private static void FlushBase64(StringBuilder buffer, StringBuilder output)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        output.AppendLine();
        output.AppendLine(Encoding.UTF8.GetString(Convert.FromBase64String(buffer.ToString())));
        buffer.Clear();
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed record CapturedSmtpSession(IReadOnlyList<string> Commands, string Data, bool Authenticated);

    private sealed class FakeSmtpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly bool _advertiseAuth;

        public FakeSmtpServer(bool advertiseAuth)
        {
            _advertiseAuth = advertiseAuth;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public async Task<CapturedSmtpSession> AcceptSingleAsync()
        {
            using TcpClient client = await _listener.AcceptTcpClientAsync();
            await using NetworkStream stream = client.GetStream();
            using StreamReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
            await using StreamWriter writer = new(stream, Encoding.ASCII, leaveOpen: true)
            {
                NewLine = "\r\n",
                AutoFlush = true
            };

            await writer.WriteLineAsync("220 localhost ESMTP FakeSmtp");
            List<string> commands = [];
            StringBuilder data = new();
            bool authenticated = false;

            while (true)
            {
                string? line = await reader.ReadLineAsync();
                if (line is null)
                {
                    break;
                }

                commands.Add(line);
                if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250-localhost");
                    if (_advertiseAuth)
                    {
                        await writer.WriteLineAsync("250-AUTH LOGIN PLAIN");
                    }

                    await writer.WriteLineAsync("250 OK");
                }
                else if (line.StartsWith("AUTH LOGIN", StringComparison.OrdinalIgnoreCase))
                {
                    if (line.Trim().Length == "AUTH LOGIN".Length)
                    {
                        await writer.WriteLineAsync("334 VXNlcm5hbWU6");
                        await reader.ReadLineAsync();
                    }

                    await writer.WriteLineAsync("334 UGFzc3dvcmQ6");
                    await reader.ReadLineAsync();
                    authenticated = true;
                    await writer.WriteLineAsync("235 2.7.0 Authentication successful");
                }
                else if (line.StartsWith("AUTH PLAIN", StringComparison.OrdinalIgnoreCase))
                {
                    authenticated = true;
                    await writer.WriteLineAsync("235 2.7.0 Authentication successful");
                }
                else if (line.StartsWith("MAIL FROM:", StringComparison.OrdinalIgnoreCase) ||
                         line.StartsWith("RCPT TO:", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250 OK");
                }
                else if (string.Equals(line, "DATA", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                    while (true)
                    {
                        string? bodyLine = await reader.ReadLineAsync();
                        if (bodyLine is null || bodyLine == ".")
                        {
                            break;
                        }

                        data.AppendLine(bodyLine);
                    }

                    await writer.WriteLineAsync("250 OK: queued");
                }
                else if (string.Equals(line, "QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 Bye");
                    break;
                }
                else
                {
                    await writer.WriteLineAsync("250 OK");
                }
            }

            return new CapturedSmtpSession(commands, data.ToString(), authenticated);
        }

        public ValueTask DisposeAsync()
        {
            _listener.Stop();
            return ValueTask.CompletedTask;
        }
    }
}
