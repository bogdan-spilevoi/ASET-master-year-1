using MediatR;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Exceptions;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Domain.Errors;

namespace SmartLost.MatchingService.Application.Queries.AnalyzeListingPhoto;

public sealed class AnalyzePhotoContentQueryHandler(IPhotoAnalyzer analyzer)
    : IRequestHandler<AnalyzePhotoContentQuery, Result<PhotoAnalysis>>
{
    public async Task<Result<PhotoAnalysis>> Handle(AnalyzePhotoContentQuery request, CancellationToken cancellationToken)
    {
        try
        {
            PhotoAnalysis analysis = await analyzer.AnalyzeAsync(request.Photo, cancellationToken);
            return Result<PhotoAnalysis>.Success(new PhotoAnalysis(
                Array.AsReadOnly(analysis.Objects.Select(item => item with
                {
                    Colors = Array.AsReadOnly(item.Colors.ToArray()),
                    VisibleText = Array.AsReadOnly(item.VisibleText.ToArray()),
                    DistinctiveFeatures = Array.AsReadOnly(item.DistinctiveFeatures.ToArray()),
                    Tags = Array.AsReadOnly(item.Tags.ToArray())
                }).ToArray()), Array.AsReadOnly(analysis.Uncertainties.ToArray())));
        }
        catch (TimeoutException)
        {
            return Result<PhotoAnalysis>.Failure(MatchingErrors.InferenceTimedOut);
        }
        catch (AiInferenceException)
        {
            return Result<PhotoAnalysis>.Failure(MatchingErrors.InferenceUnavailable);
        }
    }
}
