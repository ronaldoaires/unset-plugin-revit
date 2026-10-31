using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnSetBim.Core.Api;

/// <summary>JSON options matching the API: snake_case names, unknown fields ignored.</summary>
public static class ApiJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
