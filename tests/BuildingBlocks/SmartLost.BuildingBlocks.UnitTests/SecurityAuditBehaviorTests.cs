using System.Net;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartLost.BuildingBlocks.Application;
using SmartLost.BuildingBlocks.Application.Behaviors;
using SmartLost.BuildingBlocks.Application.Security;
using SmartLost.BuildingBlocks.AspNetCore.Security;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.BuildingBlocks.Infrastructure;
using SmartLost.BuildingBlocks.Infrastructure.Security;
using Xunit;

namespace SmartLost.BuildingBlocks.UnitTests;

public sealed class SecurityAuditBehaviorTests
{
    private sealed record AuditedProbeCommand(string Username, bool ShouldSucceed, string? ErrorCode = null)
        : IRequest<Result<string>>, ISecurityAuditedRequest
    {
        public string SecurityEventType => "ProbeLogin";

        public string? TargetSubject => Username;
    }

    private sealed record NonAuditedCommand(string Data) : IRequest<Result<string>>;

    private sealed class TestSecurityAuditContext(string? ip, string? userAgent) : ISecurityAuditContext
    {
        public string? ClientIp => ip;

        public string? UserAgent => userAgent;
    }

    [Fact]
    public async Task HandleWithNonAuditedRequestBypassesAuditSink()
    {
        InMemorySecurityAuditSink sink = new();
        SecurityAuditBehavior<NonAuditedCommand, Result<string>> behavior = new(sink);

        Result<string> result = await behavior.Handle(
            new NonAuditedCommand("payload"),
            _ => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task HandleWithSuccessfulAuditedRequestRecordsSuccessEvent()
    {
        InMemorySecurityAuditSink sink = new();
        TestSecurityAuditContext context = new("192.168.1.100", "Mozilla/5.0 TestBrowser");
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(NullLoggerProvider.Instance));
        ILogger<SecurityAuditBehavior<AuditedProbeCommand, Result<string>>> logger =
            loggerFactory.CreateLogger<SecurityAuditBehavior<AuditedProbeCommand, Result<string>>>();

        SecurityAuditBehavior<AuditedProbeCommand, Result<string>> behavior = new(sink, context, logger);

        Result<string> result = await behavior.Handle(
            new AuditedProbeCommand("alice", true),
            _ => Task.FromResult(Result<string>.Success("token123")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        SecurityAuditEvent recorded = Assert.Single(sink.Events);
        Assert.NotEqual(Guid.Empty, recorded.EventId);
        Assert.Equal("ProbeLogin", recorded.EventType);
        Assert.Equal("alice", recorded.SubjectIdentifier);
        Assert.Equal("192.168.1.100", recorded.ClientIp);
        Assert.Equal("Mozilla/5.0 TestBrowser", recorded.UserAgent);
        Assert.True(recorded.Succeeded);
        Assert.Null(recorded.FailureReason);
        Assert.True(recorded.OccurredAtUtc <= DateTime.UtcNow);
    }

    [Fact]
    public async Task HandleWithFailedAuditedRequestRecordsFailureEventWithErrorCode()
    {
        InMemorySecurityAuditSink sink = new();
        TestSecurityAuditContext context = new("10.0.0.1", "TestAgent");
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(NullLoggerProvider.Instance));
        ILogger<SecurityAuditBehavior<AuditedProbeCommand, Result<string>>> logger =
            loggerFactory.CreateLogger<SecurityAuditBehavior<AuditedProbeCommand, Result<string>>>();

        SecurityAuditBehavior<AuditedProbeCommand, Result<string>> behavior = new(sink, context, logger);

        Result<string> result = await behavior.Handle(
            new AuditedProbeCommand("bob", false, "auth.invalid_credentials"),
            _ => Task.FromResult(Result<string>.Failure([new Error("auth.invalid_credentials", "Bad password.", ErrorKind.Unauthorized)])),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        SecurityAuditEvent recorded = Assert.Single(sink.Events);
        Assert.Equal("ProbeLogin", recorded.EventType);
        Assert.Equal("bob", recorded.SubjectIdentifier);
        Assert.Equal("10.0.0.1", recorded.ClientIp);
        Assert.Equal("TestAgent", recorded.UserAgent);
        Assert.False(recorded.Succeeded);
        Assert.Equal("auth.invalid_credentials", recorded.FailureReason);
    }

    [Fact]
    public async Task HandleWhenHandlerThrowsExceptionRecordsFailureEventAndRethrows()
    {
        InMemorySecurityAuditSink sink = new();
        TestSecurityAuditContext context = new("127.0.0.1", "ConsoleClient");
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(NullLoggerProvider.Instance));
        ILogger<SecurityAuditBehavior<AuditedProbeCommand, Result<string>>> logger =
            loggerFactory.CreateLogger<SecurityAuditBehavior<AuditedProbeCommand, Result<string>>>();

        SecurityAuditBehavior<AuditedProbeCommand, Result<string>> behavior = new(sink, context, logger);

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                new AuditedProbeCommand("charlie", false),
                _ => throw new InvalidOperationException("DB failed"),
                CancellationToken.None));

        Assert.Equal("DB failed", thrown.Message);
        SecurityAuditEvent recorded = Assert.Single(sink.Events);
        Assert.Equal("ProbeLogin", recorded.EventType);
        Assert.Equal("charlie", recorded.SubjectIdentifier);
        Assert.False(recorded.Succeeded);
        Assert.Equal("InvalidOperationException", recorded.FailureReason);
    }

