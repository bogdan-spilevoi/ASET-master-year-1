namespace SmartLost.BuildingBlocks.Application.Profiling;

public interface IProfilingContext
{
    OperationProfile? Current
    {
        get;
    }

    IDisposable Begin(OperationProfile profile);
}
