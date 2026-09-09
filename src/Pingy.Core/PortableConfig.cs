using System.Buffers.Binary;
using System.Text;

namespace Pingy.Core;

/// <summary>Stores a launch profile in a bounded overlay at the end of a portable executable.</summary>
public static class PortableConfig
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PINGY_PROFILE_V1_7A6FD041BA9243C8");
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const int LengthBytes = sizeof(long);

    public static AppConfig? Read(string executablePath)
    {
        using var stream = new FileStream(executablePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return ReadOverlay(stream).Config;
    }

    public static void WriteCopy(string sourceExe, string destinationExe, AppConfig config)
    {
        var sourcePath = Path.GetFullPath(sourceExe);
        var destinationPath = Path.GetFullPath(destinationExe);
        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Выберите другое имя файла: работающую программу нельзя заменить её копией.");

        // Serialize a new profile without modifying the user's current session.
        var profile = ConfigCodec.Parse(ConfigCodec.Serialize(config));
        profile.ProfileId = Guid.NewGuid();
        var json = StrictUtf8.GetBytes(ConfigCodec.Serialize(profile));
        var temporaryPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var destinationDirectory = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(destinationDirectory);
        try
        {
            // Keep the source open without delete sharing until the atomic rename completes.
            // On Windows this also protects it if the destination uses a junction/symlink alias.
            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            {
                var overlay = ReadOverlay(source);
                if (overlay.BaseLength == 0)
                    throw new FormatException("Исходный файл программы пуст.");
                source.Position = 0;
                using var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                CopyExactly(source, output, overlay.BaseLength);
                output.Write(json);
                Span<byte> length = stackalloc byte[LengthBytes];
                BinaryPrimitives.WriteInt64LittleEndian(length, json.LongLength);
                output.Write(length);
                output.Write(Magic);
                output.Flush(flushToDisk: true);
            }

            // The original destination stays intact if copying or validation fails.
            if (File.Exists(destinationPath))
                File.Replace(temporaryPath, destinationPath, null, ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, destinationPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static (AppConfig? Config, long BaseLength) ReadOverlay(FileStream stream)
    {
        var trailerLength = Magic.Length + LengthBytes;
        if (stream.Length < Magic.Length) return (null, stream.Length);
        stream.Position = stream.Length - Magic.Length;
        Span<byte> actualMagic = stackalloc byte[Magic.Length];
        stream.ReadExactly(actualMagic);
        if (!actualMagic.SequenceEqual(Magic)) return (null, stream.Length);
        if (stream.Length < trailerLength)
            throw new FormatException("Встроенная конфигурация повреждена: заголовок неполный.");

        stream.Position = stream.Length - trailerLength;
        Span<byte> lengthBytes = stackalloc byte[LengthBytes];
        stream.ReadExactly(lengthBytes);
        var jsonLength = BinaryPrimitives.ReadInt64LittleEndian(lengthBytes);
        if (jsonLength <= 0 || jsonLength > ConfigCodec.MaxConfigBytes || jsonLength > stream.Length - trailerLength)
            throw new FormatException("Встроенная конфигурация повреждена: неверный размер.");

        var baseLength = stream.Length - trailerLength - jsonLength;
        if (baseLength <= 0)
            throw new FormatException("Встроенная конфигурация повреждена: исходная программа отсутствует.");
        stream.Position = baseLength;
        var jsonBytes = new byte[(int)jsonLength];
        stream.ReadExactly(jsonBytes);
        try
        {
            return (ConfigCodec.Parse(StrictUtf8.GetString(jsonBytes)), baseLength);
        }
        catch (DecoderFallbackException ex)
        {
            throw new FormatException("Встроенная конфигурация повреждена: неверный UTF-8.", ex);
        }
    }

    private static void CopyExactly(Stream source, Stream destination, long length)
    {
        var buffer = new byte[128 * 1024];
        var remaining = length;
        while (remaining > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0) throw new EndOfStreamException("Исходный файл программы изменился во время копирования.");
            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }
}
