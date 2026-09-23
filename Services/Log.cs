using System.Text;

namespace Tally.Services;

/// <summary>Minimal log file (log.txt) – helps with troubleshooting without the user having to see anything.</summary>
public static class Log
{
    private static readonly object Lock = new();

    public static string FilePath { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tally", "log.txt");

    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            lock (Lock)
            {
                var dir = System.IO.Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                // keep the file small: past 512 KB the old file gets replaced
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 512 * 1024)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);

                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("  ").AppendLine(message);
                if (ex != null) sb.AppendLine(ex.ToString());
                File.AppendAllText(FilePath, sb.ToString(), FileUtil.Utf8NoBom);
            }
        }
        catch
        {
            // Logging itself must never become a problem.
        }
    }
}
