using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartLost.BuildingBlocks.Application;
using SmartLost.BuildingBlocks.Application.Behaviors;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Core.Exceptions;
using SmartLost.BuildingBlocks.Core.Results;
using Xunit;

namespace SmartLost.BuildingBlocks.UnitTests;

public sealed class BehaviorTests
{
    private static readonly string[] _validationOrder = ["first-start", "first-end", "second"];
    private static readonly string[] _commitOrder = ["begin", "save", "commit", "dispose"];
    private static readonly string[] _rollbackOrder = ["begin", "rollback", "dispose"];
    private static readonly string[] _saveFailureOrder = ["begin", "save", "rollback", "dispose"];

    [Fact]
    public async Task ValidationRunsSequentiallyAndDoesNotInvokeInvalidHandlers()
    {
        List<string> events = [];
        InlineValidator<ProbeCommand> first = [];
        first.RuleFor(request => request.Name).MustAsync(async (_, _) =>
        {
            events.Add("first-start");
            await Task.Yield();
            events.Add("first-end");
            return false;
        }).WithMessage("Invalid name.").WithErrorCode("probe.invalid");
        InlineValidator<ProbeCommand> second = [];
        second.RuleFor(request => request.Name).MustAsync((_, _) =>
        {
            events.Add("second");
            return Task.FromResult(false);
        }).WithMessage("Invalid name.").WithErrorCode("probe.invalid");
        var behavior = new ValidationBehavior<ProbeCommand, Result<string>>([first, second]);
        bool handlerCalled = false;
        Result<string> result = await behavior.Handle(new ProbeCommand(""), _ =>
        {
            handlerCalled = true;
            return Task.FromResult(Result<string>.Success("unexpected"));
        }, CancellationToken.None);

        Assert.Equal(_validationOrder, events);
        Assert.False(handlerCalled);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("Name", error.PropertyName);
        Assert.Equal("probe.invalid", error.Code);
        Assert.Equal(ErrorKind.Validation, error.Kind);
    }

