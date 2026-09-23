using System.Security.Cryptography;
using System.Text;

namespace Tally.Services;

public static class FileUtil
{
    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes a file "atomically": first to a .tmp file (flushed all the way to disk),
    /// then the old file gets replaced. If power or the program cuts out mid-write,
    /// either the complete old file or the complete new file is always left behind –
    /// never a half-written one.
    /// </summary>
    public static void WriteAtomic(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var bytes = Utf8NoBom.GetBytes(content);
        var tmp = path + ".tmp";

        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(true); // actually flush to disk, not just to the cache
        }

        try
        {
            if (File.Exists(path)) File.Replace(tmp, path, null, ignoreMetadataErrors: true);
            else File.Move(tmp, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // fallback (e.g. some network drives): overwrite directly
            File.Copy(tmp, path, overwrite: true);
            try { File.Delete(tmp); } catch { /* doesn't matter */ }
        }
    }

    /// <summary>Reads UTF-8 strictly: invalid bytes throw an exception (= file corrupted).</summary>
    public static string ReadUtf8Strict(string path)
        => new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(File.ReadAllBytes(path));

    public static string Sha256(string text)
        => Convert.ToHexString(SHA256.HashData(Utf8NoBom.GetBytes(text)));

    /// <summary>Removes characters that aren't allowed in file names.</summary>
    public static string SafeFileName(string name, string fallback = "Party")
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length == 0 ? fallback : cleaned;
    }
}
