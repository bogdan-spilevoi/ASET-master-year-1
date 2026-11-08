using FluentValidation;

namespace SmartLost.MatchingService.Application.Commands.PrepareImageEmbedding;

public sealed class PrepareImageEmbeddingCommandValidator : AbstractValidator<PrepareImageEmbeddingCommand>
{
    public PrepareImageEmbeddingCommandValidator()
    {
        RuleFor(command => command.TaskId).NotEmpty().WithErrorCode("matching.task_id_required");
    }
}
