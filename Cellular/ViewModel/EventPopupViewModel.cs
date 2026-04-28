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
    private readonly Cellular.Data.EventRepository _eventRepo;
    private readonly Cellular.Data.EstablishmentRepository _estRepo;

    // ── Collections ───────────────────────────────────────────────────────

    public ObservableCollection<Establishment> Establishments { get; } = new();

    // ── Fields that match EventEditorPopup ───────────────────────────────

    private string _eventName = string.Empty;
    public string EventName
    {
        get => _eventName;
        set { if (_eventName != value) { _eventName = value; OnPropertyChanged(); } }
    }

    private string _nickName = string.Empty;
    public string NickName
    {
        get => _nickName;
        set { if (_nickName != value) { _nickName = value; OnPropertyChanged(); } }
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

    private string _weekDay = string.Empty;
    public string WeekDay
    {
        get => _weekDay;
        set { if (_weekDay != value) { _weekDay = value; OnPropertyChanged(); } }
    }

    // TimeSpan binds directly to TimePicker.Time
    private TimeSpan _startTime = new TimeSpan(18, 0, 0); // default 6:00 PM
    public TimeSpan StartTime
    {
        get => _startTime;
        set { if (_startTime != value) { _startTime = value; OnPropertyChanged(); } }
    }

    private int _numGamesPerSession = 3;
    public int NumGamesPerSession
    {
        get => _numGamesPerSession;
        set { if (_numGamesPerSession != value) { _numGamesPerSession = value; OnPropertyChanged(); } }
    }

    private bool _enabled = true;
    public bool Enabled
    {
        get => _enabled;
        set { if (_enabled != value) { _enabled = value; OnPropertyChanged(); } }
    }

    // ── Commands ──────────────────────────────────────────────────────────

    public ICommand RegisterEventCommand { get; }
    public ICommand CancelEventCommand { get; }

    // UI callbacks — popup code-behind subscribes to these
    public Action<string, string, string>? ShowAlert { get; set; }
    public Action? ClosePopup { get; set; }

    public EventPopupViewModel(Cellular.Data.EventRepository eventRepo,
                               Cellular.Data.EstablishmentRepository estRepo)
    {
        _eventRepo = eventRepo ?? throw new ArgumentNullException(nameof(eventRepo));
        _estRepo   = estRepo   ?? throw new ArgumentNullException(nameof(estRepo));

        RegisterEventCommand = new Command(async () => await RegisterEventAsync());
        CancelEventCommand = new Command(() =>
        {
            ClearFields();
            ClosePopup?.Invoke();
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
            ShowAlert?.Invoke("Error", "Please select a location.", "OK");
            return;
        }

        int userId = Preferences.Get("UserId", 0);

        var existing = await _eventRepo.GetEventByUserIdAndNameAsync(userId, EventName);
        if (existing != null)
        {
            ShowAlert?.Invoke("Error", "An event with that name already exists.", "OK");
            return;
        }

        var newEvent = new Event
        {
            UserId     = userId,
            LongName   = EventName,
            NickName   = string.IsNullOrWhiteSpace(NickName) ? EventName : NickName,
            Type       = EventType,
            Location   = SelectedEstablishment.NickName,
            WeekDay    = WeekDay,
            StartTime  = DateTime.Today.Add(StartTime).ToString("h:mm tt"),
            NumGamesPerSession = NumGamesPerSession,
            Average    = 0,
            Stats      = 0,
            Standings  = string.Empty,
            Enabled    = Enabled
        };

        await _eventRepo.AddAsync(newEvent);
        ShowAlert?.Invoke("Event", "Event added.", "OK");
        ClearFields();
        ClosePopup?.Invoke();
    }

    private void ClearFields()
    {
        EventName             = string.Empty;
        NickName              = string.Empty;
        EventType             = string.Empty;
        SelectedEstablishment = null;
        WeekDay               = string.Empty;
        StartTime             = new TimeSpan(18, 0, 0);
        NumGamesPerSession    = 3;
        Enabled               = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
