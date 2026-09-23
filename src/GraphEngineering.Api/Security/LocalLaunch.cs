using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace GraphEngineering.Api.Security;

public sealed class LocalLaunch : IDisposable
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public HashSet<string> Hosts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Origins { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string TokenPath { get; }
    private readonly byte[] tokenHash;
    private readonly FileStream ownership;
    private readonly DataDirectoryLease lease;
    private readonly object attemptLock = new();
    private DateTimeOffset windowStart = DateTimeOffset.UtcNow;
    private int attempts;
    private int disposed;

    public LocalLaunch(IConfiguration configuration)
    {
        if (configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
            throw new InvalidOperationException("Kestrel endpoint overrides are not supported. Configure loopback addresses through urls only.");
        var urls = configuration["urls"] ?? "http://127.0.0.1:5080";
        foreach (var url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries)) AddOrigin(url);
        AddOrigin(configuration["GRAPH_ENGINEERING_BROWSER_ORIGIN"] ?? "http://127.0.0.1:5173");
        lease = new DataDirectoryLease(DataDirectory(configuration));
        try
        {
            // Ownership precedes directory ACLs, token rotation, migrations, and recovery.
            Directory.CreateDirectory(DataDirectory(configuration));
            var directory = Directory.CreateDirectory(Path.Combine(DataDirectory(configuration), "runtime"));
            TokenPath = Path.Combine(directory.FullName, "pairing-token.txt");
            // The file handle also excludes alternate filesystem aliases of the same directory.
            ownership = new FileStream(Path.Combine(directory.FullName, "instance.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            RestrictedDirectory(directory.FullName);
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            tokenHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
            RestrictExistingToken(TokenPath);
            File.WriteAllText(TokenPath, token);
        }
        catch { ownership?.Dispose(); lease.Dispose(); throw; }
    }

    private void AddOrigin(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0 ||
            !(uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
              IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address)))
            throw new InvalidOperationException("Application origins must be exact loopback HTTP(S) origins.");
        Hosts.Add(uri.Authority);
        Origins.Add(uri.GetLeftPart(UriPartial.Authority));
    }

    public bool AllowPairingAttempt()
    {
        lock (attemptLock)
        {
            if (DateTimeOffset.UtcNow - windowStart >= TimeSpan.FromMinutes(1))
            {
                windowStart = DateTimeOffset.UtcNow;
                attempts = 0;
            }
            return ++attempts <= 10;
        }
    }

    public bool Matches(string token) => token.Length == 64 && CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)), tokenHash);

    public static string DataDirectory(IConfiguration configuration) => Path.GetFullPath(
        configuration["GRAPH_ENGINEERING_DATA_DIR"] ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GraphEngineering"));

    public static DirectoryInfo RestrictedDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Local protected storage requires Windows.");
        var owner = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Windows user identity unavailable.");
        var security = new DirectorySecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        var directory = new DirectoryInfo(path);
        if (!directory.Exists) directory.Create(security);
        else directory.SetAccessControl(security);
        return directory;
    }

    private static void RestrictExistingToken(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows protected storage is required.");
        if (!File.Exists(path)) return; // New files inherit only the already-restricted directory's owner ACE.
        var owner = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Windows user identity unavailable.");
        var security = new FileSecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        // Only our own launch owns this file while the exclusive instance handle is held.
        try { File.Delete(TokenPath); }
        finally { ownership.Dispose(); lease.Dispose(); }
    }
}
