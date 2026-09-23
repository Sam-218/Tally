using System.Text.Json;
using Tally.Models;
using Timer = System.Threading.Timer;

namespace Tally.Services;

public enum SaveState { Saved, Pending, Saving, Error }

public sealed record SaveStatus(SaveState State, DateTime? LastSaved, string? Error);

public sealed class LoadResult
{
    public required AppData Data { get; init; }

    /// <summary>Message for the user (e.g. "file was corrupted, backup loaded"), otherwise null.</summary>
    public string? Notice { get; init; }

    /// <summary>True if the data should be saved (again) right after loading.</summary>
    public bool NeedsSave { get; init; }
}

/// <summary>
/// Automatic saving to a folder on the PC.
///  - every change is saved automatically after a short pause (debounced)
///  - writes are atomic (never a half-written file)
///  - rolling backups (on startup, every few minutes on change, on exit)
///  - a corrupted file is set aside and the newest valid backup is loaded
/// This class knows nothing about the UI; events are marshaled to the UI thread via <c>post</c>.
/// </summary>
public sealed class DataStore : IDisposable
{
    public const string DataFileName = "tally.json";
    public const string BackupFolderName = "Backups";

    private readonly Action<Action> _post;
    private readonly Timer _saveTimer;
    private readonly Timer _backupTimer;
    private readonly object _ioLock = new();

    private Func<string>? _snapshot;
    private long _version;
    private long _lastWritten;
    private volatile bool _pending;
    private volatile bool _disposed;
    private DateTime? _lastSaved;
    private SaveState _state = SaveState.Saved;

    public DataStore(string dataFolder, Action<Action>? post = null)
    {
        DataFolder = dataFolder;
        Backups = new BackupManager(Path.Combine(dataFolder, BackupFolderName));
        _post = post ?? (a => a());
        _saveTimer = new Timer(_ => _post(() => { _ = SaveNowAsync(); }), null, Timeout.Infinite, Timeout.Infinite);
        _backupTimer = new Timer(_ => _post(TimedBackup), null, Timeout.Infinite, Timeout.Infinite);
    }

    public string DataFolder { get; }
    public string DataFilePath => Path.Combine(DataFolder, DataFileName);
    public BackupManager Backups { get; }

    public int DebounceMs { get; init; } = 600;
    public int RetryMs { get; init; } = 5000;
    public TimeSpan BackupInterval { get; init; } = TimeSpan.FromMinutes(10);

    public SaveStatus Status => new(_state, _lastSaved, _lastError);
    private string? _lastError;

    /// <summary>Raised on every change of the save status (on the UI thread).</summary>
    public event Action<SaveStatus>? StatusChanged;

    // ------------------------------------------------------------------ Loading

    /// <summary>
    /// Loads the data. Throws an IOException if the file exists but can't be read
    /// (in that case we must NOT continue with empty data, or it would get overwritten).
    /// </summary>
    public LoadResult Load()
    {
        Directory.CreateDirectory(DataFolder);

        if (File.Exists(DataFilePath))
        {
            try
            {
                return new LoadResult { Data = AppData.FromJson(FileUtil.ReadUtf8Strict(DataFilePath)) };
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or FormatException)
            {
                Log.Write("Data file corrupted", ex);
                var aside = MoveCorruptFileAside();
                var recovered = Backups.FindNewestValid();
                if (recovered != null)
                {
                    return new LoadResult
                    {
                        Data = recovered.Value.Data,
                        NeedsSave = true,
                        Notice = string.Format(LocalizationManager.T("S_DataFileCorruptedBackupLoaded"),
                            recovered.Value.Info.TimeText.Replace("  ", " "), aside)
                    };
                }

                return new LoadResult
                {
                    Data = AppData.CreateDefault(),
                    NeedsSave = true,
                    Notice = string.Format(LocalizationManager.T("S_DataFileCorruptedNoBackup"), aside)
                };
            }
        }

        // No main file: either the first launch, or the file was deleted.
        var backup = Backups.FindNewestValid();
        if (backup != null)
        {
            return new LoadResult
            {
                Data = backup.Value.Data,
                NeedsSave = true,
                Notice = string.Format(LocalizationManager.T("S_NoDataFileBackupLoaded"), backup.Value.Info.TimeText.Replace("  ", " "))
            };
        }

        return new LoadResult { Data = AppData.CreateDefault(), NeedsSave = true };
    }

