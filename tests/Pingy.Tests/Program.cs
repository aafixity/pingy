using System.Diagnostics;
using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Pingy.Core;

if (args.Length > 0)
{
    if (args.Length != 3 || args[0] != "--portable-smoke")
    {
        Console.Error.WriteLine("Usage: Pingy.Tests [--portable-smoke <exePath> <workDir>]");
        return 2;
    }
    try
    {
        await PortableExecutableSmoke(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
        Console.WriteLine("PASS Published executable, portable profile, and reembedded executable launch successfully.");
        return 0;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"FAIL Portable executable smoke: {error}");
        return 1;
    }
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Configuration roundtrip and deep clone", Sync(ConfigRoundtrip)),
    ("Upgrade removes factory samples and preserves customized hosts", Sync(MigrateSamples)),
    ("Reject invalid configuration", Sync(RejectInvalidConfig)),
    ("Merge preserves existing choices and deduplicates canonical IPs", Sync(MergeConfig)),
    ("Save/load and invalid-save preservation", Sync(StoreConfig)),
    ("Portable copy and reembedding preserve executable bytes", Sync(PortableRoundtrip)),
    ("Corrupt portable trailer sizes and text are rejected", Sync(PortableCorruption)),
    ("Loss is excluded from latency statistics", Sync(Statistics)),
    ("Adapter addresses are parseable", Sync(Adapters)),
    ("Loopback ping and cancellation stop callbacks", PingLoopback),
    ("Cancelled and empty sessions do not publish rows", EmptyAndCancelled),
    ("Host selection is snapshotted and rows stay ordered", SnapshotAndOrdering),
    ("Cancellation while waiting for a reply finishes promptly", CancelPendingPing)
};

int failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception error)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {error}");
    }
}

Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
return failed == 0 ? 0 : 1;

static Func<Task> Sync(Action action) => () => { action(); return Task.CompletedTask; };

static async Task PortableExecutableSmoke(string executablePath, string directory)
{
    Directory.CreateDirectory(directory);
    using var sourceStream = File.OpenRead(executablePath);
    byte[] sourceHash = SHA256.HashData(sourceStream);
    long sourceLength = sourceStream.Length;
    sourceStream.Close();
    AppConfig original = await LaunchAndReadSeed(executablePath, Path.Combine(directory, "original-seed.json"));

    AppConfig expected = original.Clone();
    expected.Hosts = [Host("Embedded smoke", "127.0.0.2")];
    string firstPath = Path.Combine(directory, "pingy-smoke.exe");
    PortableConfig.WriteCopy(executablePath, firstPath, expected);
    AppConfig first = await LaunchAndReadSeed(firstPath, Path.Combine(directory, "embedded-seed.json"));
    True(first.ProfileId != original.ProfileId, "A portable copy must receive a new profile identity.");
    expected.ProfileId = first.ProfileId;
    Equal(ConfigCodec.Serialize(expected), ConfigCodec.Serialize(first));
    Equal(ConfigCodec.Serialize(PortableConfig.Read(firstPath)!), ConfigCodec.Serialize(first));

    string secondPath = Path.Combine(directory, "pingy-smoke-reembedded.exe");
    PortableConfig.WriteCopy(firstPath, secondPath, first);
    AppConfig second = await LaunchAndReadSeed(secondPath, Path.Combine(directory, "reembedded-seed.json"));
    True(second.ProfileId != first.ProfileId, "A reembedded copy must receive a fresh profile identity.");
    expected.ProfileId = second.ProfileId;
    Equal(ConfigCodec.Serialize(expected), ConfigCodec.Serialize(second));
    Equal(new FileInfo(firstPath).Length, new FileInfo(secondPath).Length);
    True(new FileInfo(firstPath).Length > sourceLength, "The profile must be appended to the original published executable.");

    AppConfig originalAfterCopying = await LaunchAndReadSeed(executablePath, Path.Combine(directory, "original-seed-after.json"));
    Equal(ConfigCodec.Serialize(original), ConfigCodec.Serialize(originalAfterCopying));
    using var unchangedSource = File.OpenRead(executablePath);
    True(sourceHash.SequenceEqual(SHA256.HashData(unchangedSource)), "The published source executable must remain byte-for-byte unchanged.");
    Console.WriteLine($"Verified: original {sourceLength:N0} bytes, embedded {new FileInfo(firstPath).Length:N0} bytes, reembedded {new FileInfo(secondPath).Length:N0} bytes.");
}

