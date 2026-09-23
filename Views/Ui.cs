using System.Windows;

namespace Tally.Views;

/// <summary>Small extra properties for input fields (placeholder text, red border on error).</summary>
public static class Ui
{
    public static readonly DependencyProperty IsInvalidProperty =
        DependencyProperty.RegisterAttached("IsInvalid", typeof(bool), typeof(Ui), new PropertyMetadata(false));

    public static bool GetIsInvalid(DependencyObject obj) => (bool)obj.GetValue(IsInvalidProperty);
    public static void SetIsInvalid(DependencyObject obj, bool value) => obj.SetValue(IsInvalidProperty, value);

    /// <summary>Marks the selected tab button in the main area.</summary>
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.RegisterAttached("IsActive", typeof(bool), typeof(Ui), new PropertyMetadata(false));

    public static bool GetIsActive(DependencyObject obj) => (bool)obj.GetValue(IsActiveProperty);
    public static void SetIsActive(DependencyObject obj, bool value) => obj.SetValue(IsActiveProperty, value);

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.RegisterAttached("Placeholder", typeof(string), typeof(Ui), new PropertyMetadata(""));

    public static string GetPlaceholder(DependencyObject obj) => (string)obj.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject obj, string value) => obj.SetValue(PlaceholderProperty, value);
}
