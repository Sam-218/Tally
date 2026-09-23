using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Tally.Models;
using Tally.Services;

namespace Tally.ViewModels;

/// <summary>
/// Main application logic. Split across several files:
///   MainViewModel.cs           – data, parties, theme, exit
///   MainViewModel.ItemForm.cs  – entry form (add / edit drink)
///   MainViewModel.Guests.cs    – guest list (who owes what) and the tab switch
///   MainViewModel.Articles.cs  – article and category list
///   MainViewModel.Storage.cs   – save status, backups, import/export, changing folders
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDialogService _ui;
    private readonly AppSettings _settings;
    private readonly Action<Action> _post;
    private DataStore _store;
    private AppData _data;
    private Party? _selectedParty;
    private string _theme;
    private string _language;

    public MainViewModel(IDialogService ui, AppSettings settings, DataStore store, LoadResult load,
        Action<Action>? post = null)
    {
        _ui = ui;
        _settings = settings;
        _store = store;
        _data = load.Data;
        _post = post ?? (a => a());
        _theme = settings.Theme == "light" ? "light" : "dark";
        _language = LocalizationManager.Current;

        AttachData(_data);
        RefreshAfterDataReplaced(_data.ActiveEventId);

        _store.StatusChanged += OnStoreStatusChanged;
        _store.Start(Snapshot);
        TryBackup("start");
        if (load.NeedsSave) _store.RequestSave();
        UpdateStoragePanel(_store.Status);

        LocalizationManager.Changed += OnLanguageChanged;
    }

    private string Snapshot() => _data.ToJson();

    /// <summary>The UI should focus a specific input field ("Name", "Quantity", ...).</summary>
    public event Action<string>? FocusRequested;

    private void RequestFocus(string field) => FocusRequested?.Invoke(field);

    // ------------------------------------------------------------------ Data

    public ObservableCollection<Party> Parties => _data.Parties;

    /// <summary>Parties sorted by date (newest first) – for the sidebar.</summary>
    public ObservableCollection<Party> SortedParties { get; } = new();

    public Party? SelectedParty
    {
        get => _selectedParty;
        set
        {
            if (ReferenceEquals(_selectedParty, value)) return;

            // The ListBox briefly reports null while the list is being re-sorted – we ignore that,
            // as long as the previous party is still in the list.
            if (value == null && _selectedParty != null && SortedParties.Contains(_selectedParty))
            {
                OnPropertyChanged();
                return;
            }

            SetSelected(value, save: true);
        }
    }

    public bool HasSelectedParty => _selectedParty != null;

    private void SetSelected(Party? party, bool save)
    {
        _selectedParty = party;
        _data.ActiveEventId = party?.Id;
        OnPropertyChanged(nameof(SelectedParty));
        OnPropertyChanged(nameof(HasSelectedParty));
        ClearForm();
        ClearGuestForm();
        ResetTab();             // another party always opens on the drinks tab
        if (save) _store.RequestSave();
    }

    private void SyncSortedParties()
    {
        // OrderByDescending is stable: entries with the same date keep their relative order
        var desired = _data.Parties.OrderByDescending(p => p.Date ?? DateTime.MinValue).ToList();
        CollectionSync.Sync(SortedParties, desired);
    }

    // ---- track changes: every change to the data automatically triggers a save ----

    private void AttachData(AppData d)
    {
        d.Parties.CollectionChanged += OnPartiesCollectionChanged;
        foreach (var p in d.Parties) AttachParty(p);

        d.Articles!.CollectionChanged += OnArticlesCollectionChanged;
        foreach (var a in d.Articles) a.DataChanged += OnDataItemChanged;

        d.Categories!.CollectionChanged += OnCategoriesCollectionChanged;
        foreach (var c in d.Categories) c.DataChanged += OnDataItemChanged;
    }

    private void DetachData(AppData d)
    {
        d.Parties.CollectionChanged -= OnPartiesCollectionChanged;
        foreach (var p in d.Parties) DetachParty(p);

        d.Articles!.CollectionChanged -= OnArticlesCollectionChanged;
        foreach (var a in d.Articles) a.DataChanged -= OnDataItemChanged;

        d.Categories!.CollectionChanged -= OnCategoriesCollectionChanged;
        foreach (var c in d.Categories) c.DataChanged -= OnDataItemChanged;
    }

    private void AttachParty(Party p)
    {
        p.DataChanged += OnDataItemChanged;
        p.PropertyChanged += OnPartyPropertyChanged;
    }

    private void DetachParty(Party p)
    {
        p.DataChanged -= OnDataItemChanged;
        p.PropertyChanged -= OnPartyPropertyChanged;
    }

    private void OnDataItemChanged(object? sender, EventArgs e) => _store.RequestSave();

    private void OnPartyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Party.Date)) SyncSortedParties();
    }

    private void OnPartiesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null) foreach (Party p in e.OldItems) DetachParty(p);
        if (e.NewItems != null) foreach (Party p in e.NewItems) AttachParty(p);
        SyncSortedParties();
        _store.RequestSave();
    }

    private void OnArticlesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null) foreach (Article a in e.OldItems) a.DataChanged -= OnDataItemChanged;
        if (e.NewItems != null) foreach (Article a in e.NewItems) a.DataChanged += OnDataItemChanged;
        RefreshArticleList();
        _store.RequestSave();
    }

    private void OnCategoriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null) foreach (Category c in e.OldItems) c.DataChanged -= OnDataItemChanged;
        if (e.NewItems != null) foreach (Category c in e.NewItems) c.DataChanged += OnDataItemChanged;
        RefreshFormCategoryOptions();
        EnsureValidNewArticleCategory();
        _store.RequestSave();
    }

    /// <summary>After import / restore / changing folders: switch over to entirely new data.</summary>
    private void ReplaceData(AppData newData)
    {
        DetachData(_data);
        _data = newData;
        AttachData(_data);
        RefreshAfterDataReplaced(_data.ActiveEventId);
        _store.RequestSave();
    }

    private void RefreshAfterDataReplaced(string? selectId)
    {
        OnPropertyChanged(nameof(Parties));
        OnPropertyChanged(nameof(Articles));
        OnPropertyChanged(nameof(Categories));
        SyncSortedParties();

        var target = _data.Parties.FirstOrDefault(p => p.Id == selectId) ?? SortedParties.FirstOrDefault();
        SetSelected(target, save: false);

        RefreshFormCategoryOptions();
        EnsureValidNewArticleCategory();
        RefreshArticleList();
    }

    // ------------------------------------------------------------------ New party

    private string _newPartyName = "";
    private DateTime? _newPartyDate;

    public string NewPartyName { get => _newPartyName; set => Set(ref _newPartyName, value ?? ""); }

    /// <summary>Date of the new party; empty = today (as in the HTML version).</summary>
    public DateTime? NewPartyDate { get => _newPartyDate; set => Set(ref _newPartyDate, value?.Date); }

    private ICommand? _createParty;
    public ICommand CreatePartyCommand => _createParty ??= new RelayCommand(_ => CreateParty());

    private void CreateParty()
    {
        var name = NewPartyName.Trim();
        if (name.Length == 0) { RequestFocus("NewPartyName"); return; }

        var party = new Party { Name = name, Date = NewPartyDate ?? DateTime.Today };
        _data.Parties.Add(party);
        SetSelected(party, save: true);
        NewPartyName = "";
        NewPartyDate = null;
        RequestFocus("Name");
    }

    private ICommand? _deleteParty;
    public ICommand DeletePartyCommand => _deleteParty ??= new RelayCommand(p => DeleteParty(p as Party));

    private void DeleteParty(Party? party)
    {
        if (party == null) return;
        if (!_ui.Confirm(LocalizationManager.T("S_DeletePartyTitle"),
                string.Format(LocalizationManager.T("S_DeletePartyMsg"), party.Name),
                LocalizationManager.T("S_Delete"), LocalizationManager.T("S_Cancel"), danger: true)) return;

        TryBackup("vor-loeschen", force: true);

        if (ReferenceEquals(party, _selectedParty))
            SetSelected(SortedParties.FirstOrDefault(p => !ReferenceEquals(p, party)), save: false);

        _data.Parties.Remove(party);
    }

    // ------------------------------------------------------------------ Theme

    public string Theme
    {
        get => _theme;
        private set => Set(ref _theme, value);
    }

    public string ThemeToggleGlyph => Theme == "light" ? "◑" : "◐";

    private ICommand? _toggleTheme;
    public ICommand ToggleThemeCommand => _toggleTheme ??= new RelayCommand(_ =>
    {
        Theme = Theme == "light" ? "dark" : "light";
        OnPropertyChanged(nameof(ThemeToggleGlyph));
        _settings.Theme = Theme;
        _settings.Save();
        _ui.ApplyTheme(Theme);
    });

    public string Language
    {
        get => _language;
        private set => Set(ref _language, value);
    }

    public string LanguageToggleGlyph => Language == "en" ? "EN" : "DE";

    private ICommand? _toggleLanguage;
    public ICommand ToggleLanguageCommand => _toggleLanguage ??= new RelayCommand(_ =>
    {
        Language = Language == "en" ? "de" : "en";
        OnPropertyChanged(nameof(LanguageToggleGlyph));
        _settings.Language = Language;
        _settings.Save();
        LocalizationManager.Apply(Language);
    });

    /// <summary>
    /// LocalizationManager swaps the string ResourceDictionary, which refreshes every static
    /// "{DynamicResource S_...}" binding in the XAML by itself. This only has to refresh the
    /// handful of text properties that are computed in code instead (status text, item counts, ...).
    /// </summary>
    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(SubmitButtonText));
        OnPropertyChanged(nameof(ArticleCountText));
        UpdateStoragePanel(_store.Status);
        foreach (var party in _data.Parties) party.RefreshLocalizedText();
    }

    private ICommand? _print;
    /// <summary>Prints whatever the open tab shows - the drinks invoice or the guest list.</summary>
    public ICommand PrintCommand => _print ??= new RelayCommand(_ =>
    {
        if (_selectedParty == null) return;
        if (IsGuestsTab) _ui.PrintGuests(_selectedParty);
        else _ui.PrintParty(_selectedParty);
    });

    // ------------------------------------------------------------------ Exit

    /// <summary>
    /// Called when the window closes: saves immediately, creates a backup.
    /// Returns false if the user wants to cancel exiting (because saving failed).
    /// </summary>
    public bool PrepareExit()
    {
        if (!_store.SaveNowBlocking(out var error))
        {
            var quit = _ui.Confirm(LocalizationManager.T("S_SaveFailedTitle"),
                string.Format(LocalizationManager.T("S_SaveFailedOnExitMsg"), error),
                LocalizationManager.T("S_QuitAnyway"), LocalizationManager.T("S_BackToProgram"), danger: true);
            if (!quit) return false;
        }
        else
        {
            TryBackup("ende");
        }

        _store.Dispose();
        return true;
    }

    public void SaveWindowBounds(double left, double top, double width, double height, bool maximized)
    {
        _settings.WindowLeft = left;
        _settings.WindowTop = top;
        _settings.WindowWidth = width;
        _settings.WindowHeight = height;
        _settings.WindowMaximized = maximized;
        _settings.Save();
    }

    /// <summary>Best-effort backup: an error here must never disrupt the program.</summary>
    private void TryBackup(string label, bool force = false)
    {
        try { _store.CreateBackup(label, force); }
        catch (Exception ex) { Log.Write($"Backup '{label}' failed", ex); }
    }
}
