using System.Windows.Input;
using Tally.Models;
using Tally.Services;

namespace Tally.ViewModels;

/// <summary>Save status, backups, import/export, and changing the storage folder.</summary>
public sealed partial class MainViewModel
{
    private const string JsonFilter = "JSON-Datei (*.json)|*.json|Alle Dateien (*.*)|*.*";

    private string _storageStatusText = "";
    private string _storageStatusKind = "ok";

    public string StorageStatusText { get => _storageStatusText; private set => Set(ref _storageStatusText, value); }

    /// <summary>"ok" (green), "busy" (yellow) or "error" (red) – controls the color of the status dot.</summary>
    public string StorageStatusKind
    {
        get => _storageStatusKind;
        private set
        {
            if (Set(ref _storageStatusKind, value)) OnPropertyChanged(nameof(ShowRetrySave));
        }
    }

    public bool ShowRetrySave => StorageStatusKind == "error";
    public string DataFolderPath => _store.DataFolder;
    public string DataFolderDisplay => Shorten(_store.DataFolder, 40);

    private void OnStoreStatusChanged(SaveStatus status) => UpdateStoragePanel(status);

    private void UpdateStoragePanel(SaveStatus s)
    {
        switch (s.State)
        {
            case SaveState.Saved:
                StorageStatusText = s.LastSaved is { } t
                    ? string.Format(LocalizationManager.T("S_AutoSavedAt"), t.ToString("HH:mm:ss"))
                    : LocalizationManager.T("S_AutoSaved");
                StorageStatusKind = "ok";
                break;
            case SaveState.Pending:
                StorageStatusText = LocalizationManager.T("S_SavingChanges");
                StorageStatusKind = "busy";
                break;
            case SaveState.Saving:
                StorageStatusText = LocalizationManager.T("S_Saving");
                StorageStatusKind = "busy";
                break;
            default:
                StorageStatusText = string.Format(LocalizationManager.T("S_SaveFailedRetrying"), s.Error);
                StorageStatusKind = "error";
                break;
        }

        OnPropertyChanged(nameof(DataFolderPath));
        OnPropertyChanged(nameof(DataFolderDisplay));
    }

    private static string Shorten(string path, int max)
    {
        if (path.Length <= max) return path;
        var head = Math.Max(8, max / 3);
        var tail = max - head - 1;
        return path[..head] + "…" + path[^tail..];
    }

    // ------------------------------------------------------------------ Commands

    private ICommand? _openFolder;
    public ICommand OpenDataFolderCommand => _openFolder ??= new RelayCommand(_ => _ui.OpenPath(_store.DataFolder));

    private ICommand? _retrySave;
    public ICommand RetrySaveCommand => _retrySave ??= new RelayCommand(_ => { _ = _store.SaveNowAsync(); });

    private ICommand? _backupNow;
    public ICommand BackupNowCommand => _backupNow ??= new RelayCommand(_ => BackupNow());

    private ICommand? _showBackups;
    public ICommand ShowBackupsCommand => _showBackups ??= new RelayCommand(_ => ShowBackups());

    private ICommand? _export;
    public ICommand ExportCommand => _export ??= new RelayCommand(_ => Export());

    private ICommand? _import;
    public ICommand ImportCommand => _import ??= new RelayCommand(_ => Import());

    private ICommand? _changeFolder;
    public ICommand ChangeDataFolderCommand => _changeFolder ??= new RelayCommand(_ => ChangeDataFolder());

    // ------------------------------------------------------------------ Backups

    private void BackupNow()
    {
        try
        {
            var path = _store.CreateBackup("manuell", force: true);
            _ui.Info(LocalizationManager.T("S_BackupCreatedTitle"), string.Format(LocalizationManager.T("S_BackupCreatedMsg"), path));
        }
        catch (Exception ex)
        {
            Log.Write("Manual backup failed", ex);
            _ui.Error(LocalizationManager.T("S_BackupFailedTitle"), ex.Message);
        }
    }

    private void ShowBackups()
    {
        var pick = _ui.PickBackup(_store.Backups.List(), _store.Backups.Folder);
        if (pick == null) return;

        AppData restored;
        try { restored = BackupManager.Read(pick); }
        catch (Exception ex)
        {
            Log.Write("Backup not readable", ex);
            _ui.Error(LocalizationManager.T("S_BackupNotReadableTitle"), string.Format(LocalizationManager.T("S_BackupNotReadableMsg"), ex.Message));
            return;
        }

        if (!_ui.Confirm(LocalizationManager.T("S_RestoreBackupTitle"),
                string.Format(LocalizationManager.T("S_RestoreBackupMsg"), pick.TimeText),
                LocalizationManager.T("S_Restore"), LocalizationManager.T("S_Cancel"))) return;

        TryBackup("vor-wiederherstellung", force: true);
        ReplaceData(restored);
    }

