using System.Security.Cryptography;
using System.Text;

namespace GraphEngineering.Api.Security;

// Mutex ownership is thread-affine. A dedicated thread holds it for the host's
// lifetime, including asynchronous startup/disposal; process death abandons it.
public sealed class DataDirectoryLease : IDisposable
{
    private readonly ManualResetEventSlim release = new(false);
    private readonly Thread holder;
    private int disposed;

    public DataDirectoryLease(string directory)
    {
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)).ToUpperInvariant();
        var name = "Local\\GraphEngineering.Data." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        using var ready = new ManualResetEventSlim(false);
        Exception? failure = null;
        holder = new Thread(() =>
        {
            Mutex? mutex = null;
            var acquired = false;
            try
            {
                mutex = new Mutex(false, name);
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("Another Graph Engineering backend owns this data directory.");
            }
            catch (Exception error) { failure = error; }
            finally { ready.Set(); }
            if (!acquired) { mutex?.Dispose(); return; }
            try { release.Wait(); }
            finally { mutex!.ReleaseMutex(); mutex.Dispose(); }
        }) { IsBackground = true, Name = "Graph Engineering data directory owner" };
        holder.Start();
        ready.Wait();
        if (failure is not null) { holder.Join(); release.Dispose(); throw failure; }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        release.Set();
        holder.Join();
        release.Dispose();
    }
}
