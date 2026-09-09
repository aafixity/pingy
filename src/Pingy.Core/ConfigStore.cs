using System.Text;

namespace Pingy.Core;

public sealed class ConfigStore
{
    private readonly string _path;
    private readonly object _gate = new();

    public ConfigStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public AppConfig? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return null;
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > ConfigCodec.MaxConfigBytes)
                throw new FormatException("Конфигурация не должна превышать 4 МБ.");
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
            try { return ConfigCodec.Parse(reader.ReadToEnd()); }
            catch (DecoderFallbackException ex)
            {
                throw new FormatException("Файл конфигурации содержит некорректный текст UTF-8.", ex);
            }
        }
    }

    public void Save(AppConfig config)
    {
        var content = Encoding.UTF8.GetBytes(ConfigCodec.Serialize(config));
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(content);
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(_path))
                    File.Replace(temporaryPath, _path, _path + ".bak", ignoreMetadataErrors: true);
                else
                    File.Move(temporaryPath, _path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }
}
