using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Tally.Models;

/// <summary>
/// Base class for all data objects. Reports changes to the UI (PropertyChanged)
/// and additionally via DataChanged whenever something changes that needs to be saved.
/// Automatic saving hooks into this: every change to a data object triggers
/// a (debounced) save, without anyone having to remember to call it.
/// </summary>
public abstract class ModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Only raised when saved data has actually changed.</summary>
    public event EventHandler? DataChanged;

    /// <summary>Sets a saved field and reports the change.</summary>
    protected bool SetData<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        RaisePropertyChanged(name);
        RaiseDataChanged();
        return true;
    }

    protected void RaisePropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void RaiseDataChanged()
        => DataChanged?.Invoke(this, EventArgs.Empty);
}

public static class Ids
{
    /// <summary>New unique ID (as text, so old files from the HTML version stay compatible).</summary>
    public static string New() => Guid.NewGuid().ToString("N");
}
