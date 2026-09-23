namespace Tally.Services;

/// <summary>
/// One-time move from the old "PartyRechnungen" folders to the new "Tally" ones
/// (the program was renamed; the folders followed).
///
/// Everything is COPIED, never moved or deleted: the old folders stay untouched as a
/// safety net, so even a half-finished migration cannot cost a single file. Runs once
/// at startup, before the settings are read.
/// </summary>
public static class LegacyMigration
{
    private const string LegacyFolderName = "PartyRechnungen";
    private const string LegacyFilePrefix = "party-rechnungen";
    private const string NewFilePrefix = "tally";

    public static void Run()
    {
        // Settings first: the data migration needs to know whether a custom folder was picked.
        try { MigrateSettings(); }
        catch (Exception ex) { Log.Write("Old settings could not be taken over", ex); }

        try { MigrateDataFolder(); }
        catch (Exception ex) { Log.Write("Old data folder could not be taken over", ex); }
    }

    private static void MigrateSettings()
    {
        var target = AppSettings.SettingsDirectory;
        if (File.Exists(AppSettings.SettingsPath)) return;      // already migrated (or a fresh install)

        var legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LegacyFolderName, "settings.json");
        if (!File.Exists(legacy)) return;                       // nothing to take over

        Directory.CreateDirectory(target);
        File.Copy(legacy, AppSettings.SettingsPath);
    }

    private static void MigrateDataFolder()
    {
        var target = AppSettings.DefaultDataFolder();
        if (Directory.Exists(target)) return;                   // never touch an existing new folder

        // Only the default location is migrated - someone who chose their own folder keeps it.
        if (!string.IsNullOrWhiteSpace(AppSettings.Load().DataFolder)) return;

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(docs)) return;

        var legacy = Path.Combine(docs, LegacyFolderName);
        if (!File.Exists(Path.Combine(legacy, LegacyFilePrefix + ".json"))) return;

        Directory.CreateDirectory(target);
        CopyRenamed(legacy, target);

        var legacyBackups = Path.Combine(legacy, "Backups");
        if (Directory.Exists(legacyBackups))
        {
            var targetBackups = Path.Combine(target, "Backups");
            Directory.CreateDirectory(targetBackups);
            CopyRenamed(legacyBackups, targetBackups);
        }

        Log.Write($"Data taken over from \"{legacy}\" into \"{target}\" - the old folder was kept.");
    }

    /// <summary>
    /// Copies every .json across, renaming the old "party-rechnungen" prefix to "tally"
    /// so the data file, the backups and any quarantined files keep working under the new name.
    /// </summary>
    private static void CopyRenamed(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*.json"))
        {
            var name = Path.GetFileName(file);
            var renamed = name.StartsWith(LegacyFilePrefix, StringComparison.OrdinalIgnoreCase)
                ? NewFilePrefix + name[LegacyFilePrefix.Length..]
                : name;

            var destination = Path.Combine(to, renamed);
            if (!File.Exists(destination)) File.Copy(file, destination);
        }
    }
}
