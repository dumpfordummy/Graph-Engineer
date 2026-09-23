using System.Text.Json;
using System.Text.Json.Serialization;

namespace GraphEngineering.Api.Providers;

public sealed record CredentialChange(string Action, string? Value = null);
public sealed record ProviderWrite(string Name, string Protocol, string BaseUrl, string ModelId,
    string AuthMode, int TimeoutSeconds, int MaxOutputTokens, bool AllowPrivateNetwork,
    bool AllowInsecureHttp, CredentialChange Credential, bool ConfirmDestinationChange, long? ExpectedRevision = null);
public sealed record ProviderProfile(Guid Id, string Name, long Revision, long ConnectionVersion,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string Protocol, string BaseUrl,
    string ResolvedEndpoint, string ModelId, string AuthMode, int TimeoutSeconds, int MaxOutputTokens,
    bool AllowPrivateNetwork, bool AllowInsecureHttp, bool HasCredential, TestResult? LastTest);
public sealed record TestUsage(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? InputTokens,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? OutputTokens,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? TotalTokens);
public sealed record TestResult(DateTimeOffset TestedAt, long ConnectionVersion, string Category, bool Success,
    long DurationMs, string Message, string? Preview, bool PhraseMatched, string? ObservedModel,
    string? RequestId, TestUsage? Usage);
public sealed record TestRequest(long ExpectedRevision, long ConnectionVersion);
public sealed record DeleteRequest(long ExpectedRevision);

public sealed class ProviderRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public long Revision { get; set; }
    public long ConnectionVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string Protocol { get; set; } = "openai-responses";
    public string BaseUrl { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string AuthMode { get; set; } = "bearer";
    public int TimeoutSeconds { get; set; }
    public int MaxOutputTokens { get; set; }
    public bool AllowPrivateNetwork { get; set; }
    public bool AllowInsecureHttp { get; set; }
    public string? LastTestJson { get; set; }
    public ProviderCredential? Credential { get; set; }

    public ProviderProfile Public() => new(Id, Name, Revision, ConnectionVersion,
        new DateTimeOffset(DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Utc)),
        new DateTimeOffset(DateTime.SpecifyKind(UpdatedAtUtc, DateTimeKind.Utc)), Protocol, BaseUrl,
        BaseUrl.TrimEnd('/') + "/responses", ModelId, AuthMode, TimeoutSeconds, MaxOutputTokens,
        AllowPrivateNetwork, AllowInsecureHttp, Credential is not null,
        LastTestJson is null ? null : JsonSerializer.Deserialize<TestResult>(LastTestJson, ProviderJson.Options));
}

public sealed class ProviderCredential
{
    public Guid ProviderId { get; set; }
    public byte[] Ciphertext { get; set; } = [];
}

internal static class ProviderJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
