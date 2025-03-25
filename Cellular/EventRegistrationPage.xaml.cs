using Microsoft.Maui.Controls;

namespace Cellular;

public partial class EventRegistrationPage : ContentPage
{
    public EventRegistrationPage()
    {
        InitializeComponent();
    }

    // Event handler for the "Register Event" button
    private async void OnRegisterEventClicked(object sender, EventArgs e)
    {
        // Get the entered data
        string eventName = EventNameEntry.Text;
        string? eventType = EventTypePicker.SelectedItem?.ToString();
        string? establishment = EstablishmentPicker.SelectedItem?.ToString();

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

        // Display a confirmation message
        await DisplayAlert("Event Registered", $"Event '{eventName}' ({eventType}) registered at {establishment}.", "OK");

        // Optionally, clear the form
        EventNameEntry.Text = string.Empty;
        EventTypePicker.SelectedIndex = -1;
        EstablishmentPicker.SelectedIndex = -1;
    }
}
