using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;
using Cellular.ViewModel;
using Cellular.Data;

namespace Cellular;

public partial class EventPage : ContentPage
{
    // This will hold the list of events
    public ObservableCollection<Event> Events { get; set; } = new ObservableCollection<Event>();

    public EventPage()
    {
        InitializeComponent();
        // Add some sample events for testing
        Events.Add(new Event { Name = "League Night", Type = "League" });
        Events.Add(new Event { Name = "Tournament 2025", Type = "Tournament" });

        // Bind the ListView to the Events collection
        EventsList.ItemsSource = Events;
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