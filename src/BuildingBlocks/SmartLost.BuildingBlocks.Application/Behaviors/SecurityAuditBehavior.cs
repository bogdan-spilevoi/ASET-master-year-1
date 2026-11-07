using MediatR;
using Microsoft.Extensions.Logging;
using SmartLost.BuildingBlocks.Application.Security;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.BuildingBlocks.Application.Behaviors;

public sealed class SecurityAuditBehavior<TRequest, TResponse>(
    ISecurityAuditSink auditSink,
    ISecurityAuditContext? auditContext = null,
    ILogger<SecurityAuditBehavior<TRequest, TResponse>>? logger = null)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IResult
{
    private static readonly Action<ILogger, string, string?, Exception?> _successLog = LoggerMessage.Define<string, string?>(
        LogLevel.Information, new EventId(3100, "SecurityAuditSuccess"), "Security audit: {EventType} succeeded for '{Subject}'.");

    private static readonly Action<ILogger, string, string?, string, Exception?> _failureLog = LoggerMessage.Define<string, string?, string>(
        LogLevel.Warning, new EventId(3101, "SecurityAuditFailure"), "Security audit: {EventType} failed for '{Subject}' with reason '{FailureReason}'.");

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not ISecurityAuditedRequest auditedRequest)
        {
            return await next(cancellationToken);
        }

        string eventType = auditedRequest.SecurityEventType;
        string? targetSubject = auditedRequest.TargetSubject;
        ISecurityAuditContext context = auditContext ?? DefaultSecurityAuditContext.Instance;

        try
        {
            TResponse response = await next(cancellationToken);

            bool succeeded = response.IsSuccess;
            string? failureReason = !succeeded ? string.Join(", ", response.Errors.Select(error => error.Code)) : null;

            SecurityAuditEvent auditEvent = new(
                EventId: Guid.NewGuid(),
                EventType: eventType,
                SubjectIdentifier: targetSubject,
                ClientIp: context.ClientIp,
                UserAgent: context.UserAgent,
                Succeeded: succeeded,
                FailureReason: failureReason,
                OccurredAtUtc: DateTime.UtcNow);

            await auditSink.RecordAsync(auditEvent, cancellationToken);

            if (logger is not null)
            {
                if (succeeded)
                {
                    _successLog(logger, eventType, targetSubject, null);
                }
                else
                {
                    _failureLog(logger, eventType, targetSubject, failureReason ?? "UnknownFailure", null);
                }
            }

            return response;
        }
        catch (Exception exception)
        {
            SecurityAuditEvent failureEvent = new(
                EventId: Guid.NewGuid(),
                EventType: eventType,
                SubjectIdentifier: targetSubject,
                ClientIp: context.ClientIp,
                UserAgent: context.UserAgent,
                Succeeded: false,
                FailureReason: exception.GetType().Name,
                OccurredAtUtc: DateTime.UtcNow);

            await auditSink.RecordAsync(failureEvent, cancellationToken);

            if (logger is not null)
            {
                _failureLog(logger, eventType, targetSubject, exception.GetType().Name, exception);
            }

            throw;
        }
    }
}
