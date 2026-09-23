using System.Net;
using System.Net.Sockets;

namespace GraphEngineering.Api.Providers;

public sealed class DestinationException(string message) : Exception(message);

public sealed class ProviderDestinationPolicy(IConfiguration configuration)
{
    private readonly HashSet<int> selfPorts = (configuration["urls"] ?? "http://127.0.0.1:5080")
        .Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Append(configuration["GRAPH_ENGINEERING_BROWSER_ORIGIN"] ?? "http://127.0.0.1:5173")
        .Select(value => new Uri(value).Port).ToHashSet();

    public async Task<Uri> ValidateForSaveAsync(string? value, bool allowPrivate, bool allowHttp, string authMode, CancellationToken token)
    {
        var uri = Validate(value, allowPrivate, allowHttp, authMode);
        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out _)) return uri;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host, timeout.Token);
            if (addresses.Length == 0) throw new DestinationException("The provider host did not resolve to an address.");
            foreach (var address in addresses) ValidateAddress(address, uri.Port, allowPrivate, uri.Scheme == "http" || authMode == "none");
            return uri;
        }
        catch (SocketException) { throw new DestinationException("The provider host could not be resolved. Check its name and local DNS before saving."); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new DestinationException("The provider host resolution timed out. Check local DNS before saving."); }
    }

    public Uri Validate(string? value, bool allowPrivate, bool allowHttp, string authMode)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || value != value.Trim() ||
            value.Any(char.IsControl) || value.Contains('\\') ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            value.Contains('?') || value.Contains('#') || uri.HostNameType == UriHostNameType.Unknown)
            throw new DestinationException("Enter an HTTP(S) API base URL without user information, query, or fragment.");

        // Uri normalizes dot segments; inspect the original path before that normalization can hide them.
        var authorityEnd = value.IndexOf('/', value.IndexOf("://", StringComparison.Ordinal) + 3);
        var rawPath = authorityEnd < 0 ? "" : value[authorityEnd..];
        var decoded = Uri.UnescapeDataString(rawPath);
        if (decoded.Contains('\\') || decoded.Contains('%') || decoded.Any(char.IsControl) ||
            decoded.Split('/').Any(part => part is "." or "..") ||
            rawPath.Contains("%2f", StringComparison.OrdinalIgnoreCase) ||
            decoded.TrimEnd('/').EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
            throw new DestinationException("Use the API base prefix, not a responses method URL or an encoded path traversal.");
        if (uri.Scheme == "http" && (!allowPrivate || !allowHttp))
            throw new DestinationException("HTTP requires explicit private-network approval and the separate unencrypted HTTP acknowledgment.");
        if (authMode == "none" && !allowPrivate)
            throw new DestinationException("No-auth is allowed only for an explicitly approved private or loopback destination.");
        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address))
            ValidateAddress(address, uri.Port, allowPrivate, uri.Scheme == "http" || authMode == "none");
        return new Uri(uri.GetLeftPart(UriPartial.Path).TrimEnd('/'));
    }

    public void ValidateAddress(IPAddress address, int port, bool allowPrivate, bool requirePrivate)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        var ipv4 = address.AddressFamily == AddressFamily.InterNetwork;
        var forbidden = ipv4
            ? bytes[0] == 0 || bytes[0] >= 224 || bytes[0] == 169 && bytes[1] == 254
            : address.Equals(IPAddress.IPv6Any) || address.IsIPv6Multicast || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal;
        if (forbidden || address.Equals(IPAddress.Parse("fd00:ec2::254")) ||
            address.Equals(IPAddress.Parse("100.100.100.200")) || address.Equals(IPAddress.Parse("168.63.129.16")))
            throw new DestinationException("Unspecified, multicast, link-local, and cloud metadata/control destinations are not permitted.");
        var local = IPAddress.IsLoopback(address);
        var isPrivate = local || (ipv4
            ? bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 100 && bytes[1] is >= 64 and <= 127
            : (bytes[0] & 0xfe) == 0xfc);
        if (local && selfPorts.Contains(port)) throw new DestinationException("The application's API and frontend cannot be provider destinations.");
        if (isPrivate && !allowPrivate) throw new DestinationException("This destination requires explicit private-network approval.");
        if (!isPrivate && requirePrivate) throw new DestinationException("HTTP and no-auth are permitted only for private or loopback destinations.");
    }

    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, ProviderRecord profile, CancellationToken token)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
        return await ConnectResolvedAsync(addresses, context.DnsEndPoint.Port, profile, token);
    }

    public async ValueTask<Stream> ConnectResolvedAsync(IPAddress[] addresses, int port, ProviderRecord profile, CancellationToken token)
    {
        if (addresses.Length == 0) throw new SocketException((int)SocketError.HostNotFound);
        // Reject the whole DNS answer if any address violates policy, then dial those exact validated IPs.
        foreach (var address in addresses)
            ValidateAddress(address, port, profile.AllowPrivateNetwork, profile.AuthMode == "none" || new Uri(profile.BaseUrl).Scheme == "http");
        SocketException? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException error) { socket.Dispose(); lastError = error; }
            catch { socket.Dispose(); throw; }
        }
        throw lastError!;
    }
}
