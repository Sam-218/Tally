using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tally.Services;

namespace Tally.Views;

/// <summary>List of all backups; the user picks one to restore.</summary>
public partial class BackupsWindow : Window
{
    private readonly string _folder;
    private readonly Action<string> _openPath;

    public BackupsWindow(IReadOnlyList<BackupInfo> backups, string folder, Action<string> openPath)
    {
        InitializeComponent();
        ThemeManager.Attach(this);

        _folder = folder;
        _openPath = openPath;

        BackupList.ItemsSource = backups;
        EmptyText.Visibility = backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RestoreButton.IsEnabled = false;
        if (backups.Count > 0) BackupList.SelectedIndex = 0;
    }

    /// <summary>The chosen backup (only valid if ShowDialog() returns true).</summary>
    public BackupInfo? Selected { get; private set; }

    private void BackupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => RestoreButton.IsEnabled = BackupList.SelectedItem != null;

    private void BackupList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var container = ItemsControl.ContainerFromElement(BackupList, (DependencyObject)e.OriginalSource);
        if (container != null) Restore_Click(sender, e);
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        Selected = BackupList.SelectedItem as BackupInfo;
        if (Selected != null) DialogResult = true;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(_folder); } catch { /* doesn't matter */ }
        _openPath(_folder);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        DialogResult = false;
    }
}
