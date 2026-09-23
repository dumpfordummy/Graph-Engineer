using System.Text.Json;
using GraphEngineering.Api.Providers;

namespace GraphEngineering.Api.Runs;

internal static class RunPrivacy
{
    internal static bool ContainsCredential(JsonElement value, IReadOnlyCollection<string> activeCredentials) => value.ValueKind switch
    {
        JsonValueKind.String => ResponsesProbe.ContainsSensitive(value.GetString(), activeCredentials),
        JsonValueKind.Object => value.EnumerateObject().Any(property => ResponsesProbe.ContainsSensitive(property.Name, activeCredentials) || ContainsCredential(property.Value, activeCredentials)),
        JsonValueKind.Array => value.EnumerateArray().Any(item => ContainsCredential(item, activeCredentials)),
        _ => ResponsesProbe.ContainsSensitive(value.GetRawText(), activeCredentials)
    };
}
