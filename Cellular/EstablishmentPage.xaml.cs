using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;
using Cellular.ViewModel;
using Cellular.Data;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
namespace Cellular;

public partial class EstablishmentPage : ContentPage
{
    private readonly EstablishmentRepository _EstablishmentRepository;
    public ObservableCollection<Establishment> Establishments { get; set; }
    private int userId;
    public EstablishmentPage()
    {
        InitializeComponent();
        _EstablishmentRepository = new EstablishmentRepository(new CellularDatabase().GetConnection());
        Establishments = new ObservableCollection<Establishment>();
        EstablishmentsList.BindingContext = this;
        LoadEstablishments();
        EstablishmentsList.ItemsSource = Establishments;
    }
    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Prevent duplicate entries by resetting the list
        EstablishmentsList.ItemsSource = null;

        // Load the event list again
        LoadEstablishments();
        EstablishmentsList.ItemsSource = Establishments;
    }
    private async void LoadEstablishments()
    {
        userId = Preferences.Get("UserId", 0);
        Debug.WriteLine("This is USer ID"+ userId);
        //Debug.WriteLine("This is strighat form the pref" + Preferences.Get("UserId", 0));
        if (userId == 0)
        {
            return;
        }
        var eventsFromDb = await _EstablishmentRepository.GetEstablishmentsByUserIdAsync(userId);
        Establishments.Clear();
        foreach (var events in eventsFromDb)
        {
            //Debug.WriteLine("This is Event name" + events.Name);
            Establishments.Add(events);
        }
    }
    // Event handler for the "+" button to navigate to the registration page
    private async void OnAddEventClicked(object sender, EventArgs e)
    {
        // Navigate to the event registration page
        await Navigation.PushAsync(new EstablishmentRegistrationPage());
    }

    // Event handler when an event in the list is selected
    private async void OnEventSelected(object sender, SelectedItemChangedEventArgs e)
    {
        if (e.SelectedItem != null)
        {
            // Get the selected event
            var selectedEvent = e.SelectedItem as Establishment;
            // Navigate to the event page (replace with your event details page)
            await DisplayAlert("Establishment Selected", $"You selected {selectedEvent.Name}", "OK");
            // Deselect the item
            EstablishmentsList.SelectedItem = null;
        }
    }
}