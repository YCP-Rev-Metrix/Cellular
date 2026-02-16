using Cellular.Data;
using Cellular.ViewModel;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls;
using System.Diagnostics;

namespace Cellular.Views;
public partial class EventPopup : Popup
{
    private readonly EventPopupViewModel _viewModel;

    public EventPopup()
    {
        InitializeComponent();

        // create repositories and VM (match how your app creates DB connections)
        var eventRepo = new EventRepository(new CellularDatabase().GetConnection());
        var estRepo = new EstablishmentRepository(new CellularDatabase().GetConnection());

        _viewModel = new EventPopupViewModel(eventRepo, estRepo);

        // hook up alert display (UI responsibility)
        _viewModel.ShowAlert = async (title, message, cancel) =>
        {
            try
            {
                await Application.Current?.MainPage?.DisplayAlert(title, message, cancel);
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"Alert failed: {ex.Message}");
            }
        };

        // VM will invoke this to request the popup close.
        _viewModel.ClosePopup = () =>
        {
            try
            {
                // Ensure Close runs on the UI thread
                Application.Current?.MainPage?.Dispatcher.Dispatch(() => CloseAsync());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Close failed: {ex.Message}");
            }
        };

        BindingContext = _viewModel;

        // fire-and-forget load
        _ = _viewModel.LoadAsync();
    }
}