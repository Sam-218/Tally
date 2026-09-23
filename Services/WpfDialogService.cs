using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using Tally.Models;
using Tally.ViewModels;
using Tally.Views;

namespace Tally.Services;

/// <summary>The real UI behind IDialogService: windows, file dialogs, printing.</summary>
public sealed class WpfDialogService : IDialogService
{
    public MainViewModel? ViewModel { get; set; }

    private static Window? Owner =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.MainWindow;

    public bool Confirm(string title, string message, string okText = "OK", string cancelText = "Abbrechen", bool danger = false)
        => DialogWindow.Show(Owner, title, message, okText, null, cancelText, danger) == DialogChoice.Yes;

    public DialogChoice AskYesNoCancel(string title, string message, string yesText, string noText, string cancelText = "Abbrechen")
        => DialogWindow.Show(Owner, title, message, yesText, noText, cancelText, false);

    public void Info(string title, string message)
        => DialogWindow.Show(Owner, title, message, "OK", null, null, false);

    public void Error(string title, string message)
        => DialogWindow.Show(Owner, title, message, "OK", null, null, false);

    public string? PickFolder(string title, string? initialFolder)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
            dialog.InitialDirectory = initialFolder;
        var owner = Owner;
        var ok = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return ok == true ? dialog.FolderName : null;
    }

    public string? PickOpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        var owner = Owner;
        var ok = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return ok == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(string title, string suggestedFileName, string filter)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = suggestedFileName, OverwritePrompt = true };
        var owner = Owner;
        var ok = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return ok == true ? dialog.FileName : null;
    }

    public BackupInfo? PickBackup(IReadOnlyList<BackupInfo> backups, string backupFolder)
    {
        var window = new BackupsWindow(backups, backupFolder, OpenPath) { Owner = Owner };
        return window.ShowDialog() == true ? window.Selected : null;
    }

    public void OpenArticleEditor()
    {
        if (ViewModel == null) return;
        new ArticleEditorWindow(ViewModel) { Owner = Owner }.ShowDialog();
    }

    public void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write($"Could not open '{path}'", ex);
            Error("Öffnen fehlgeschlagen", ex.Message);
        }
    }

    public void PrintParty(Party party)
    {
        try
        {
            PrintService.Print(party);
        }
        catch (Exception ex)
        {
            Log.Write("Printing failed", ex);
            Error("Drucken fehlgeschlagen", ex.Message);
        }
    }

    public void PrintGuests(Party party)
    {
        try
        {
            PrintService.PrintGuests(party);
        }
        catch (Exception ex)
        {
            Log.Write("Printing the guest list failed", ex);
            Error("Drucken fehlgeschlagen", ex.Message);
        }
    }

    public void ApplyTheme(string theme) => ThemeManager.Apply(theme);
}
