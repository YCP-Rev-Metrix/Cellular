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

        var eventRepo = new EventRepository(new CellularDatabase().GetConnection());
        var estRepo   = new EstablishmentRepository(new CellularDatabase().GetConnection());
        _viewModel = new EventPopupViewModel(eventRepo, estRepo);

        _viewModel.ShowAlert = async (title, message, cancel) =>
        {
            try
            {
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(title, message, cancel);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Alert failed: {ex.Message}");
            }
        };

        _viewModel.ClosePopup = () =>
        {
            try
            {
                Application.Current?.Windows[0]?.Page?.Dispatcher.Dispatch(() => CloseAsync());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Close failed: {ex.Message}");
            }
        };

        BindingContext = _viewModel;

        _ = _viewModel.LoadAsync();
    }
}
