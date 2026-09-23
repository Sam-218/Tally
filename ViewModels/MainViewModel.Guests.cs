using System.Windows.Input;
using Tally.Models;
using Tally.Services;

namespace Tally.ViewModels;

/// <summary>Guest list of the selected party (who owes what, who has paid) and the tab switch.</summary>
public sealed partial class MainViewModel
{
    private string _activeTab = "drinks";
    private string _newGuestName = "";
    private string _newGuestOwes = "";

    // ------------------------------------------------------------------ Tabs

    /// <summary>"drinks" or "guests" - which half of the main area is shown.</summary>
    public string ActiveTab
    {
        get => _activeTab;
        private set
        {
            if (!Set(ref _activeTab, value)) return;
            OnPropertyChanged(nameof(IsDrinksTab));
            OnPropertyChanged(nameof(IsGuestsTab));
        }
    }

    public bool IsDrinksTab => _activeTab != "guests";
    public bool IsGuestsTab => _activeTab == "guests";

    private ICommand? _setTab;
    public ICommand SetTabCommand => _setTab ??= new RelayCommand(t => ActiveTab = t as string ?? "drinks");

    /// <summary>Back to the drinks tab - used when another party is selected.</summary>
    private void ResetTab() => ActiveTab = "drinks";

    // ------------------------------------------------------------------ New guest

    public string NewGuestName { get => _newGuestName; set => Set(ref _newGuestName, value ?? ""); }
    public string NewGuestOwes { get => _newGuestOwes; set => Set(ref _newGuestOwes, value ?? ""); }

    private void ClearGuestForm()
    {
        NewGuestName = "";
        NewGuestOwes = "";
    }

    private ICommand? _addGuest;
    public ICommand AddGuestCommand => _addGuest ??= new RelayCommand(_ => AddGuest());

    private void AddGuest()
    {
        var party = _selectedParty;
        if (party == null) return;

        var name = NewGuestName.Trim();
        if (name.Length == 0) { RequestFocus("NewGuestName"); return; }

        // The amount is optional here - it can just as well be typed into the row later.
        var owes = Fmt.ParseOrZero(NewGuestOwes);
        if (owes < 0) owes = 0;

        party.Guests.Add(new Guest { Name = name, Owes = owes });

        ClearGuestForm();
        RequestFocus("NewGuestName");
    }

    // ------------------------------------------------------------------ Row actions

    /// <summary>Marks a guest as fully paid. An explicit click - nothing is calculated on its own.</summary>
    private ICommand? _settleGuest;
    public ICommand SettleGuestCommand => _settleGuest ??= new RelayCommand(g =>
    {
        if (g is Guest guest) guest.Paid = guest.Owes;
    });

    private ICommand? _deleteGuest;
    public ICommand DeleteGuestCommand => _deleteGuest ??= new RelayCommand(g => DeleteGuest(g as Guest));

    private void DeleteGuest(Guest? guest)
    {
        if (guest == null || _selectedParty == null) return;

        // A guest row holds money someone still owes - ask first, like deleting a party.
        var name = string.IsNullOrWhiteSpace(guest.Name) ? "?" : guest.Name;
        if (!_ui.Confirm(LocalizationManager.T("S_DeleteGuestTitle"),
                string.Format(LocalizationManager.T("S_DeleteGuestMsg"), name),
                LocalizationManager.T("S_Delete"), LocalizationManager.T("S_Cancel"), danger: true)) return;

        _selectedParty.Guests.Remove(guest);
    }
}
