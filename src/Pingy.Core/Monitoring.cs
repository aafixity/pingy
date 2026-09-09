using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Pingy.Core;

public sealed record PingSample(Guid HostId, long? RoundtripMs, string Status);

public sealed record PingBatch(DateTimeOffset Timestamp, IReadOnlyList<PingSample> Samples);

/// <summary>Runs complete, non-overlapping rounds against a snapshot of the enabled hosts.</summary>
public sealed class PingMonitor
{
    private const int MaximumConcurrency = 32;
    private static readonly byte[] Payload = "pingy connectivity check"u8.ToArray();

    public async Task RunAsync(
        IReadOnlyList<HostEntry> hosts,
        int intervalMs,
        int timeoutMs,
        Action<PingBatch> onBatch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        ArgumentNullException.ThrowIfNull(onBatch);
        ArgumentOutOfRangeException.ThrowIfLessThan(intervalMs, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMs, 1);

        // Changes to the host editor only affect the next session.
        HostEntry[] snapshot = hosts.Where(host => host.Enabled).Select(host => host.Clone()).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.Length == 0)
            return;

        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = MaximumConcurrency
        };

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long started = Stopwatch.GetTimestamp();
            var timestamp = DateTimeOffset.Now;
            var samples = new PingSample[snapshot.Length];

            await Parallel.ForEachAsync(
                Enumerable.Range(0, snapshot.Length),
                parallelOptions,
                async (index, token) =>
                {
                    samples[index] = await SendAsync(snapshot[index], timeoutMs, token).ConfigureAwait(false);
                }).ConfigureAwait(false);

            // Never publish an incomplete row or publish a completed row after cancellation
            // has already been observed. The UI also rejects callbacks from old sessions.
            cancellationToken.ThrowIfCancellationRequested();
            onBatch(new PingBatch(timestamp, Array.AsReadOnly(samples)));

            var remaining = TimeSpan.FromMilliseconds(intervalMs) - Stopwatch.GetElapsedTime(started);
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<PingSample> SendAsync(HostEntry host, int timeoutMs, CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(host.Address, out IPAddress? address))
            return new PingSample(host.Id, null, "InvalidAddress");

        var ping = new Ping();
        Task<PingReply>? pending = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            pending = ping.SendPingAsync(address, timeoutMs, Payload);
            // Windows cannot cancel the native ICMP wait immediately. Stop waiting here;
            // the bounded native request is cleaned up independently when it completes.
            PingReply reply = await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new PingSample(host.Id, reply.Status == IPStatus.Success ? reply.RoundtripTime : null, reply.Status.ToString());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is PingException or SocketException or InvalidOperationException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PingSample(host.Id, null, "PingError");
        }
        finally
        {
            if (pending is { IsCompleted: false })
                _ = DisposeAfterCompletionAsync(ping, pending);
            else
                ping.Dispose();
        }
    }

    private static async Task DisposeAfterCompletionAsync(Ping ping, Task<PingReply> pending)
    {
        try { await pending.ConfigureAwait(false); }
        catch (Exception) { /* Observe an error from a request abandoned on Stop. */ }
        finally { ping.Dispose(); }
    }
}

/// <summary>Per-host statistics for one session. Missing replies count as loss, never as zero ms.</summary>
public sealed class PingStatistics
{
    private double totalMilliseconds;

    public long Sent { get; private set; }
    public long Received { get; private set; }
    public long Lost => Sent - Received;
    public double? AverageMs => Received == 0 ? null : totalMilliseconds / Received;
    public long? MaximumMs { get; private set; }
    public long? MinimumMs { get; private set; }
    public double LossPercent => Sent == 0 ? 0 : Lost * 100.0 / Sent;

    public void Add(PingSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        Sent++;
        if (sample.RoundtripMs is not long milliseconds || milliseconds < 0)
            return;

        Received++;
        totalMilliseconds += milliseconds;
        MaximumMs = MaximumMs.HasValue ? Math.Max(MaximumMs.Value, milliseconds) : milliseconds;
        MinimumMs = MinimumMs.HasValue ? Math.Min(MinimumMs.Value, milliseconds) : milliseconds;
    }
}

public sealed record AdapterInfo(string Name, IReadOnlyList<string> Addresses, IReadOnlyList<string> Gateways);

public static class NetworkInfo
{
    public static IReadOnlyList<AdapterInfo> GetAdapters()
    {
        var results = new List<AdapterInfo>();
        NetworkInterface[] adapters;
        try
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (Exception error) when (error is NetworkInformationException or PlatformNotSupportedException)
        {
            return results;
        }

        foreach (NetworkInterface adapter in adapters)
        {
            try
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                IPInterfaceProperties properties = adapter.GetIPProperties();
                string[] addresses = properties.UnicastAddresses.Select(entry => entry.Address)
                    .Where(IsUsefulAddress).OrderBy(address => address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                    .Select(address => address.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (addresses.Length == 0)
                    continue;

                string[] gateways = properties.GatewayAddresses.Select(entry => entry.Address)
                    .Where(IsUsefulAddress).OrderBy(address => address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                    .Select(address => address.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                results.Add(new AdapterInfo(adapter.Name, Array.AsReadOnly(addresses), Array.AsReadOnly(gateways)));
            }
            catch (Exception error) when (error is NetworkInformationException or SocketException or PlatformNotSupportedException)
            {
                // An interface can disappear while Windows applies network settings.
            }
        }

        return results.OrderBy(adapter => adapter.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static bool IsUsefulAddress(IPAddress address) =>
        address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6 &&
        !IPAddress.IsLoopback(address) && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any);
}
