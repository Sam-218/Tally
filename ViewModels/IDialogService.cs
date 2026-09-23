using Tally.Models;
using Tally.Services;

namespace Tally.ViewModels;

public enum DialogChoice { Yes, No, Cancel }

/// <summary>
/// Everything the ViewModel needs from the UI (dialogs, file picking, printing ...).
/// This keeps the logic testable and independent of WPF.
/// </summary>
public interface IDialogService
{
    bool Confirm(string title, string message, string okText = "OK", string cancelText = "Abbrechen", bool danger = false);
    DialogChoice AskYesNoCancel(string title, string message, string yesText, string noText, string cancelText = "Abbrechen");
    void Info(string title, string message);
    void Error(string title, string message);

    string? PickFolder(string title, string? initialFolder);
    string? PickOpenFile(string title, string filter);
    string? PickSaveFile(string title, string suggestedFileName, string filter);

    /// <summary>Shows the backup list and returns the chosen backup (or null).</summary>
    BackupInfo? PickBackup(IReadOnlyList<BackupInfo> backups, string backupFolder);

    void OpenArticleEditor();
    void OpenPath(string path);
    void PrintParty(Party party);
    void PrintGuests(Party party);
    void ApplyTheme(string theme);
}