static async Task<AppConfig> LaunchAndReadSeed(string executablePath, string outputPath)
{
    File.Delete(outputPath);
    var start = new ProcessStartInfo(executablePath)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden,
        WorkingDirectory = Path.GetDirectoryName(executablePath)!
    };
    start.ArgumentList.Add("--write-embedded-config");
    start.ArgumentList.Add(outputPath);
    using var process = Process.Start(start) ?? throw new Exception("Could not start the published executable.");
    try
    {
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
    }
    catch (TimeoutException)
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
        throw new Exception($"Published executable did not finish within 30 seconds: {executablePath}");
    }
    Equal(0, process.ExitCode);
    True(File.Exists(outputPath), "The published executable must write its embedded profile.");
    return ConfigCodec.Parse(File.ReadAllText(outputPath));
}

static HostEntry Host(string name = "Main router", string address = "192.0.2.10", bool enabled = true) => new()
{
    Id = Guid.NewGuid(), Name = name, Address = address, Group = "Office", Description = "Optional notes", Enabled = enabled
};

static AppConfig Config(params HostEntry[] hosts) => new()
{
    Version = 1, ProfileId = Guid.NewGuid(), IntervalMs = 1000, TimeoutMs = 700, Hosts = hosts.ToList()
};

static void ConfigRoundtrip()
{
    var original = Config(Host("Сервер Київ", "2001:db8::1"), Host("Disabled", "192.0.2.11", false));
    AppConfig parsed = ConfigCodec.Parse(ConfigCodec.Serialize(original));
    Equal(original.ProfileId, parsed.ProfileId);
    Equal(original.IntervalMs, parsed.IntervalMs);
    Equal(original.TimeoutMs, parsed.TimeoutMs);
    Equal(2, parsed.Hosts.Count);
    Equal(original.Hosts[0].Id, parsed.Hosts[0].Id);
    Equal("Сервер Київ", parsed.Hosts[0].Name);
    Equal("Optional notes", parsed.Hosts[0].Description);
    Equal(false, parsed.Hosts[1].Enabled);

    AppConfig copy = original.Clone();
    copy.Hosts[0].Name = "Changed";
    copy.Hosts.RemoveAt(1);
    Equal("Сервер Київ", original.Hosts[0].Name);
    Equal(2, original.Hosts.Count);

    var normalizable = Config(Host("  Router  ", "2001:0db8:0000:0000:0000:0000:0000:0001"));
    normalizable.Hosts[0].Group = "  ";
    ConfigCodec.Validate(normalizable);
    Equal("Router", normalizable.Hosts[0].Name);
    Equal("2001:db8::1", normalizable.Hosts[0].Address);
    True(!string.IsNullOrWhiteSpace(normalizable.Hosts[0].Group), "An empty group should receive a display name.");
}

static void MigrateSamples()
{
    using var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Pingy.Tests.legacy-defaults.json")!;
    using var reader = new StreamReader(stream);
    string legacy = reader.ReadToEnd();
    var config = ConfigCodec.Parse(legacy);
    var id = config.ProfileId;
    config.Hosts[1].Enabled = true;
    config.Hosts.Add(Host("My server", "10.10.10.12"));
    Equal(3, ProfileMigration.RemoveUnmodifiedSamples(config));
    Equal(1, config.Hosts.Count);
    Equal("My server", config.Hosts[0].Name);
    Equal(id, config.ProfileId);
    Equal(0, ProfileMigration.RemoveUnmodifiedSamples(config));

    foreach (Action<HostEntry> customize in new Action<HostEntry>[]
    {
        h => h.Name = "Office", h => h.Address = "10.20.30.40",
        h => h.Group = "Custom", h => h.Description = "My notes"
    })
    {
        var customized = ConfigCodec.Parse(legacy);
        customize(customized.Hosts[1]);
        Equal(2, ProfileMigration.RemoveUnmodifiedSamples(customized));
        Equal(1, customized.Hosts.Count);
    }
}

