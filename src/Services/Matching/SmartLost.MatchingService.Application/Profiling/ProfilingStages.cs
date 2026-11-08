namespace SmartLost.MatchingService.Application.Profiling;

public static class ProfilingStages
{
    public const string CandidateSelection = "CandidateSelection";
    public const string RelevanceRanking = "RelevanceRanking";
    public const string ImageEmbedding = "Dinov3.ImageEmbedding";
    public const string PhotoAnalysis = "Qwen3Vl.PhotoAnalysis";
}
