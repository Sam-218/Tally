using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Tally.Models;
using Tally.Services;
using Tally.ViewModels;

namespace Tally.Views;

/// <summary>
/// Main window. The logic lives in MainViewModel; this only holds what's directly tied to the UI:
/// autocomplete on the drink field, keyboard shortcuts, focus, and window size.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _applyingSuggestion;

    public MainWindow(MainViewModel viewModel, AppSettings settings)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        ThemeManager.Attach(this);
        ApplySavedBounds(settings);

        _vm.FocusRequested += OnFocusRequested;
        Deactivated += (_, _) => CloseSuggestions();
        LocationChanged += (_, _) => CloseSuggestions(); // popups don't follow along while the window is moved
        SizeChanged += (_, _) => CloseSuggestions();
        Loaded += (_, _) =>
        {
            if (_vm.HasSelectedParty) NameBox.Focus();
            else NewPartyBox.Focus();
        };
    }

    // ------------------------------------------------------------------ Window size and exit

    private void ApplySavedBounds(AppSettings s)
    {
        if (s.WindowWidth is { } w && s.WindowHeight is { } h && w >= MinWidth && h >= MinHeight)
        {
            Width = Math.Min(w, SystemParameters.VirtualScreenWidth);
            Height = Math.Min(h, SystemParameters.VirtualScreenHeight);

            if (s.WindowLeft is { } left && s.WindowTop is { } top && IsReachable(left, top, Width))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
        }

        if (s.WindowMaximized) WindowState = WindowState.Maximized;
    }

    /// <summary>Is the title bar still on a screen? (Otherwise the window would be unreachable after a monitor change.)</summary>
    private static bool IsReachable(double left, double top, double width)
    {
        var vl = SystemParameters.VirtualScreenLeft;
        var vt = SystemParameters.VirtualScreenTop;
        var vw = SystemParameters.VirtualScreenWidth;
        var vh = SystemParameters.VirtualScreenHeight;

        var horizontal = left + width > vl + 100 && left < vl + vw - 100;
        var vertical = top > vt - 5 && top < vt + vh - 60;
        return horizontal && vertical;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;

        CommitPartyName(); // a not-yet-confirmed new party name doesn't get lost
        if (!_vm.PrepareExit())
        {
            e.Cancel = true;
            return;
        }

        var maximized = WindowState == WindowState.Maximized;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _vm.SaveWindowBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height, maximized);
    }

    // ------------------------------------------------------------------ Focus requests from the ViewModel

    private void OnFocusRequested(string field)
    {
        Control? target = field switch
        {
            "Name" => NameBox,
            "Quantity" => QuantityBox,
            "NewPartyName" => NewPartyBox,
            "NewGuestName" => NewGuestBox,
            _ => null,
        };
        if (target == null) return;

        // only focus after the current event (and after the UI has been built)
        Dispatcher.InvokeAsync(() =>
        {
            CloseSuggestions();
            target.Focus();
            if (target is TextBox box) box.SelectAll();
        }, DispatcherPriority.Input);
    }

    // ------------------------------------------------------------------ Party name (heading)

    private void PartyName_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitPartyName();

    private void PartyName_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitPartyName();
            NameBox.Focus(); // move straight on to the first drink
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            PartyNameBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget(); // discard the change
            NameBox.Focus();
            e.Handled = true;
        }
    }

    /// <summary>Applies the typed name; an empty name becomes "Unbenannte Party".</summary>
    private void CommitPartyName()
    {
        if (_vm.SelectedParty is not { } party) return;

        var text = PartyNameBox.Text.Trim();
        party.Name = text.Length == 0 ? LocalizationManager.T("S_UnnamedParty") : text;
        PartyNameBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }

    // ------------------------------------------------------------------ Form: keys

    private void Form_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Esc cancels editing (if the suggestion list is currently open, Esc first just closes that)
        if (e.Key == Key.Escape && _vm.IsEditing && !SuggestPopup.IsOpen && !CategoryBox.IsDropDownOpen)
        {
            _vm.CancelEditCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Enter in the other text fields saves the item (in the name field, Enter picks a suggestion)
        if (e.Key == Key.Enter && e.OriginalSource is TextBox box && !ReferenceEquals(box, NameBox))
        {
            _vm.SubmitItemCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ Autocomplete for the drink field

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // only react when the user is typing – not when the ViewModel sets the text
        if (_applyingSuggestion || !NameBox.IsKeyboardFocused) return;
        UpdateSuggestions();
    }

    private void NameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CloseSuggestions();

    private void NameBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var open = SuggestPopup.IsOpen;

        switch (e.Key)
        {
            case Key.Down:
                if (!open) UpdateSuggestions();
                if (SuggestPopup.IsOpen) MoveSuggestion(+1);
                e.Handled = true;
                break;

            case Key.Up:
                if (open)
                {
                    MoveSuggestion(-1);
                    e.Handled = true;
                }
                break;

            case Key.Enter:
                e.Handled = true;
                if (open && ApplyHighlightedSuggestion()) break;

                // no suggestion chosen: if the name matches an article exactly, apply its category
                if (!_vm.IsEditing) ApplyExactMatch();
                CloseSuggestions();
                QuantityBox.Focus();
                QuantityBox.SelectAll();
                break;

            case Key.Tab:
                if (open && SuggestList.SelectedItem != null)
                {
                    ApplyHighlightedSuggestion();
                    e.Handled = true;
                }
                else
                {
                    CloseSuggestions(); // Tab moves to the next field as usual
                }
                break;

            case Key.Escape:
                if (open)
                {
                    CloseSuggestions();
                    e.Handled = true;
                }
                break;
        }
    }

    private void UpdateSuggestions()
    {
        var matches = _vm.SuggestArticles(NameBox.Text);
        if (matches.Count == 0)
        {
            CloseSuggestions();
            return;
        }

        SuggestList.ItemsSource = matches;
        SuggestList.SelectedIndex = -1;
        SuggestBorder.Width = Math.Max(NameBox.ActualWidth, 240);
        SuggestPopup.IsOpen = true;
    }

    private void CloseSuggestions()
    {
        if (SuggestPopup.IsOpen) SuggestPopup.IsOpen = false;
    }

    private void MoveSuggestion(int delta)
    {
        var count = SuggestList.Items.Count;
        if (count == 0) return;

        var index = SuggestList.SelectedIndex + delta;
        if (index >= count) index = 0;
        if (index < 0) index = count - 1;

        SuggestList.SelectedIndex = index;
        if (SuggestList.SelectedItem != null) SuggestList.ScrollIntoView(SuggestList.SelectedItem);
    }

    private bool ApplyHighlightedSuggestion()
    {
        if (SuggestList.SelectedItem is not Article article) return false;
        ApplySuggestion(article);
        return true;
    }

    private void ApplySuggestion(Article article)
    {
        _applyingSuggestion = true;
        try { _vm.ApplyArticle(article); }
        finally { _applyingSuggestion = false; }

        CloseSuggestions();
        QuantityBox.Focus();
        QuantityBox.SelectAll();
    }

    private void ApplyExactMatch()
    {
        var typed = NameBox.Text.Trim();
        if (typed.Length == 0) return;

        var match = _vm.Articles.FirstOrDefault(a => string.Equals(a.Name, typed, StringComparison.OrdinalIgnoreCase));
        if (match == null) return;

        _applyingSuggestion = true;
        try { _vm.ApplyArticle(match); }
        finally { _applyingSuggestion = false; }
    }

    /// <summary>Click on a suggestion. Deliberately handled on mouse-down, before anything shifts focus.</summary>
    private void SuggestList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(SuggestList, source) is not ListBoxItem container) return;
        if (container.DataContext is not Article article) return;

        e.Handled = true;
        ApplySuggestion(article);
    }

    // ------------------------------------------------------------------ Guest rows

    private void GuestName_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box) CommitGuestName(box);
    }

    private void GuestName_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;

        if (e.Key == Key.Enter)
        {
            CommitGuestName(box);
            box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); // on to the amount
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // discard the change: pull the text back from the guest
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            box.SelectAll();
            e.Handled = true;
        }
    }

    /// <summary>Applies the typed name (trimmed). An empty name is discarded.</summary>
    private static void CommitGuestName(TextBox box)
    {
        if (box.DataContext is not Guest guest) return;

        var text = box.Text.Trim();
        if (text.Length > 0) guest.Name = text;

        box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }

    private void GuestMoney_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box) CommitGuestMoney(box);
    }

    private void GuestMoney_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;

        if (e.Key == Key.Enter)
        {
            // Enter means "done with this guest" - moving the focus on would land on a button
            CommitGuestMoney(box);
            box.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            box.SelectAll();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Applies a typed amount. An empty field means 0 €; anything unreadable (or negative)
    /// is discarded and the field snaps back to the stored value - just like the date field.
    /// </summary>
    private static void CommitGuestMoney(TextBox box)
    {
        if (box.DataContext is not Guest guest) return;
        var paidField = (string?)box.Tag == "paid";

        var text = box.Text.Trim();
        if (text.Length == 0)
        {
            if (paidField) guest.Paid = 0; else guest.Owes = 0;
        }
        else if (Fmt.TryParse(text, out var value) && value >= 0)
        {
            if (paidField) guest.Paid = value; else guest.Owes = value;
        }

        // shows the applied amount (or, for invalid input, the old one) as "12,50"
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }
}
