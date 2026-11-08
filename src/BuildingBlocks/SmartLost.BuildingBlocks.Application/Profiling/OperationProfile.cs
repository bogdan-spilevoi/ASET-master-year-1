using System.Globalization;
using System.Text;

namespace SmartLost.BuildingBlocks.Application.Profiling;

/// <summary>Measurements collected for one Matching request.</summary>
public sealed class OperationProfile
{
    private readonly List<StageMeasurement> _stages = [];
    private readonly Lock _gate = new();

    public OperationProfile(string operationName, string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        OperationName = operationName;
        CorrelationId = correlationId;
    }

    public string OperationName
    {
        get;
    }

    public string CorrelationId
    {
        get;
    }

    public IReadOnlyList<StageMeasurement> Stages
    {
        get
        {
            lock (_gate)
            {
                return [.. _stages];
            }
        }
    }

    public void Add(StageMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        lock (_gate)
        {
            _stages.Add(measurement);
        }
    }

    /// <summary>Formats stages as <c>Name=12.3ms/Succeeded/items=4</c>, separated by semicolons.</summary>
    public string Describe()
    {
        IReadOnlyList<StageMeasurement> stages = Stages;
        if (stages.Count == 0)
        {
            return "none";
        }

        StringBuilder builder = new();
        foreach (StageMeasurement stage in stages)
        {
            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append(CultureInfo.InvariantCulture, $"{stage.Name}={stage.Duration.TotalMilliseconds:F1}ms/{stage.Outcome}");
            if (stage.ItemCount is int count)
            {
                builder.Append(CultureInfo.InvariantCulture, $"/items={count}");
            }

            if (stage.ExceptionType is not null)
            {
                builder.Append(CultureInfo.InvariantCulture, $"/{stage.ExceptionType}");
            }
        }

        return builder.ToString();
    }
}
