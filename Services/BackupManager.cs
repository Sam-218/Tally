using System.Globalization;
using System.Text.RegularExpressions;
using Tally.Models;

namespace Tally.Services;

/// <summary>An existing backup file.</summary>
public sealed record BackupInfo(string Path, DateTime Timestamp, string Label, long Size)
{
    /// <summary>Label without an appended counter ("auto-2" -> "auto").</summary>
    public string BaseLabel => Regex.Replace(Label, @"-\d+$", "");

    /// <summary>Manual / safety backups aren't cleaned up as aggressively as automatic ones.</summary>
    public bool IsProtected => BaseLabel is not ("auto" or "start" or "ende");

    public string Reason => BaseLabel switch
    {
        "auto" => LocalizationManager.T("S_ReasonAuto"),
        "start" => LocalizationManager.T("S_ReasonStart"),
        "ende" => LocalizationManager.T("S_ReasonEnd"),
        "manuell" => LocalizationManager.T("S_ReasonManual"),
        "vor-import" => LocalizationManager.T("S_ReasonBeforeImport"),
        "vor-wiederherstellung" => LocalizationManager.T("S_ReasonBeforeRestore"),
        "vor-ordnerwechsel" => LocalizationManager.T("S_ReasonBeforeFolderChange"),
        "vor-loeschen" => LocalizationManager.T("S_ReasonBeforeDelete"),
        "" => LocalizationManager.T("S_ReasonBackup"),
        _ => BaseLabel,
    };

    public string SizeText => Size < 1024 ? $"{Size} B" : $"{Size / 1024.0:0.#} KB";
    public string TimeText => Timestamp.ToString("dd.MM.yyyy  HH:mm:ss", CultureInfo.InvariantCulture);
}

/// <summary>
/// Creates backups in the "Backups" subfolder and prunes old ones.
/// Retention: the newest 20 automatic backups, plus the last one per day
/// (for 30 days), plus the newest 50 manual/safety backups.
/// </summary>
public sealed class BackupManager
{
    private static readonly Regex NameRx = new(
        @"^tally_(\d{4}-\d{2}-\d{2})_(\d{2}-\d{2}-\d{2})(?:_([A-Za-z0-9-]+))?\.json$",
        RegexOptions.Compiled);

    private readonly object _lock = new();
    private string? _lastHash;

    public BackupManager(string folder) => Folder = folder;

    public string Folder { get; }
    public int KeepNewest { get; init; } = 20;
    public int KeepDailyForDays { get; init; } = 30;
    public int KeepProtected { get; init; } = 50;

    public List<BackupInfo> List()
    {
        lock (_lock) return ListUnlocked();
    }

    private List<BackupInfo> ListUnlocked()
    {
        var result = new List<BackupInfo>();
        if (!Directory.Exists(Folder)) return result;

        foreach (var file in Directory.EnumerateFiles(Folder, "tally_*.json"))
        {
            var m = NameRx.Match(System.IO.Path.GetFileName(file));
            if (!m.Success) continue;
            if (!DateTime.TryParseExact($"{m.Groups[1].Value} {m.Groups[2].Value}", "yyyy-MM-dd HH-mm-ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts)) continue;
            long size = 0;
            try { size = new FileInfo(file).Length; } catch { /* doesn't matter */ }
            result.Add(new BackupInfo(file, ts, m.Groups[3].Value, size));
        }

        return result.OrderByDescending(b => b.Timestamp).ThenByDescending(b => b.Path, StringComparer.Ordinal).ToList();
    }

    /// <summary>Remembers the content of the newest backup, so unchanged data isn't backed up twice.</summary>
    public void InitLastHash()
    {
        lock (_lock)
        {
            _lastHash = null;
            var newest = ListUnlocked().FirstOrDefault();
            if (newest == null) return;
            try { _lastHash = FileUtil.Sha256(FileUtil.ReadUtf8Strict(newest.Path)); }
            catch { /* backup unreadable -> the next backup just gets created normally */ }
        }
    }

    /// <summary>
    /// Creates a backup. Returns null if the data hasn't changed since the last backup
    /// (unless force = true).
    /// </summary>
    public string? Create(string json, string label, bool force = false)
    {
        lock (_lock)
        {
            var hash = FileUtil.Sha256(json);
            if (!force && hash == _lastHash) return null;

            var now = DateTime.Now;
            string path;
            var n = 1;
            do
            {
                var suffix = n == 1 ? label : $"{label}-{n}";
                path = System.IO.Path.Combine(Folder,
                    $"tally_{now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)}_{suffix}.json");
                n++;
            } while (File.Exists(path));

            FileUtil.WriteAtomic(path, json);
            _lastHash = hash;
            PruneUnlocked();
            return path;
        }
    }

    public void Prune()
    {
        lock (_lock) PruneUnlocked();
    }

    private void PruneUnlocked()
    {
        var all = ListUnlocked(); // newest first
        var keep = new HashSet<string>();

        foreach (var b in all.Where(b => b.IsProtected).Take(KeepProtected)) keep.Add(b.Path);

        var normal = all.Where(b => !b.IsProtected).ToList();
        foreach (var b in normal.Take(KeepNewest)) keep.Add(b.Path);

        var cutoff = DateTime.Now.Date.AddDays(-KeepDailyForDays);
        foreach (var day in normal.Where(b => b.Timestamp >= cutoff).GroupBy(b => b.Timestamp.Date))
            keep.Add(day.First().Path); // the last backup of that day

        foreach (var b in all)
        {
            if (keep.Contains(b.Path)) continue;
            try { File.Delete(b.Path); } catch { /* doesn't matter, will retry next time */ }
        }
    }

    /// <summary>Reads a backup. Throws an exception if it's unusable.</summary>
    public static AppData Read(BackupInfo backup) => AppData.FromJson(FileUtil.ReadUtf8Strict(backup.Path));

    /// <summary>The newest backup that actually loads successfully.</summary>
    public (AppData Data, BackupInfo Info)? FindNewestValid()
    {
        foreach (var b in List())
        {
            try { return (Read(b), b); }
            catch { /* try the next one */ }
        }
        return null;
    }
}
