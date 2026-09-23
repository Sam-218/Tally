using System.Collections.ObjectModel;
using System.Windows.Input;
using Tally.Models;
using Tally.Services;

namespace Tally.ViewModels;

public sealed partial class MainViewModel
{
    private enum PriceField { Unit, Total }

    private string _formName = "";
    private string _formCategory = "";
    private string _formQuantity = "1";
    private string _formUnitPrice = "";
    private string _formTotalPrice = "";
    private string _formStore = "";
    private bool _nameInvalid;
    private bool _quantityInvalid;
    private bool _recalculating;
    private PriceField _lastEdited = PriceField.Unit;
    private Item? _editingItem;

    /// <summary>Categories in the form's selection field (while editing, may include a deleted category).</summary>
    public ObservableCollection<string> FormCategoryOptions { get; } = new();

    public string FormName
    {
        get => _formName;
        set { if (Set(ref _formName, value ?? "")) NameInvalid = false; }
    }

    public string FormCategory
    {
        get => _formCategory;
        set
        {
            if (value == null) return; // the ComboBox reports null while its list is briefly being rebuilt
            Set(ref _formCategory, value);
        }
    }

    public string FormQuantity
    {
        get => _formQuantity;
        set
        {
            if (!Set(ref _formQuantity, value ?? "")) return;
            QuantityInvalid = false;
            if (_recalculating) return;
            if (_lastEdited == PriceField.Total) RecalcUnitFromTotal(); else RecalcTotalFromUnit();
        }
    }

    public string FormUnitPrice
    {
        get => _formUnitPrice;
        set
        {
            if (!Set(ref _formUnitPrice, value ?? "") || _recalculating) return;
            _lastEdited = PriceField.Unit;
            RecalcTotalFromUnit();
        }
    }

    public string FormTotalPrice
    {
        get => _formTotalPrice;
        set
        {
            if (!Set(ref _formTotalPrice, value ?? "") || _recalculating) return;
            _lastEdited = PriceField.Total;
            RecalcUnitFromTotal();
        }
    }

    public string FormStore { get => _formStore; set => Set(ref _formStore, value ?? ""); }

    public bool NameInvalid { get => _nameInvalid; private set => Set(ref _nameInvalid, value); }
    public bool QuantityInvalid { get => _quantityInvalid; private set => Set(ref _quantityInvalid, value); }

    public bool IsEditing => _editingItem != null;
    public string SubmitButtonText => IsEditing ? LocalizationManager.T("S_Save") : LocalizationManager.T("S_AddButton");

    // ---- price automation: unit price <-> total price (as in the HTML version) ----

    private void RecalcTotalFromUnit()
    {
        _recalculating = true;
        try { FormTotalPrice = Fmt.Fixed2(Fmt.ParseOrZero(FormQuantity) * Fmt.ParseOrZero(FormUnitPrice)); }
        finally { _recalculating = false; }
    }

    private void RecalcUnitFromTotal()
    {
        _recalculating = true;
        try
        {
            var qty = Fmt.ParseOrZero(FormQuantity);
            FormUnitPrice = qty > 0 ? Fmt.Fixed2(Fmt.ParseOrZero(FormTotalPrice) / qty) : Fmt.Fixed2(0);
        }
        finally { _recalculating = false; }
    }

    // ---- commands ----

    private ICommand? _submitItem;
    public ICommand SubmitItemCommand => _submitItem ??= new RelayCommand(_ => SubmitItem());

    private ICommand? _cancelEdit;
    public ICommand CancelEditCommand => _cancelEdit ??= new RelayCommand(_ => ClearForm());

    private ICommand? _editItem;
    public ICommand EditItemCommand => _editItem ??= new RelayCommand(i => BeginEdit(i as Item));

    private ICommand? _deleteItem;
    public ICommand DeleteItemCommand => _deleteItem ??= new RelayCommand(i => DeleteItem(i as Item));

