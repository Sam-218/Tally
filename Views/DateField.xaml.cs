using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Tally.Views;

/// <summary>
/// Date field in the app's own style: type a date (14.06.2025, 14.6.25 ...) or pick one via the calendar.
/// SelectedDate is bindable; an empty field means "no date" (null).
/// </summary>
public partial class DateField : UserControl
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static readonly string[] Formats = { "d.M.yyyy", "dd.MM.yyyy", "d.M.yy", "yyyy-MM-dd", "d.M." };

    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime?), typeof(DateField),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedDateChanged));

    private DateTime _viewMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    /// <summary>Not-yet-applied but validly typed date – used only to highlight it in the open calendar.</summary>
    private DateTime? _typedDate;

    public DateField()
    {
        InitializeComponent();
        ShowText();
    }

    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((DateField)d).ShowText();

    private void ShowText() => Input.Text = SelectedDate?.ToString("dd.MM.yyyy", De) ?? "";

    /// <summary>Applies the typed text – invalid input reverts to the previous date.</summary>
    private void Commit()
    {
        var text = Input.Text.Trim();
        if (text.Length == 0)
        {
            SelectedDate = null;
        }
        else if (DateTime.TryParseExact(text, Formats, De, DateTimeStyles.None, out var date))
        {
            SelectedDate = date.Date;
        }
        _typedDate = null;
        ShowText();
    }

    private void Input_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit();

    /// <summary>
    /// While the calendar is open, keep it live: a validly typed (but not yet applied)
    /// date is immediately highlighted in the calendar and the month jumps along automatically,
    /// instead of the calendar staying put and no longer matching the typed text.
    /// </summary>
    private void Input_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!CalendarPopup.IsOpen) return;

        var text = Input.Text.Trim();
        if (text.Length > 0 && DateTime.TryParseExact(text, Formats, De, DateTimeStyles.None, out var date))
        {
            _typedDate = date.Date;
            _viewMonth = new DateTime(date.Year, date.Month, 1);
        }
        else
        {
            _typedDate = null;
        }
        BuildDays();
    }

    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // WPF reports Alt+Down as Key.System with SystemKey.Down
        var altDown = e.Key == Key.System && e.SystemKey == Key.Down;

        if (e.Key == Key.F4 || altDown)
        {
            Commit();
            OpenCalendar();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Commit();
            CalendarPopup.IsOpen = false;
            Input.SelectAll();
        }
        else if (e.Key == Key.Escape)
        {
            var wasOpen = CalendarPopup.IsOpen;
            CalendarPopup.IsOpen = false;
            ShowText();
            Input.SelectAll();
            e.Handled = wasOpen; // only "consume" Esc if it actually closed the calendar
        }
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        Commit();
        OpenCalendar();
    }

    private void OpenCalendar()
    {
        _typedDate = null;
        var basis = SelectedDate ?? DateTime.Today;
        _viewMonth = new DateTime(basis.Year, basis.Month, 1);
        BuildDays();
        CalendarPopup.IsOpen = true;
    }

    private void BuildDays()
    {
        MonthText.Text = _viewMonth.ToString("MMMM yyyy", De);
        DaysGrid.Children.Clear();

        var offset = ((int)_viewMonth.DayOfWeek + 6) % 7; // Monday = 0
        var start = _viewMonth.AddDays(-offset);
        var dayStyle = (Style)FindResource("CalDayButton");

        for (var i = 0; i < 42; i++)
        {
            var day = start.AddDays(i);
            var button = new Button
            {
                Content = day.Day.ToString(De),
                Tag = day,
                Style = dayStyle,
            };

            if (day.Month != _viewMonth.Month) button.Opacity = 0.45;
            if (day.Date == DateTime.Today) button.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
            if ((_typedDate ?? SelectedDate)?.Date == day.Date)
            {
                button.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
                button.SetResourceReference(Control.ForegroundProperty, "OnAccentBrush");
            }

            button.Click += Day_Click;
            DaysGrid.Children.Add(button);
        }
    }

    private void Day_Click(object sender, RoutedEventArgs e)
    {
        SelectedDate = ((DateTime)((Button)sender).Tag).Date;
        CalendarPopup.IsOpen = false;
    }

    private void PrevMonth_Click(object sender, RoutedEventArgs e)
    {
        _viewMonth = _viewMonth.AddMonths(-1);
        BuildDays();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _viewMonth = _viewMonth.AddMonths(1);
        BuildDays();
    }

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        SelectedDate = DateTime.Today;
        CalendarPopup.IsOpen = false;
    }
}
