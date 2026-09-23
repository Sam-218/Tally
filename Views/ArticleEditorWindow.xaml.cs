using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tally.Models;
using Tally.Services;
using Tally.ViewModels;

namespace Tally.Views;

/// <summary>
/// Window for maintaining the article list and categories. Everything goes through MainViewModel;
/// saving happens automatically (the changes land in the same collections as the main window).
/// </summary>
public partial class ArticleEditorWindow : Window
{
    public ArticleEditorWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ThemeManager.Attach(this);

        viewModel.FocusRequested += OnFocusRequested;
        Closed += (_, _) => viewModel.FocusRequested -= OnFocusRequested;
        Loaded += (_, _) => NewArticleBox.Focus();
    }

    private void OnFocusRequested(string field)
    {
        var target = field switch
        {
            "NewCategoryName" => NewCategoryBox,
            "NewArticleName" => NewArticleBox,
            _ => null,
        };
        if (target == null) return;

        // only focus after the current event, otherwise WPF immediately takes the focus back away
        Dispatcher.InvokeAsync(() =>
        {
            target.Focus();
            target.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Esc closes the window – unless a field has already handled Esc itself.</summary>
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.Handled) return;
        e.Handled = true;
        Close();
    }

    // ------------------------------------------------------------------ Article rows

    private void ArticleName_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box) CommitName(box);
    }

    private void ArticleName_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;

        if (e.Key == Key.Enter)
        {
            CommitName(box);
            box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // discard the change: pull the text back from the article
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            box.SelectAll();
            e.Handled = true; // don't close the window
        }
    }

    /// <summary>Applies the typed name (trimmed). An empty name is discarded.</summary>
    private static void CommitName(TextBox box)
    {
        if (box.DataContext is not Article article) return;

        var text = box.Text.Trim();
        if (text.Length > 0) article.Name = text;

        // shows the applied name (or, for an empty field, the old one)
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }

    private void ArticleCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        e.Handled = true; // don't pass it on to the surrounding list

        // Only write back real selections. If a category disappears from the list,
        // the ComboBox reports "nothing selected" – but the article should keep its category name.
        if (sender is ComboBox { DataContext: Article article }
            && e.AddedItems.Count == 1
            && e.AddedItems[0] is Category chosen
            && article.Category != chosen.Name)
        {
            article.Category = chosen.Name;
        }
    }
}
