using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;

namespace GraphEngineering.Api.Providers;

public sealed class ResponsesProbe(ProviderDestinationPolicy policy)
{
    public const int MaximumResponseBytes = 256 * 1024;
    public const string SyntheticInput = "Reply with GE_CONNECTION_OK.";

    public async Task<TestResult> TestAsync(ProviderRecord profile, string? credential, CancellationToken cancellation)
    {
        var watch = Stopwatch.StartNew();
        var at = DateTimeOffset.UtcNow;
        TestResult Result(string category, string message, string? preview = null, bool phrase = false,
            string? model = null, string? requestId = null, TestUsage? usage = null) =>
            new(at, profile.ConnectionVersion, category, category == "success", watch.ElapsedMilliseconds,
                message, preview, phrase, model, requestId, usage);
        try
        {
            var destination = policy.Validate(profile.BaseUrl, profile.AllowPrivateNetwork, profile.AllowInsecureHttp, profile.AuthMode);
            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false, UseCookies = false, UseProxy = false, Credentials = null,
                ConnectTimeout = TimeSpan.FromSeconds(Math.Min(10, profile.TimeoutSeconds)),
                MaxResponseHeadersLength = 16, MaxConnectionsPerServer = 1,
                ConnectCallback = (context, token) => policy.ConnectAsync(context, profile, token)
            };
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(profile.TimeoutSeconds));
            using var request = new HttpRequestMessage(HttpMethod.Post, destination.AbsoluteUri.TrimEnd('/') + "/responses")
            {
                Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                Content = JsonContent.Create(new { model = profile.ModelId, input = SyntheticInput, stream = false, store = false, max_output_tokens = profile.MaxOutputTokens })
            };
            if (profile.AuthMode == "bearer") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var requestId = Safe(response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null, credential, 120);
            if ((int)response.StatusCode is >= 300 and < 400)
                return Result("rejected_request", "The provider redirected the request. Redirects are disabled; configure the final approved API base URL.", requestId: requestId);
            if (!response.IsSuccessStatusCode)
            {
                var (category, message) = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ("auth_access", "The provider rejected authentication or access. Check the saved key and permissions."),
                    HttpStatusCode.TooManyRequests => ("rate_limit", "The provider reported a rate or quota limit. No retry was sent."),
                    _ when (int)response.StatusCode is >= 400 and < 500 => ("rejected_request", "The provider rejected the request. Check its model, Responses protocol, and support for store=false."),
                    _ => ("connectivity", "The provider could not complete the request. No retry was sent.")
                };
                return Result(category, message, requestId: requestId);
            }
            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                return Result("unexpected_payload", "The provider response exceeded the 256 KiB limit.");
            if (response.Content.Headers.ContentType?.MediaType is not ("application/json") &&
                response.Content.Headers.ContentType?.MediaType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) != true)
                return Result("unexpected_payload", "The provider did not return a JSON Responses payload.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            while (true)
            {
                var count = await stream.ReadAsync(chunk, timeout.Token);
                if (count == 0) break;
                if (buffer.Length + count > MaximumResponseBytes)
                    return Result("unexpected_payload", "The provider response exceeded the 256 KiB limit.");
                buffer.Write(chunk, 0, count);
            }
            using var json = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            var root = json.RootElement;
            if (HasDuplicateProperties(root)) return Result("unexpected_payload", "The provider returned ambiguous JSON with duplicate properties.");
            if (root.ValueKind != JsonValueKind.Object || Text(root, "object") != "response" ||
                !root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String)
                return Result("unexpected_payload", "The provider did not return a valid Responses object.");
            if (status.GetString() != "completed")
                return Result("incomplete_output", "The provider response did not complete. This does not verify text capability.", requestId: requestId);
            if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
                return Result("unexpected_payload", "The completed response did not contain an output array.");
            var text = new StringBuilder();
            foreach (var item in output.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || Text(item, "type") != "message" || Text(item, "role") != "assistant" ||
                    Text(item, "status") != "completed" || !item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
                foreach (var part in content.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.Object && Text(part, "type") == "output_text" && Text(part, "text") is { } value)
                    { if (text.Length > 0) text.Append('\n'); text.Append(value); }
                }
            }
            if (string.IsNullOrWhiteSpace(text.ToString()))
                return Result("incomplete_output", "The completed response contained no nonempty assistant output text.", requestId: requestId);
            TestUsage? usage = null;
            if (root.TryGetProperty("usage", out var supplied) && supplied.ValueKind == JsonValueKind.Object)
            {
                var input = Count(supplied, "input_tokens"); var outputCount = Count(supplied, "output_tokens"); var total = Count(supplied, "total_tokens");
                if (input.HasValue || outputCount.HasValue || total.HasValue) usage = new(input, outputCount, total);
            }
            var answer = text.ToString();
            return Result("success", "Completed assistant text was received for this saved connection version.",
                Safe(answer, credential, 500), answer.Contains("GE_CONNECTION_OK", StringComparison.Ordinal),
                Safe(Text(root, "model"), credential, 200), requestId, usage);
        }
        catch (DestinationException) { return Result("invalid_configuration", "The destination is not permitted by its saved approvals or address policy."); }
        catch (OperationCanceledException) { return Result(cancellation.IsCancellationRequested ? "cancelled" : "timeout", cancellation.IsCancellationRequested ? "The local request was cancelled. The provider may still finish and charge for it." : "The connection test timed out. The provider may still finish and charge for it."); }
        catch (HttpRequestException error)
        {
            if (Contains<DestinationException>(error)) return Result("invalid_configuration", "The resolved destination is not permitted by its saved approvals or address policy.");
            if (error.HttpRequestError == HttpRequestError.SecureConnectionError || Contains<AuthenticationException>(error))
                return Result("tls", "TLS certificate verification or the secure handshake failed. Certificate verification remains enabled.");
            return Result("connectivity", "The provider could not be reached. Check DNS, the port, and service availability. Ambient proxies are not used.");
        }
        catch (JsonException) { return Result("unexpected_payload", "The provider returned malformed or overly nested JSON."); }
        catch (IOException) { return Result("connectivity", "The provider connection ended before its response completed."); }
    }

    private static bool Contains<T>(Exception error) where T : Exception => error is T || error.InnerException is not null && Contains<T>(error.InnerException);
    private static bool HasDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().Any(HasDuplicateProperties);
        if (value.ValueKind != JsonValueKind.Object) return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        return value.EnumerateObject().Any(property => !names.Add(property.Name) || HasDuplicateProperties(property.Value));
    }
    private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out var member) && member.ValueKind == JsonValueKind.String ? member.GetString() : null;
    private static long? Count(JsonElement value, string name) => value.TryGetProperty(name, out var member) && member.ValueKind == JsonValueKind.Number && member.TryGetInt64(out var number) && number is >= 0 and <= 9007199254740991 ? number : null;
    internal static string? Safe(string? value, string? credential, int limit)
    {
        if (value is null) return null;
        // Redact before truncating, including secrets straddling the eventual preview boundary.
        if (!string.IsNullOrEmpty(credential)) value = value.Replace(credential, "[redacted]", StringComparison.Ordinal);
        value = new string(value.Where(character => !char.IsControl(character) || character is '\n' or '\t').ToArray());
        // Removing control characters can reassemble an echoed key that did not match the first pass.
        if (!string.IsNullOrEmpty(credential)) value = value.Replace(credential, "[redacted]", StringComparison.Ordinal);
        return value.Length <= limit ? value : value[..limit];
    }
}