    [Fact]
    public async Task ValidationAllowsValidRequestsAndPropagatesCancellation()
    {
        var empty = new ValidationBehavior<ProbeCommand, Result>([]);
        Result success = await empty.Handle(new ProbeCommand("valid"), _ => Task.FromResult(Result.Success()), CancellationToken.None);
        Assert.True(success.IsSuccess);

        InlineValidator<ProbeCommand> validator = [];
        validator.RuleFor(request => request.Name).NotEmpty();
        var behavior = new ValidationBehavior<ProbeCommand, Result>([validator]);
        Assert.True((await behavior.Handle(new ProbeCommand("valid"), _ => Task.FromResult(Result.Success()), CancellationToken.None)).IsSuccess);
        Assert.True((await behavior.Handle(new ProbeCommand(""), _ => throw new InvalidOperationException(), CancellationToken.None)).IsFailure);

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => behavior.Handle(new ProbeCommand("valid"),
            _ => Task.FromResult(Result.Success()), cancellation.Token));
    }

    [Fact]
    public async Task ExceptionMappingHandlesOnlyExplicitDomainErrors()
    {
        var behavior = new ExceptionToResultBehavior<ProbeCommand, Result<string>>();
        var request = new ProbeCommand("valid");
        Result<string> success = await behavior.Handle(request, _ => Task.FromResult(Result<string>.Success("ok")), CancellationToken.None);
        Assert.Equal("ok", success.Value);
        Error error = new("probe.conflict", "Exists.", ErrorKind.Conflict);
        DomainException exception = new(error);
        Assert.Equal(error.Message, exception.Message);
        Result<string> failure = await behavior.Handle(request, _ => throw exception, CancellationToken.None);
        Assert.Equal(error, Assert.Single(failure.Errors));
        var nongeneric = new ExceptionToResultBehavior<ProbeCommand, Result>();
        Assert.True((await nongeneric.Handle(request, _ => throw exception, CancellationToken.None)).IsFailure);

        InvalidOperationException unexpected = new("programming defect");
        Assert.Same(unexpected, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(request, _ => throw unexpected, CancellationToken.None)));
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => behavior.Handle(request,
            _ => Task.FromCanceled<Result<string>>(cancellation.Token), cancellation.Token));
    }


    [Fact]
    public async Task TransactionCommitsSuccessfulCommandsAndSkipsQueries()
    {
        RecordingUnitOfWork unitOfWork = new();
        TransactionBehavior<ProbeCommand, Result<string>> behavior = CreateTransactionBehavior(unitOfWork);
        Assert.True((await behavior.Handle(new ProbeCommand("ok"), _ => Task.FromResult(Result<string>.Success("ok")), CancellationToken.None)).IsSuccess);
        Assert.Equal(_commitOrder, unitOfWork.Events);

        unitOfWork.Events.Clear();
        var queryBehavior = new TransactionBehavior<ProbeQuery, Result<string>>(unitOfWork, NullLogger<TransactionBehavior<ProbeQuery, Result<string>>>.Instance);
        Assert.True((await queryBehavior.Handle(new ProbeQuery(), _ => Task.FromResult(Result<string>.Success("ok")), CancellationToken.None)).IsSuccess);
        Assert.Empty(unitOfWork.Events);
    }


    [Fact]
    public async Task TransactionRollsBackFailuresAndExceptionsWithoutMaskingOriginalErrors()
    {
        RecordingUnitOfWork unitOfWork = new();
        TransactionBehavior<ProbeCommand, Result<string>> behavior = CreateTransactionBehavior(unitOfWork);
        Error error = new("probe.invalid", "Invalid.", ErrorKind.Validation);
        Result<string> failure = await behavior.Handle(new ProbeCommand("invalid"), _ => Task.FromResult(Result<string>.Failure(error)), CancellationToken.None);
        Assert.True(failure.IsFailure);
        Assert.Equal(_rollbackOrder, unitOfWork.Events);

        unitOfWork.Events.Clear();
        InvalidOperationException original = new("handler failed");
        Assert.Same(original, await Assert.ThrowsAsync<InvalidOperationException>(() => behavior.Handle(
            new ProbeCommand("ok"), _ => throw original, CancellationToken.None)));
        Assert.Equal(_rollbackOrder, unitOfWork.Events);

        unitOfWork.Events.Clear();
        unitOfWork.SaveException = original;
        unitOfWork.RollbackException = new InvalidOperationException("rollback failed");
        Assert.Same(original, await Assert.ThrowsAsync<InvalidOperationException>(() => behavior.Handle(
            new ProbeCommand("ok"), _ => Task.FromResult(Result<string>.Success("ok")), CancellationToken.None)));
        Assert.Equal(_saveFailureOrder, unitOfWork.Events);
    }


    [Fact]
    public async Task RegisteredPipelineValidatesBeforeOpeningTransactions()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddBuildingBlocksApplication(typeof(ProbeCommand).Assembly, useTransactions: true);
        RecordingUnitOfWork unitOfWork = new();
        services.AddSingleton<IUnitOfWork>(unitOfWork);
        using ServiceProvider provider = services.BuildServiceProvider();
        ISender sender = provider.GetRequiredService<ISender>();
        Result<string> invalid = await sender.Send(new ProbeCommand(""));
        Assert.True(invalid.IsFailure);
        Assert.Empty(unitOfWork.Events);
        Result<string> success = await sender.Send(new ProbeCommand("ok"));
        Assert.Equal("ok", success.Value);
        Assert.Equal(_commitOrder, unitOfWork.Events);

        unitOfWork.Events.Clear();
        Result<string> domainFailure = await sender.Send(new ProbeCommand("domain-error"));
        Assert.True(domainFailure.IsFailure);
        Assert.Equal(_rollbackOrder, unitOfWork.Events);
    }

    [Fact]
    public async Task TransactionCancellationRollsBackWithAnUncancelledToken()
    {
        RecordingUnitOfWork unitOfWork = new();
        TransactionBehavior<ProbeCommand, Result<string>> behavior = CreateTransactionBehavior(unitOfWork);
        using CancellationTokenSource cancellation = new();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => behavior.Handle(new ProbeCommand("ok"), async token =>
        {
            await cancellation.CancelAsync();
            token.ThrowIfCancellationRequested();
            return Result<string>.Success("unexpected");
        }, cancellation.Token));
        Assert.Equal(_rollbackOrder, unitOfWork.Events);
    }

    private static TransactionBehavior<ProbeCommand, Result<string>> CreateTransactionBehavior(RecordingUnitOfWork unitOfWork)
    {
        return new(unitOfWork, NullLogger<TransactionBehavior<ProbeCommand, Result<string>>>.Instance);
    }

    private sealed record ProbeQuery : IRequest<Result<string>>;

    private sealed class RecordingUnitOfWork : IUnitOfWork, ITransaction
    {
        public List<string> Events { get; } = [];

        public Exception? SaveException
        {
            get; set;
        }

        public Exception? RollbackException
        {
            get; set;
        }

        public Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken)
        {
            Events.Add("begin");
            return Task.FromResult<ITransaction>(this);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Events.Add("save");
            return SaveException is null ? Task.CompletedTask : Task.FromException(SaveException);
        }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            Events.Add("commit");
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken)
        {
            Events.Add("rollback");
            Assert.False(cancellationToken.IsCancellationRequested);
            return RollbackException is null ? Task.CompletedTask : Task.FromException(RollbackException);
        }

        public ValueTask DisposeAsync()
        {
            Events.Add("dispose");
            return ValueTask.CompletedTask;
        }
    }
}

public sealed record ProbeCommand(string Name) : IRequest<Result<string>>, ITransactionalCommand;

public sealed class ProbeValidator : AbstractValidator<ProbeCommand>
{
    public ProbeValidator()
    {
        RuleFor(request => request.Name).NotEmpty();
    }
}

public sealed class ProbeHandler : IRequestHandler<ProbeCommand, Result<string>>
{
    public Task<Result<string>> Handle(ProbeCommand request, CancellationToken cancellationToken)
    {
        if (request.Name == "domain-error")
        {
            throw new DomainException(new Error("probe.invalid", "Invalid.", ErrorKind.Validation));
        }

        return Task.FromResult(Result<string>.Success(request.Name));
    }
}
