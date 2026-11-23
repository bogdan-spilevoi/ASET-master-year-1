using System.Text.Json.Serialization;

namespace SmartLost.ListingsService.Application.Dtos;

[JsonConverter(typeof(JsonStringEnumConverter<MatchChoice>))]
public enum MatchChoice
{
    Accept = 1,
    Reject = 2
}