    // ------------------------------------------------------------------ Export / Import

    private void Export()
    {
        var path = _ui.PickSaveFile(LocalizationManager.T("S_ExportDataTitle"), $"tally-{DateTime.Today:yyyy-MM-dd}.json", JsonFilter);
        if (path == null) return;

        try
        {
            FileUtil.WriteAtomic(path, _data.ToJson());
            _ui.Info(LocalizationManager.T("S_ExportedTitle"), string.Format(LocalizationManager.T("S_ExportedMsg"), path));
        }
        catch (Exception ex)
        {
            Log.Write("Export failed", ex);
            _ui.Error(LocalizationManager.T("S_ExportFailedTitle"), ex.Message);
        }
    }

    /// <summary>Loads a JSON file – including export files from the old HTML version.</summary>
    private void Import()
    {
        var path = _ui.PickOpenFile(LocalizationManager.T("S_ImportDataTitle"), JsonFilter);
        if (path == null) return;

        AppData imported;
        try { imported = AppData.FromJson(FileUtil.ReadUtf8Strict(path)); }
        catch (Exception ex)
        {
            Log.Write("Import failed", ex);
            _ui.Error(LocalizationManager.T("S_ImportFailedTitle"), string.Format(LocalizationManager.T("S_ImportFailedMsg"), ex.Message));
            return;
        }

        if (!_ui.Confirm(LocalizationManager.T("S_ImportDataTitle"),
                string.Format(LocalizationManager.T("S_ImportConfirmMsg"), imported.Parties.Count),
                LocalizationManager.T("S_Replace"), LocalizationManager.T("S_Cancel"), danger: true)) return;

        TryBackup("vor-import", force: true);
        ReplaceData(imported);
    }

    // ------------------------------------------------------------------ Changing the storage folder

    private void ChangeDataFolder()
    {
        var picked = _ui.PickFolder(LocalizationManager.T("S_ChooseNewFolderTitle"), _store.DataFolder);
        if (string.IsNullOrWhiteSpace(picked)) return;

        var folder = Path.GetFullPath(picked);
        var current = Path.GetFullPath(_store.DataFolder);
        if (string.Equals(folder.TrimEnd('\\', '/'), current.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return;

        var problem = DataStore.TestWritable(folder);
        if (problem != null)
        {
            _ui.Error(LocalizationManager.T("S_FolderNotUsableTitle"), string.Format(LocalizationManager.T("S_FolderNotUsableMsg"), problem));
            return;
        }

        var newStore = new DataStore(folder, _post);
        var existingFile = newStore.DataFilePath;
        var loadExisting = false;

        if (File.Exists(existingFile))
        {
            var choice = _ui.AskYesNoCancel(LocalizationManager.T("S_DataFileFoundTitle"),
                string.Format(LocalizationManager.T("S_DataFileFoundMsg"), folder),
                LocalizationManager.T("S_LoadThisFile"), LocalizationManager.T("S_CopyCurrentData"));
            if (choice == DialogChoice.Cancel) { newStore.Dispose(); return; }
            loadExisting = choice == DialogChoice.Yes;
        }

        // still save the current state in the old folder
        _store.SaveNowBlocking(out _);

        AppData? loaded = null;
        string? notice = null;

        if (loadExisting)
        {
            try
            {
                var result = newStore.Load();
                loaded = result.Data;
                notice = result.Notice;
            }
            catch (Exception ex)
            {
                newStore.Dispose();
                Log.Write("File in the new folder not readable", ex);
                _ui.Error(LocalizationManager.T("S_FileNotReadableTitle"), string.Format(LocalizationManager.T("S_FileNotReadableMsg"), ex.Message));
                return;
            }
        }
        else if (File.Exists(existingFile))
        {
            try { newStore.Backups.Create(FileUtil.ReadUtf8Strict(existingFile), "vor-ordnerwechsel", force: true); }
            catch (Exception ex)
            {
                Log.Write("Backing up the existing file failed", ex);
                try { File.Copy(existingFile, existingFile + ".alt", overwrite: true); } catch { /* doesn't matter */ }
            }
        }

        // switch over
        _store.StatusChanged -= OnStoreStatusChanged;
        _store.Dispose();
        _store = newStore;
        _store.StatusChanged += OnStoreStatusChanged;
        _store.Start(Snapshot);

        _settings.DataFolder = folder;
        _settings.Save();

        if (loaded != null) ReplaceData(loaded);
        _ = _store.SaveNowAsync(); // write to the new folder right away, not just after the debounce pause

        UpdateStoragePanel(_store.Status);
        if (notice != null) _ui.Info(LocalizationManager.T("S_NoticeTitle"), notice);
    }
}
