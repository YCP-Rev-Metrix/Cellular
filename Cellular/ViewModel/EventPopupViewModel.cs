using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Cellular.Data;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace Cellular.ViewModel;
public class EventPopupViewModel : INotifyPropertyChanged
{
    // fully qualify repository types to avoid ambiguity
    private readonly Cellular.Data.EventRepository _eventRepo;
    private readonly Cellular.Data.EstablishmentRepository _estRepo;

    public ObservableCollection<Establishment> Establishments { get; } = new();

    private string _eventName = string.Empty;
    public string EventName
    {
        get => _eventName;
        set { if (_eventName != value) { _eventName = value; OnPropertyChanged(); } }
    }

    private string _eventType = string.Empty;
    public string EventType
    {
        get => _eventType;
        set { if (_eventType != value) { _eventType = value; OnPropertyChanged(); } }
    }

    private Establishment? _selectedEstablishment;
    public Establishment? SelectedEstablishment
    {
        get => _selectedEstablishment;
        set { if (_selectedEstablishment != value) { _selectedEstablishment = value; OnPropertyChanged(); } }
    }

    public ICommand RegisterEventCommand { get; }

    public ICommand CancelEventCommand { get; }

    // UI callback: popup subscribes and shows the actual alert
    public Action<string, string, string>? ShowAlert { get; set; }


    // UI callback: popup subscribes and will call Close() when invoked
    public Action? ClosePopup { get; set; }


    // fully qualify ctor parameter types to ensure the compiler binds to the Data types
    public EventPopupViewModel(Cellular.Data.EventRepository eventRepo, Cellular.Data.EstablishmentRepository estRepo)
    {
        _eventRepo = eventRepo ?? throw new ArgumentNullException(nameof(eventRepo));
        _estRepo = estRepo ?? throw new ArgumentNullException(nameof(estRepo));
        RegisterEventCommand = new Command(async () => await RegisterEventAsync());
        CancelEventCommand = new Command(() =>
        {
            // Clear fields and close popup
            EventName = string.Empty;
            EventType = string.Empty;
            SelectedEstablishment = null;
            ClosePopup();
        });
    }

    public async Task LoadAsync()
    {
        var list = await _estRepo.GetEstablishmentsByUserIdAsync(Preferences.Get("UserId", 0));
        Establishments.Clear();
        foreach (var e in list) Establishments.Add(e);
    }

    private async Task RegisterEventAsync()
    {
        if (string.IsNullOrWhiteSpace(EventName))
        {
            ShowAlert?.Invoke("Error", "Please enter an event name.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(EventType))
        {
            ShowAlert?.Invoke("Error", "Please select an event type.", "OK");
            return;
        }

        if (SelectedEstablishment == null)
        {
            ShowAlert?.Invoke("Error", "Please select an establishment.", "OK");
            return;
        }

        var newEvent = new Event
        {
            UserId = Preferences.Get("UserId", 0),
            Name = EventName,
            Type = EventType,
            Location = SelectedEstablishment.Name,
            Average = 0,
            Stats = 0,
            Standings = string.Empty
        };

        var existing = await _eventRepo.GetEventByUserIdAndNameAsync(Preferences.Get("UserId", 0), EventName);
        if (existing == null)
        {
            await _eventRepo.AddAsync(newEvent);
            ShowAlert?.Invoke("Event", "The Event was Added", "OK");
            // optionally clear fields
            EventName = string.Empty;
            EventType = string.Empty;
            SelectedEstablishment = null;

            // request the view to close the popup
            ClosePopup?.Invoke();
        }
        else
        {
            ShowAlert?.Invoke("Error", "An event with that name already exists.", "OK");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}