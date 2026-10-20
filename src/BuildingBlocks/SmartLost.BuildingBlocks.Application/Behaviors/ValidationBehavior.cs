using FluentValidation;
using FluentValidation.Results;
using MediatR;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.BuildingBlocks.Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IResult<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var context = new ValidationContext<TRequest>(request);
        List<Error> errors = [];
        // Validators may share a scoped DbContext, so they must not run concurrently.
        foreach (IValidator<TRequest> validator in validators)
        {
            ValidationResult result = await validator.ValidateAsync(context, cancellationToken);
            errors.AddRange(result.Errors.Select(failure =>
                new Error(failure.ErrorCode, failure.ErrorMessage, ErrorKind.Validation, failure.PropertyName)));
        }

        return errors.Count > 0 ? TResponse.Failure([.. errors.Distinct()]) : await next(cancellationToken);
    }
}