static void RejectInvalidConfig()
{
    Throws<FormatException>(() => ConfigCodec.Parse("not json"));
    Throws<FormatException>(() => ConfigCodec.Parse("null"));
    foreach (Action<AppConfig> corrupt in new Action<AppConfig>[]
    {
        config => config.Version = 99,
        config => config.IntervalMs = 0,
        config => config.TimeoutMs = 0,
        config => config.IntervalMs = 60001,
        config => config.TimeoutMs = 10001,
        config => config.Hosts[0].Address = "https://example.com",
        config => config.Hosts[0].Address = "999.10.10.10",
        config => config.Hosts[0].Name = "   ",
        config => config.Hosts = Enumerable.Range(0, 257).Select(index => Host($"H{index}", $"10.0.{index / 255}.{index % 255}")).ToList()
    })
    {
        var config = Config(Host());
        corrupt(config);
        Throws<FormatException>(() => ConfigCodec.Validate(config));
    }
}

static void MergeConfig()
{
    var existing = Config(Host("Keep me", "2001:db8::1", false));
    var imported = Config(Host("Duplicate", "2001:0db8:0:0:0:0:0:1"), Host("New host", "192.0.2.12"));
    imported.IntervalMs = 2000;
    imported.TimeoutMs = 1500;
    AppConfig merged = ConfigCodec.Merge(existing, imported);
    Equal(2, merged.Hosts.Count);
    Equal(existing.ProfileId, merged.ProfileId);
    Equal(existing.IntervalMs, merged.IntervalMs);
    Equal(existing.TimeoutMs, merged.TimeoutMs);
    Equal(existing.Hosts[0].Id, merged.Hosts[0].Id);
    Equal("Keep me", merged.Hosts[0].Name);
    Equal(false, merged.Hosts[0].Enabled);
    Equal("New host", merged.Hosts[1].Name);
    merged.Hosts[0].Name = "Only in merge";
    Equal("Keep me", existing.Hosts[0].Name);
    Equal(1, existing.Hosts.Count);
    Equal(2, imported.Hosts.Count);
}

static void StoreConfig()
{
    WithTemporaryDirectory(directory =>
    {
        string path = Path.Combine(directory, "nested", "config.json");
        var store = new ConfigStore(path);
        Equal<AppConfig?>(null, store.Load());
        var config = Config(Host());
        store.Save(config);
        Equal(config.ProfileId, store.Load()!.ProfileId);
        byte[] previous = File.ReadAllBytes(path);
        var invalid = config.Clone();
        invalid.Hosts[0].Address = "invalid";
        Throws<FormatException>(() => store.Save(invalid));
        True(previous.SequenceEqual(File.ReadAllBytes(path)), "A rejected save must not damage the existing file.");

        config.Hosts[0].Name = "Saved again";
        store.Save(config);
        Equal("Saved again", store.Load()!.Hosts[0].Name);
        File.WriteAllText(path, "{broken");
        Throws<FormatException>(() => store.Load());
    });
}

static void PortableRoundtrip()
{
    WithTemporaryDirectory(directory =>
    {
        string source = Path.Combine(directory, "source.exe");
        string first = Path.Combine(directory, "first.exe");
        string second = Path.Combine(directory, "second.exe");
        byte[] executable = new byte[8192];
        new Random(417).NextBytes(executable);
        executable[0] = (byte)'M';
        executable[1] = (byte)'Z';
        File.WriteAllBytes(source, executable);
        Equal<AppConfig?>(null, PortableConfig.Read(source));

        var config = Config(Host("Packed server", "192.0.2.24"));
        PortableConfig.WriteCopy(source, first, config);
        var embedded = PortableConfig.Read(first)!;
        True(embedded is not null, "The copied executable must contain an embedded configuration.");
        Equal("Packed server", embedded!.Hosts[0].Name);
        Equal("192.0.2.24", embedded.Hosts[0].Address);
        Equal(config.IntervalMs, embedded.IntervalMs);
        Equal(config.TimeoutMs, embedded.TimeoutMs);
        True(embedded.ProfileId != config.ProfileId, "Each portable build needs a fresh local settings identity.");
        True(executable.SequenceEqual(File.ReadAllBytes(source)), "Export must leave the source executable unchanged.");
        True(executable.SequenceEqual(File.ReadAllBytes(first).Take(executable.Length)), "Export must preserve the original executable bytes.");

        config.Hosts[0].Name = "Second server";
        PortableConfig.WriteCopy(first, second, config);
        Equal("Second server", PortableConfig.Read(second)!.Hosts[0].Name);
        Equal(new FileInfo(first).Length, new FileInfo(second).Length);
        True(executable.SequenceEqual(File.ReadAllBytes(second).Take(executable.Length)), "Reembedding must strip the previous trailer only.");
        True(PortableConfig.Read(first)!.ProfileId != PortableConfig.Read(second)!.ProfileId, "Reembedding must create another independent profile.");

        // A self-copy must fail before changing any bytes.
        byte[] before = File.ReadAllBytes(first);
        Throws<IOException>(() => PortableConfig.WriteCopy(first, first, config));
        True(before.SequenceEqual(File.ReadAllBytes(first)), "A refused self-copy must leave its source intact.");
    });
}

