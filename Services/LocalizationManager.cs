using System.Windows;

namespace Tally.Services;

/// <summary>
/// Switches the UI language between German and English at runtime, the same way
/// ThemeManager swaps colors: a merged ResourceDictionary of strings gets replaced,
/// and every XAML "{DynamicResource S_...}" binding updates itself automatically.
/// Number and date formatting always stays German (see Fmt.De) regardless of this setting.
/// </summary>
public static class LocalizationManager
{
    public static string Current { get; private set; } = "de";

    /// <summary>Raised after the language has changed, so ViewModels can refresh computed text.</summary>
    public static event Action? Changed;

    public static void Apply(string lang)
    {
        Current = lang == "en" ? "en" : "de";
        var app = Application.Current;
        if (app == null) return;

        var file = Current == "en" ? "English" : "German";
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Languages/{file}.xaml", UriKind.Absolute)
        };

        // The string dictionary is the one that contains "S_AppTitle" – swap it, every DynamicResource follows.
        var merged = app.Resources.MergedDictionaries;
        var replaced = false;
        for (var i = 0; i < merged.Count; i++)
        {
            if (!merged[i].Contains("S_AppTitle")) continue;
            merged[i] = dict;
            replaced = true;
            break;
        }
        if (!replaced) merged.Insert(0, dict);

        Changed?.Invoke();
    }

    /// <summary>Looks up a string resource for use from C# (e.g. in dialog messages).</summary>
    public static string T(string key)
        => Application.Current?.TryFindResource(key) as string ?? key;
}
