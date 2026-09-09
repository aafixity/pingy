using System.Net;
using System.Net.Sockets;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Pingy.Core;

public static class ConfigCodec
{
    public const int MaxHosts = 256;
    public const int MaxConfigBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        MaxDepth = 16
    };

    public static AppConfig Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new FormatException("Configuration is empty.");
        if (Encoding.UTF8.GetByteCount(json) > MaxConfigBytes)
            throw new FormatException("Configuration must not exceed 4 MB.");
        try
        {
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions)
                         ?? throw new FormatException("Configuration must contain a JSON object.");
            Validate(config);
            return config;
        }
        catch (JsonException ex)
        {
            throw new FormatException("Could not read the configuration JSON. Check the file format.", ex);
        }
    }

    public static string Serialize(AppConfig config)
    {
        var normalized = CopyAndValidate(config);
        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaxConfigBytes)
            throw new FormatException("Configuration must not exceed 4 MB.");
        return json;
    }

    /// <summary>Checks a configuration and normalizes its user-entered text and IP addresses in place.</summary>
    public static void Validate(AppConfig config)
    {
        if (config is null) throw new FormatException("Configuration is missing.");
        if (config.Version != 1)
            throw new FormatException($"Configuration version {config.Version} is unsupported. Expected version 1.");
        if (config.ProfileId == Guid.Empty)
            config.ProfileId = Guid.NewGuid();
        if (config.IntervalMs is < 250 or > 60000)
            throw new FormatException("Interval must be between 250 and 60,000 ms.");
        if (config.TimeoutMs is < 100 or > 10000)
            throw new FormatException("Timeout must be between 100 and 10,000 ms.");
        if (config.Hosts is null)
            throw new FormatException("Host list is missing.");
        if (config.Hosts.Count > MaxHosts)
            throw new FormatException($"A maximum of {MaxHosts} hosts is supported.");

        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<Guid>();
        for (var i = 0; i < config.Hosts.Count; i++)
        {
            var host = config.Hosts[i];
            if (host is null)
                throw new FormatException($"Host #{i + 1} is missing.");
            host.Name = (host.Name ?? "").Trim();
            host.Group = (host.Group ?? "").Trim();
            host.Description = (host.Description ?? "").Trim();
            if (host.Name.Length == 0)
                throw new FormatException($"Enter a name for host #{i + 1}.");
            if (host.Name.Length > 120)
                throw new FormatException($"The name of host #{i + 1} must not exceed 120 characters.");
            if (host.Group.Length == 0) host.Group = "Ungrouped";
            if (host.Group.Length > 80)
                throw new FormatException($"The group for '{host.Name}' must not exceed 80 characters.");
            if (host.Description.Length > 1000)
                throw new FormatException($"The notes for '{host.Name}' must not exceed 1,000 characters.");

            host.Address = NormalizeAddress(host.Address, host.Name);
            if (!addresses.Add(host.Address))
                throw new FormatException($"IP address {host.Address} appears more than once.");
            if (host.Id == Guid.Empty) host.Id = Guid.NewGuid();
            if (!ids.Add(host.Id))
                throw new FormatException($"The ID for '{host.Name}' appears more than once.");
        }
    }

    public static AppConfig Merge(AppConfig existing, AppConfig imported)
    {
        var merged = CopyAndValidate(existing);
        var incoming = CopyAndValidate(imported);
        var addresses = merged.Hosts.Select(host => host.Address).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ids = merged.Hosts.Select(host => host.Id).ToHashSet();
        foreach (var host in incoming.Hosts)
        {
            if (!addresses.Add(host.Address)) continue;
            while (!ids.Add(host.Id)) host.Id = Guid.NewGuid();
            merged.Hosts.Add(host);
        }
        Validate(merged);
        return merged;
    }

    private static AppConfig CopyAndValidate(AppConfig config)
    {
        if (config is null) throw new FormatException("Configuration is missing.");
        // Validate the shape before cloning so malformed JSON-shaped data yields a useful error.
        if (config.Hosts is null || config.Hosts.Any(host => host is null))
            throw new FormatException("Configuration contains an invalid host list.");
        var copy = config.Clone();
        Validate(copy);
        return copy;
    }

    private static string NormalizeAddress(string? value, string name)
    {
        var address = (value ?? "").Trim();
        IPAddress parsed;
        if (!address.Contains(':'))
        {
            var parts = address.Split('.');
            if (parts.Length != 4 || parts.Any(part => part.Length is 0 or > 3 || !part.All(char.IsAsciiDigit)))
                throw new FormatException($"The IPv4 address for '{name}' must contain four numbers separated by dots.");
            var bytes = new byte[4];
            for (var i = 0; i < bytes.Length; i++)
                if (!byte.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out bytes[i]))
                    throw new FormatException($"Invalid IP address for '{name}'. IPv4 numbers must be between 0 and 255.");
            // IPAddress.TryParse accepts historical octal forms; user-entered octets are decimal here.
            parsed = new IPAddress(bytes);
        }
        else if (!IPAddress.TryParse(address, out var ipv6) || ipv6.AddressFamily != AddressFamily.InterNetworkV6)
        {
            throw new FormatException($"Invalid IP address for '{name}'.");
        }
        else parsed = ipv6;
        if (parsed.IsIPv4MappedToIPv6) parsed = parsed.MapToIPv4();
        return parsed.ToString();
    }
}
