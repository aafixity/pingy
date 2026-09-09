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
            throw new FormatException("Конфигурация пуста.");
        if (Encoding.UTF8.GetByteCount(json) > MaxConfigBytes)
            throw new FormatException("Конфигурация не должна превышать 4 МБ.");
        try
        {
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions)
                         ?? throw new FormatException("Конфигурация должна содержать JSON-объект.");
            Validate(config);
            return config;
        }
        catch (JsonException ex)
        {
            throw new FormatException("Не удалось прочитать JSON конфигурации. Проверьте формат файла.", ex);
        }
    }

    public static string Serialize(AppConfig config)
    {
        var normalized = CopyAndValidate(config);
        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaxConfigBytes)
            throw new FormatException("Конфигурация не должна превышать 4 МБ.");
        return json;
    }

    /// <summary>Checks a configuration and normalizes its user-entered text and IP addresses in place.</summary>
    public static void Validate(AppConfig config)
    {
        if (config is null) throw new FormatException("Конфигурация отсутствует.");
        if (config.Version != 1)
            throw new FormatException($"Версия конфигурации {config.Version} не поддерживается. Нужна версия 1.");
        if (config.ProfileId == Guid.Empty)
            config.ProfileId = Guid.NewGuid();
        if (config.IntervalMs is < 250 or > 60000)
            throw new FormatException("Интервал должен быть от 250 до 60 000 мс.");
        if (config.TimeoutMs is < 100 or > 10000)
            throw new FormatException("Тайм-аут должен быть от 100 до 10 000 мс.");
        if (config.Hosts is null)
            throw new FormatException("Список хостов отсутствует.");
        if (config.Hosts.Count > MaxHosts)
            throw new FormatException($"Можно добавить не более {MaxHosts} хостов.");

        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<Guid>();
        for (var i = 0; i < config.Hosts.Count; i++)
        {
            var host = config.Hosts[i];
            if (host is null)
                throw new FormatException($"Хост №{i + 1} отсутствует.");
            host.Name = (host.Name ?? "").Trim();
            host.Group = (host.Group ?? "").Trim();
            host.Description = (host.Description ?? "").Trim();
            if (host.Name.Length == 0)
                throw new FormatException($"Укажите имя хоста №{i + 1}.");
            if (host.Name.Length > 120)
                throw new FormatException($"Имя хоста №{i + 1} не должно превышать 120 символов.");
            if (host.Group.Length == 0) host.Group = "Без группы";
            if (host.Group.Length > 80)
                throw new FormatException($"Группа хоста «{host.Name}» не должна превышать 80 символов.");
            if (host.Description.Length > 1000)
                throw new FormatException($"Описание хоста «{host.Name}» не должно превышать 1000 символов.");

            host.Address = NormalizeAddress(host.Address, host.Name);
            if (!addresses.Add(host.Address))
                throw new FormatException($"IP-адрес {host.Address} повторяется в списке хостов.");
            if (host.Id == Guid.Empty) host.Id = Guid.NewGuid();
            if (!ids.Add(host.Id))
                throw new FormatException($"Идентификатор хоста «{host.Name}» повторяется.");
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
        if (config is null) throw new FormatException("Конфигурация отсутствует.");
        // Validate the shape before cloning so malformed JSON-shaped data yields a useful error.
        if (config.Hosts is null || config.Hosts.Any(host => host is null))
            throw new FormatException("Конфигурация содержит некорректный список хостов.");
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
                throw new FormatException($"У хоста «{name}» IPv4 должен содержать четыре числа через точку.");
            var bytes = new byte[4];
            for (var i = 0; i < bytes.Length; i++)
                if (!byte.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out bytes[i]))
                    throw new FormatException($"У хоста «{name}» неверный IP-адрес. Числа IPv4 должны быть от 0 до 255.");
            // IPAddress.TryParse accepts historical octal forms; user-entered octets are decimal here.
            parsed = new IPAddress(bytes);
        }
        else if (!IPAddress.TryParse(address, out var ipv6) || ipv6.AddressFamily != AddressFamily.InterNetworkV6)
        {
            throw new FormatException($"У хоста «{name}» неверный IP-адрес.");
        }
        else parsed = ipv6;
        if (parsed.IsIPv4MappedToIPv6) parsed = parsed.MapToIPv4();
        return parsed.ToString();
    }
}