static void PortableCorruption()
{
    WithTemporaryDirectory(directory =>
    {
        string source = Path.Combine(directory, "source.exe");
        string packed = Path.Combine(directory, "packed.exe");
        string corrupt = Path.Combine(directory, "corrupt.exe");
        string destination = Path.Combine(directory, "destination.exe");
        File.WriteAllBytes(source, new byte[2048]);
        var config = Config(Host());
        PortableConfig.WriteCopy(source, packed, config);
        byte[] valid = File.ReadAllBytes(packed);
        byte[] magic = Encoding.ASCII.GetBytes("PINGY_PROFILE_V1_7A6FD041BA9243C8");
        int lengthOffset = valid.Length - magic.Length - sizeof(long);
        foreach (long length in new long[] { -1, 0, ConfigCodec.MaxConfigBytes + 1L, valid.Length, long.MaxValue })
        {
            byte[] invalid = (byte[])valid.Clone();
            BinaryPrimitives.WriteInt64LittleEndian(invalid.AsSpan(lengthOffset, sizeof(long)), length);
            File.WriteAllBytes(corrupt, invalid);
            Throws<FormatException>(() => PortableConfig.Read(corrupt));
        }

        File.WriteAllBytes(corrupt, magic);
        Throws<FormatException>(() => PortableConfig.Read(corrupt));

        byte[] badUtf8 = (byte[])valid.Clone();
        badUtf8[2048] = 0xff;
        File.WriteAllBytes(corrupt, badUtf8);
        Throws<FormatException>(() => PortableConfig.Read(corrupt));
        byte[] existingDestination = "keep this existing destination"u8.ToArray();
        File.WriteAllBytes(destination, existingDestination);
        Throws<FormatException>(() => PortableConfig.WriteCopy(corrupt, destination, config));
        True(existingDestination.SequenceEqual(File.ReadAllBytes(destination)), "A rejected export must preserve an existing destination.");
    });
}

static void Statistics()
{
    var stats = new PingStatistics();
    Equal<long>(0, stats.Sent);
    Equal<double?>(null, stats.AverageMs);
    Equal<long?>(null, stats.MinimumMs);
    Equal<long?>(null, stats.MaximumMs);
    Equal(0.0, stats.LossPercent);
    stats.Add(new PingSample(Guid.NewGuid(), null, "TimedOut"));
    Equal<double?>(null, stats.AverageMs);
    Equal(100.0, stats.LossPercent);
    stats.Add(new PingSample(Guid.NewGuid(), 10, "Success"));
    stats.Add(new PingSample(Guid.NewGuid(), 30, "Success"));
    stats.Add(new PingSample(Guid.NewGuid(), 0, "Success"));
    Equal<long>(4, stats.Sent);
    Equal<long>(3, stats.Received);
    Equal<long>(1, stats.Lost);
    Equal<long?>(0, stats.MinimumMs);
    Equal<long?>(30, stats.MaximumMs);
    Equal(25.0, stats.LossPercent);
    True(Math.Abs(stats.AverageMs!.Value - 40.0 / 3) < 0.000001, "Timeouts must not lower average latency; zero-ms successes must be included.");
}

