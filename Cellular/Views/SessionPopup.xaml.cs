using Cellular.ViewModel;
using CommunityToolkit.Maui.Views;
using System.ComponentModel;
using System.Diagnostics;

namespace Cellular.Views;

public partial class SessionPopup : Popup, INotifyPropertyChanged
{
    private readonly SessionListViewModel _viewModel;
    public SessionPopup()
	{
		InitializeComponent();
        _viewModel = new SessionListViewModel();
    }

	public async void OnCreateSessionClicked(object sender, EventArgs e)
	{
		await _viewModel.AddSession();
        try
        {
            // Ensure Close runs on the UI thread
            Application.Current?.MainPage?.Dispatcher.Dispatch(() => CloseAsync());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Close failed: {ex.Message}");
        }
    }
}