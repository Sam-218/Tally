using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Tally.Services;
using Tally.ViewModels;

namespace Tally.Views;

/// <summary>Unified message window in the app's own style (replaces the gray Windows MessageBoxes).</summary>
public partial class DialogWindow : Window
{
    public DialogChoice Choice { get; private set; } = DialogChoice.Cancel;

    private DialogWindow(string title, string message, string primaryText, string? secondaryText,
        string? cancelText, bool danger)
    {
        InitializeComponent();
        ThemeManager.Attach(this);

        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primaryText;

        if (secondaryText != null)
        {
            SecondaryButton.Content = secondaryText;
            SecondaryButton.Visibility = Visibility.Visible;
        }

        if (cancelText != null)
        {
            CancelButton.Content = cancelText;
            CancelButton.Visibility = Visibility.Visible;
        }

        if (danger)
        {
            PrimaryButton.SetResourceReference(BackgroundProperty, "DangerBrush");
            PrimaryButton.SetResourceReference(BorderBrushProperty, "DangerBrush");
            PrimaryButton.Foreground = ThemeManager.Current == "dark"
                ? new SolidColorBrush(Color.FromRgb(0x2B, 0x0A, 0x0A))
                : Brushes.White;

            // For dangerous actions, Enter shouldn't accidentally delete: Cancel is the default.
            PrimaryButton.IsDefault = false;
            if (cancelText != null) CancelButton.IsDefault = true;
        }
    }

    /// <summary>
    /// Shows the window. First button = Yes, second = No, Cancel / Esc / Close = Cancel.
    /// With only one button (a notice), Yes is returned.
    /// </summary>
    public static DialogChoice Show(Window? owner, string title, string message, string primaryText,
        string? secondaryText, string? cancelText, bool danger)
    {
        var dialog = new DialogWindow(title, message, primaryText, secondaryText, cancelText, danger);
        if (owner != null && owner.IsVisible)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
        return dialog.Choice;
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        Choice = DialogChoice.Yes;
        Close();
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        Choice = DialogChoice.No;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Choice = DialogChoice.Cancel;
        Close();
    }

    /// <summary>Esc closes the window like "Cancel" (doesn't matter for plain notices).</summary>
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Choice = DialogChoice.Cancel;
        e.Handled = true;
        Close();
    }
}