    private string MoveCorruptFileAside()
    {
        var name = $"tally.beschaedigt-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        var target = Path.Combine(DataFolder, name);
        try { File.Move(DataFilePath, target); }
        catch (Exception ex)
        {
            Log.Write("Could not move the corrupted file aside", ex);
            try { File.Copy(DataFilePath, target, overwrite: true); } catch { /* doesn't matter */ }
        }
        return name;
    }

    // ---------------------------------------------------------------- Starting

    /// <summary>Connects the store to the data. From now on the auto-save and backup timers run.</summary>
    public void Start(Func<string> snapshot)
    {
        _snapshot = snapshot;
        Backups.InitLastHash();
        _backupTimer.Change(BackupInterval, BackupInterval);
    }

    // ---------------------------------------------------------------- Saving

    /// <summary>Reports "something changed" – it gets saved after a short pause.</summary>
    public void RequestSave()
    {
        if (_disposed || _snapshot == null) return;
        _pending = true;
        _saveTimer.Change(DebounceMs, Timeout.Infinite);
        Publish(SaveState.Pending);
    }

    /// <summary>Saves immediately (the write runs in the background). Call on the UI thread.</summary>
    public Task SaveNowAsync()
    {
        if (_disposed || _snapshot == null) return Task.CompletedTask;
        _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);

        string json;
        try { json = _snapshot(); }
        catch (Exception ex) { Fail(ex); return Task.CompletedTask; }

        var version = Interlocked.Increment(ref _version);
        _pending = false;
        Publish(SaveState.Saving);

        return Task.Run(() =>
        {
            try
            {
                WriteMain(json, version);
                _lastSaved = DateTime.Now;
                _lastError = null;
                Publish(_pending ? SaveState.Pending : SaveState.Saved);
            }
            catch (Exception ex) { Fail(ex); }
        });
    }

    /// <summary>
    /// Saves immediately and waits until the file has been written (for program exit).
    /// Returns false and supplies the error message if saving failed.
    /// </summary>
    public bool SaveNowBlocking(out string? error)
    {
        error = null;
        if (_disposed || _snapshot == null) return true;
        _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
        try
        {
            var json = _snapshot();
            var version = Interlocked.Increment(ref _version);
            _pending = false;
            WriteMain(json, version);
            _lastSaved = DateTime.Now;
            _lastError = null;
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("Saving on exit failed", ex);
            error = ex.Message;
            return false;
        }
    }

    private void WriteMain(string json, long version)
    {
        lock (_ioLock)
        {
            if (version < _lastWritten) return; // a newer version was already written
            FileUtil.WriteAtomic(DataFilePath, json);
            _lastWritten = version;
        }
    }

    private void Fail(Exception ex)
    {
        Log.Write("Saving failed", ex);
        _pending = true;
        _lastError = ex.Message;
        if (!_disposed) _saveTimer.Change(RetryMs, Timeout.Infinite); // retry automatically
        Publish(SaveState.Error);
    }

    private void Publish(SaveState state)
    {
        _state = state;
        var status = new SaveStatus(state, _lastSaved, _lastError);
        _post(() => StatusChanged?.Invoke(status));
    }

    // ------------------------------------------------------------------ Backups

    private void TimedBackup()
    {
        if (_disposed || _snapshot == null) return;
        string json;
        try { json = _snapshot(); } catch { return; }
        Task.Run(() =>
        {
            try { Backups.Create(json, "auto"); }
            catch (Exception ex) { Log.Write("Automatic backup failed", ex); }
        });
    }

    /// <summary>Creates a backup of the current state right away (call on the UI thread). Returns the path.</summary>
    public string? CreateBackup(string label, bool force = true)
    {
        if (_snapshot == null) return null;
        return Backups.Create(_snapshot(), label, force);
    }

    // ---------------------------------------------------------------- Cleanup

    /// <summary>Checks whether the folder is writable. Returns null if so, otherwise the error message.</summary>
    public static string? TestWritable(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".schreibtest-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "test");
            File.Delete(probe);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _saveTimer.Dispose();
        _backupTimer.Dispose();
    }
}
