using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Text.Json.Serialization;
using Tally.Services;

namespace Tally.Models;

public sealed record CategoryTotal(string Category, decimal Sum);

/// <summary>A party with its drink line items (called "event" in the HTML version).</summary>
public sealed class Party : ModelBase
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private string _id = Ids.New();
    private string _name = "";
    private DateTime? _date;
    private ObservableCollection<Item> _items = new();
    private ObservableCollection<Guest> _guests = new();

    public Party()
    {
        Hook(_items);
        Hook(_guests);
    }

    [JsonPropertyName("id")]
    public string Id { get => _id; set => SetData(ref _id, value ?? Ids.New()); }

    [JsonPropertyName("name")]
    public string Name { get => _name; set => SetData(ref _name, value ?? ""); }

    [JsonPropertyName("date")]
    [JsonConverter(typeof(DateStringConverter))]
    public DateTime? Date
    {
        get => _date;
        set
        {
            if (SetData(ref _date, value?.Date))
                RaisePropertyChanged(nameof(DateText));
        }
    }

    [JsonPropertyName("items")]
    public ObservableCollection<Item> Items
    {
        get => _items;
        set
        {
            Unhook(_items);
            _items = value ?? new ObservableCollection<Item>();
            Hook(_items);
            RaisePropertyChanged();
            RefreshTotals();
            RaiseDataChanged();
        }
    }

    [JsonPropertyName("guests")]
    public ObservableCollection<Guest> Guests
    {
        get => _guests;
        set
        {
            Unhook(_guests);
            _guests = value ?? new ObservableCollection<Guest>();
            Hook(_guests);
            RaisePropertyChanged();
            RefreshGuestTotals();
            RaiseDataChanged();
        }
    }

    // ---- computed values (not saved) ----

    [JsonIgnore] public decimal TotalPrice => _items.Sum(i => i.TotalPrice);
    [JsonIgnore] public int ItemCount => _items.Count;
    [JsonIgnore]
    public string ItemCountText => string.Format(
        LocalizationManager.T(_items.Count == 1 ? "S_ItemCountSingular" : "S_ItemCountPlural"), _items.Count);
    [JsonIgnore] public string DateText => _date?.ToString("dd.MM.yyyy", De) ?? "";

    [JsonIgnore]
    public IReadOnlyList<CategoryTotal> CategoryTotals =>
        _items
            .GroupBy(i => string.IsNullOrWhiteSpace(i.Category) ? LocalizationManager.T("S_NoCategory") : i.Category)
            .Select(g => new CategoryTotal(g.Key, g.Sum(x => x.TotalPrice)))
            .OrderByDescending(x => x.Sum)
            .ToList();

    // ---- guests: who owes what, and what the party ends up costing ----

    [JsonIgnore] public int GuestCount => _guests.Count;
    [JsonIgnore] public decimal GuestsOwed => _guests.Sum(g => g.Owes);
    [JsonIgnore] public decimal GuestsPaid => _guests.Sum(g => g.Paid);
    [JsonIgnore] public decimal GuestsOpen => _guests.Sum(g => g.Owes - g.Paid);

    /// <summary>
    /// Money actually collected minus what the drinks cost. Negative for most of a party's
    /// life (the drinks are bought up front) - that is the normal state, not an error.
    /// </summary>
    [JsonIgnore] public decimal Profit => GuestsPaid - TotalPrice;
    [JsonIgnore] public bool IsProfitNegative => Profit < 0;

    [JsonIgnore]
    public string GuestCountText => string.Format(
        LocalizationManager.T(_guests.Count == 1 ? "S_GuestCountSingular" : "S_GuestCountPlural"), _guests.Count);

    /// <summary>Re-raises PropertyChanged for the text properties above when the UI language changes.</summary>
    public void RefreshLocalizedText()
    {
        RaisePropertyChanged(nameof(ItemCountText));
        RaisePropertyChanged(nameof(CategoryTotals));
        RaisePropertyChanged(nameof(GuestCountText));
    }

    // ---- forward item changes to the party ----

    private void Hook(ObservableCollection<Item> items)
    {
        items.CollectionChanged += OnItemsChanged;
        foreach (var item in items) item.DataChanged += OnItemDataChanged;
    }

    private void Unhook(ObservableCollection<Item> items)
    {
        items.CollectionChanged -= OnItemsChanged;
        foreach (var item in items) item.DataChanged -= OnItemDataChanged;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (Item item in e.OldItems) item.DataChanged -= OnItemDataChanged;
        if (e.NewItems != null)
            foreach (Item item in e.NewItems) item.DataChanged += OnItemDataChanged;

        RefreshTotals();
        RaiseDataChanged();
    }

    private void OnItemDataChanged(object? sender, EventArgs e)
    {
        RefreshTotals();
        RaiseDataChanged();
    }

    private void RefreshTotals()
    {
        RaisePropertyChanged(nameof(TotalPrice));
        RaisePropertyChanged(nameof(ItemCount));
        RaisePropertyChanged(nameof(ItemCountText));
        RaisePropertyChanged(nameof(CategoryTotals));
        RaiseProfitChanged();   // buying a drink changes the result as well
    }

    // ---- forward guest changes to the party (same idea as the items above) ----

    private void Hook(ObservableCollection<Guest> guests)
    {
        guests.CollectionChanged += OnGuestsChanged;
        foreach (var guest in guests) guest.DataChanged += OnGuestDataChanged;
    }

    private void Unhook(ObservableCollection<Guest> guests)
    {
        guests.CollectionChanged -= OnGuestsChanged;
        foreach (var guest in guests) guest.DataChanged -= OnGuestDataChanged;
    }

    private void OnGuestsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (Guest guest in e.OldItems) guest.DataChanged -= OnGuestDataChanged;
        if (e.NewItems != null)
            foreach (Guest guest in e.NewItems) guest.DataChanged += OnGuestDataChanged;

        RefreshGuestTotals();
        RaiseDataChanged();
    }

    private void OnGuestDataChanged(object? sender, EventArgs e)
    {
        RefreshGuestTotals();
        RaiseDataChanged();
    }

    private void RefreshGuestTotals()
    {
        RaisePropertyChanged(nameof(GuestCount));
        RaisePropertyChanged(nameof(GuestCountText));
        RaisePropertyChanged(nameof(GuestsOwed));
        RaisePropertyChanged(nameof(GuestsPaid));
        RaisePropertyChanged(nameof(GuestsOpen));
        RaiseProfitChanged();
    }

    private void RaiseProfitChanged()
    {
        RaisePropertyChanged(nameof(Profit));
        RaisePropertyChanged(nameof(IsProfitNegative));
    }
}