    private void SubmitItem()
    {
        var party = _selectedParty;
        if (party == null) return;

        var name = FormName.Trim();
        var quantity = Fmt.ParseOrZero(FormQuantity);
        var unitPrice = Fmt.ParseOrZero(FormUnitPrice);
        var totalPrice = Fmt.ParseOrZero(FormTotalPrice);
        if (totalPrice == 0) totalPrice = quantity * unitPrice;
        var category = FormCategory ?? "";
        var store = FormStore.Trim();

        NameInvalid = name.Length == 0;
        QuantityInvalid = quantity <= 0;
        if (NameInvalid) { RequestFocus("Name"); return; }
        if (QuantityInvalid) { RequestFocus("Quantity"); return; }

        if (_editingItem != null && party.Items.Contains(_editingItem))
        {
            var item = _editingItem;
            item.Name = name;
            item.Category = category;
            item.Quantity = quantity;
            item.UnitPrice = unitPrice;
            item.TotalPrice = totalPrice;
            item.Store = store;
        }
        else
        {
            party.Items.Add(new Item
            {
                Name = name,
                Category = category,
                Quantity = quantity,
                UnitPrice = unitPrice,
                TotalPrice = totalPrice,
                Store = store,
            });
        }

        ClearForm();
        RequestFocus("Name"); // go straight to typing the next drink
    }

    private void BeginEdit(Item? item)
    {
        if (item == null) return;
        _editingItem = item;
        RefreshFormCategoryOptions();

        _recalculating = true;
        try
        {
            FormName = item.Name;
            FormCategory = item.Category;
            FormQuantity = Fmt.Qty(item.Quantity);
            FormUnitPrice = Fmt.Fixed2(item.UnitPrice);
            FormTotalPrice = Fmt.Fixed2(item.TotalPrice);
            FormStore = item.Store;
        }
        finally { _recalculating = false; }

        _lastEdited = PriceField.Unit;
        NameInvalid = false;
        QuantityInvalid = false;
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(SubmitButtonText));
        RequestFocus("Name");
    }

    private void DeleteItem(Item? item)
    {
        if (item == null || _selectedParty == null) return;
        var wasEditing = ReferenceEquals(item, _editingItem);
        _selectedParty.Items.Remove(item);
        if (wasEditing) ClearForm();
    }

    /// <summary>Reset the form (after adding, canceling, or switching party).</summary>
    private void ClearForm()
    {
        _editingItem = null;
        RefreshFormCategoryOptions();

        _recalculating = true;
        try
        {
            FormName = "";
            FormCategory = Categories.FirstOrDefault()?.Name ?? "";
            FormQuantity = "1";
            FormUnitPrice = "";
            FormTotalPrice = "";
            FormStore = "";
        }
        finally { _recalculating = false; }

        _lastEdited = PriceField.Unit;
        NameInvalid = false;
        QuantityInvalid = false;
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(SubmitButtonText));
    }

    // ---- category selection in the form ----

    private void RefreshFormCategoryOptions()
    {
        var desired = Categories.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();

        // While editing: still offer the item's category even if it has since been deleted,
        // so it doesn't get silently changed when saved.
        var extra = _editingItem?.Category;
        if (!string.IsNullOrWhiteSpace(extra) && !desired.Contains(extra)) desired.Add(extra);

        CollectionSync.Sync(FormCategoryOptions, desired);

        if (!desired.Contains(_formCategory) && !_recalculating)
            FormCategory = desired.FirstOrDefault() ?? "";
    }

    // ---- autocomplete ----

    /// <summary>Up to 8 matching articles: first the ones starting with the text, then the ones containing it.</summary>
    public IReadOnlyList<Article> SuggestArticles(string? query)
    {
        var q = query?.Trim();
        if (string.IsNullOrEmpty(q)) return Array.Empty<Article>();

        return Articles
            .Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Take(8)
            .ToList();
    }

    /// <summary>Applies an article from the suggestion list to the form (name + category).</summary>
    public void ApplyArticle(Article article)
    {
        FormName = article.Name;
        if (!string.IsNullOrWhiteSpace(article.Category))
        {
            if (!FormCategoryOptions.Contains(article.Category)) FormCategoryOptions.Add(article.Category);
            FormCategory = article.Category;
        }
    }
}
