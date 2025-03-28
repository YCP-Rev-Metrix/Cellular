using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;
using Cellular.ViewModel;
using Cellular.Data;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
namespace Cellular;

public partial class EventPage : ContentPage
{
    private readonly EventRepository _eventRepository;
    public ObservableCollection<Event> Events { get; set; }
    private int userId;
    public EventPage()
    {
        InitializeComponent();
        _eventRepository = new EventRepository(new CellularDatabase().GetConnection());
        Events = new ObservableCollection<Event>();
        EventsList.BindingContext = this;
        LoadEvents();
        EventsList.ItemsSource = Events;
    }
    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Prevent duplicate entries by resetting the list
        EventsList.ItemsSource = null;

        // Load the event list again
        LoadEvents();
        EventsList.ItemsSource = Events; // Ensure GetEvents() is returning fresh data
    }
    private async void LoadEvents()
    {
        userId = Preferences.Get("UserId", 0);
        Debug.WriteLine("This is USer ID"+ userId);
        //Debug.WriteLine("This is strighat form the pref" + Preferences.Get("UserId", 0));
        if (userId == 0)
        {
            return;
        }
        var eventsFromDb = await _eventRepository.GetEventsByUserIdAsync(userId);
        Events.Clear();
        foreach (var events in eventsFromDb)
        {
            Debug.WriteLine("This is Event name" + events.Name);
            Events.Add(events);
        }
    }
    // Event handler for the "+" button to navigate to the registration page
    private async void OnAddEventClicked(object sender, EventArgs e)
    {
        // Navigate to the event registration page
        await Navigation.PushAsync(new EventRegistrationPage());
    }

    // Event handler when an event in the list is selected
    private async void OnEventSelected(object sender, SelectedItemChangedEventArgs e)
    {
        if (e.SelectedItem != null)
        {
            // Get the selected event
            var selectedEvent = e.SelectedItem as Event;
            // Navigate to the event page (replace with your event details page)
            await DisplayAlert("Event Selected", $"You selected {selectedEvent.Name}", "OK");
            // Deselect the item
            EventsList.SelectedItem = null;
        }
    }
}