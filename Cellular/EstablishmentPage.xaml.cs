using Microsoft.Maui.Controls;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Maui.Extensions;
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
        EstablishmentsList.ItemsSource = Establishments;
        LoadEstablishments();
    }
    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadEstablishments();
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
    // Change this signature
    private async void OnEventSelected(object sender, SelectionChangedEventArgs e)
    {
        // CollectionView uses e.CurrentSelection (a list)
        var selectedEvent = e.CurrentSelection.FirstOrDefault() as Establishment;

        if (selectedEvent != null)
        {
            try
            {
                var popup = new Views.EstablishmentEditorPopup(selectedEvent);

                popup.Closed += (s, args) =>
                {
                    if (!popup.Completion.Task.IsCompleted)
                    {
                        popup.Completion.TrySetResult(null);
                    }
                };

                this.ShowPopup(popup);

                var result = await popup.Completion.Task;
                if (result != null)
                {
                    await _EstablishmentRepository.UpdateAsync(result);

                    LoadEstablishments();

                    var idx = Establishments.IndexOf(selectedEvent);
                    if (idx >= 0)
                    {
                        Establishments[idx] = result;
                    }

                    await DisplayAlertAsync("Establishment", "Establishment saved.", "OK");
                }
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"Error showing editor: {ex.Message}");
            }

            // Deselect the item so it can be clicked again
            ((CollectionView)sender).SelectedItem = null;
        }
    }
}