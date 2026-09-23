using System.Text.Json.Serialization;

namespace Tally.Models;

/// <summary>
/// A guest of a party: what they are supposed to pay and what they have paid so far.
/// Both amounts are always typed by hand - nothing here is ever split or calculated
/// automatically, so an entered amount never changes on its own.
/// </summary>
public sealed class Guest : ModelBase
{
    private string _id = Ids.New();
    private string _name = "";
    private decimal _owes;
    private decimal _paid;

    [JsonPropertyName("id")]
    public string Id { get => _id; set => SetData(ref _id, value ?? Ids.New()); }

    [JsonPropertyName("name")]
    public string Name { get => _name; set => SetData(ref _name, value ?? ""); }

    /// <summary>What the guest is supposed to pay.</summary>
    [JsonPropertyName("owes")]
    public decimal Owes
    {
        get => _owes;
        set { if (SetData(ref _owes, value)) RefreshOpen(); }
    }

    /// <summary>What the guest has already paid (part payments are allowed).</summary>
    [JsonPropertyName("paid")]
    public decimal Paid
    {
        get => _paid;
        set { if (SetData(ref _paid, value)) RefreshOpen(); }
    }

    // ---- computed values (not saved) ----

    /// <summary>Still to be paid. Negative if the guest paid too much.</summary>
    [JsonIgnore] public decimal Open => _owes - _paid;

    /// <summary>Nothing left to pay (and there was something to pay in the first place).</summary>
    [JsonIgnore] public bool IsSettled => _owes > 0 && _owes - _paid <= 0;

    private void RefreshOpen()
    {
        RaisePropertyChanged(nameof(Open));
        RaisePropertyChanged(nameof(IsSettled));
    }
}