static void Adapters()
{
    foreach (AdapterInfo adapter in NetworkInfo.GetAdapters())
    {
        True(!string.IsNullOrWhiteSpace(adapter.Name), "Adapter names must be present.");
        True(adapter.Addresses.Count > 0, "Listed adapters must have addresses.");
        foreach (string text in adapter.Addresses.Concat(adapter.Gateways))
        {
            True(IPAddress.TryParse(text, out var address), "Adapter values must be IP literals.");
            True(!IPAddress.IsLoopback(address!), "Loopback addresses must not be listed as network adapters.");
        }
    }
}

static async Task PingLoopback()
{
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    var host = Host("Loopback", "127.0.0.1");
    var batches = new List<PingBatch>();
    var starts = new List<long>();
    Task running = new PingMonitor().RunAsync([host], 150, 1000, batch =>
    {
        batches.Add(batch);
        starts.Add(Stopwatch.GetTimestamp());
        if (batches.Count == 3)
            cancellation.Cancel();
    }, cancellation.Token);
    await ExpectCancellation(running);
    Equal(3, batches.Count);
    foreach (var batch in batches)
    {
        Equal(1, batch.Samples.Count);
        Equal(host.Id, batch.Samples[0].HostId);
        Equal("Success", batch.Samples[0].Status);
        True(batch.Samples[0].RoundtripMs >= 0, "Loopback should return a successful ping.");
    }
    True(Stopwatch.GetElapsedTime(starts[0], starts[2]).TotalMilliseconds >= 250, "Rounds must respect the requested interval.");
    await Task.Delay(200);
    Equal(3, batches.Count);
}

static async Task EmptyAndCancelled()
{
    int callbacks = 0;
    await new PingMonitor().RunAsync([Host(enabled: false)], 100, 1000, _ => callbacks++, CancellationToken.None);
    Equal(0, callbacks);
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    await ExpectCancellation(new PingMonitor().RunAsync([Host()], 100, 1000, _ => callbacks++, cancellation.Token));
    Equal(0, callbacks);
}

static async Task SnapshotAndOrdering()
{
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    var first = Host("First", "127.0.0.1");
    var disabled = Host("Disabled", "127.0.0.1", false);
    var last = Host("Last", "not-an-ip");
    var hosts = new List<HostEntry> { first, disabled, last };
    int callbacks = 0;
    Task running = new PingMonitor().RunAsync(hosts, 100, 1000, batch =>
    {
        callbacks++;
        Equal(2, batch.Samples.Count);
        Equal(first.Id, batch.Samples[0].HostId);
        Equal(last.Id, batch.Samples[1].HostId);
        Equal("Success", batch.Samples[0].Status);
        Equal("InvalidAddress", batch.Samples[1].Status);
        if (callbacks == 1)
        {
            first.Address = "not-an-ip";
            first.Enabled = false;
            disabled.Enabled = true;
            hosts.Clear();
        }
        if (callbacks == 2)
            cancellation.Cancel();
    }, cancellation.Token);
    await ExpectCancellation(running);
    Equal(2, callbacks);
}

static async Task CancelPendingPing()
{
    using var cancellation = new CancellationTokenSource();
    int callbacks = 0;
    Task running = new PingMonitor().RunAsync([Host("Documentation address", "192.0.2.254")], 60000, 10000,
        _ => Interlocked.Increment(ref callbacks), cancellation.Token);
    await Task.Delay(100);
    cancellation.Cancel();
    int countWhenCancelled = Volatile.Read(ref callbacks);
    var stopwatch = Stopwatch.StartNew();
    await ExpectCancellation(running.WaitAsync(TimeSpan.FromSeconds(2)));
    True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), "Cancellation should not wait for the 10-second ping timeout.");
    await Task.Delay(100);
    Equal(countWhenCancelled, Volatile.Read(ref callbacks));
}

static async Task ExpectCancellation(Task task)
{
    try { await task; }
    catch (OperationCanceledException) { return; }
    throw new Exception("Expected OperationCanceledException.");
}

static void WithTemporaryDirectory(Action<string> action)
{
    string directory = Path.Combine(Path.GetTempPath(), "pingy-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try { action(directory); }
    finally { Directory.Delete(directory, recursive: true); }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected ?? (object)"<null>"}, got {actual ?? (object)"<null>"}.");
}

static void True(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}