    [Fact]
    public void DefaultSecurityAuditContextReturnsNull()
    {
        DefaultSecurityAuditContext context = DefaultSecurityAuditContext.Instance;
        Assert.Null(context.ClientIp);
        Assert.Null(context.UserAgent);
    }

    [Fact]
    public void HttpSecurityAuditContextReadsHttpContextValuesCorrectly()
    {
        HttpContextAccessor accessor = new();
        HttpSecurityAuditContext context = new(accessor);

        Assert.Null(context.ClientIp);
        Assert.Null(context.UserAgent);

        DefaultHttpContext httpContext = new();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.0.50");
        httpContext.Request.Headers.UserAgent = "SmartLostClient/1.0";
        accessor.HttpContext = httpContext;

        Assert.Equal("192.168.0.50", context.ClientIp);
        Assert.Equal("SmartLostClient/1.0", context.UserAgent);
    }

    [Fact]
    public async Task InMemorySecurityAuditSinkStoresAndClearsEvents()
    {
        InMemorySecurityAuditSink sink = new();
        Assert.Empty(sink.Events);

        SecurityAuditEvent first = new(Guid.NewGuid(), "TypeA", "userA", null, null, true, null, DateTime.UtcNow);
        SecurityAuditEvent second = new(Guid.NewGuid(), "TypeB", "userB", null, null, false, "err", DateTime.UtcNow);

        await sink.RecordAsync(first);
        await sink.RecordAsync(second);

        Assert.Equal(2, sink.Events.Count);
        Assert.Contains(sink.Events, candidate => candidate.EventType == "TypeA");
        Assert.Contains(sink.Events, candidate => candidate.EventType == "TypeB");

        sink.Clear();
        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task LoggingSecurityAuditSinkRecordsWithoutException()
    {
        LoggingSecurityAuditSink sink = new(NullLogger<LoggingSecurityAuditSink>.Instance);
        SecurityAuditEvent auditEvent = new(Guid.NewGuid(), "TestLogin", "user1", "1.1.1.1", "Agent", true, null, DateTime.UtcNow);

        await sink.RecordAsync(auditEvent);
        await Assert.ThrowsAsync<ArgumentNullException>(() => sink.RecordAsync(null!));
    }

    [Fact]
    public void DependencyInjectionRegistersSecurityAuditServices()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddBuildingBlocksSecurityAudit();
        services.AddBuildingBlocksApplication(typeof(SecurityAuditBehaviorTests).Assembly, useSecurityAudit: true);

        using ServiceProvider provider = services.BuildServiceProvider();
        ISecurityAuditSink sink = provider.GetRequiredService<ISecurityAuditSink>();
        ISecurityAuditContext context = provider.GetRequiredService<ISecurityAuditContext>();

        Assert.NotNull(sink);
        Assert.NotNull(context);
        Assert.IsType<LoggingSecurityAuditSink>(sink);
        Assert.IsType<DefaultSecurityAuditContext>(context);
    }
}
