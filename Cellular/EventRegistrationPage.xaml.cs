using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;
using Cellular.ViewModel;
using Cellular.Data;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SQLite;

namespace Cellular;

public partial class EventRegistrationPage : ContentPage
{
    private readonly EventRepository _eventRepository;
    public ObservableCollection<Event> Events { get; set; }
    private int userId;
    public EventRegistrationPage()
    {
        InitializeComponent();
        _eventRepository = new EventRepository(new CellularDatabase().GetConnection());
        Events = new ObservableCollection<Event>();
    }

    // Event handler for the "Register Event" button
    private async void OnRegisterEventClicked(object sender, EventArgs e)
    {
        // Get the entered data
        string eventName = EventNameEntry.Text;
        string? eventType = EventTypePicker.SelectedItem?.ToString();
        string? establishment = EstablishmentPicker.SelectedItem?.ToString();
        userId = Preferences.Get("UserId", 0);

        Debug.WriteLine("This is USer ID" + userId);

        // Validation to ensure all fields are filled
        if (string.IsNullOrWhiteSpace(eventName))
        {
            await DisplayAlert("Error", "Please enter an event name.", "OK");
            return;
        }

        if (string.IsNullOrEmpty(eventType))
        {
            await DisplayAlert("Error", "Please select an event type.", "OK");
            return;
        }

        if (string.IsNullOrEmpty(establishment))
        {
            await DisplayAlert("Error", "Please select an establishment.", "OK");
            return;
        }

        // You can further process the data here (e.g., save it to a database or display a success message)

        var newEvent = new Event
        {
            UserId = userId,
            Name = eventName,
            Type = eventType,
            Location = establishment,
            Average = 0,
            Stats = 0,
            Standings = "",
        };
        var existingEvent = await _eventRepository.GetEventByNameAsync(eventName);
        Debug.WriteLine(existingEvent);
        if (existingEvent == null)
        {
            await _eventRepository.AddAsync(newEvent);
            Events.Add(newEvent);
        }
        await DisplayAlert("Event","The Event was Added","OK");
        // Optionally, clear the form
        EventNameEntry.Text = string.Empty;
        EventTypePicker.SelectedIndex = -1;
        EstablishmentPicker.SelectedIndex = -1;
    }
}
