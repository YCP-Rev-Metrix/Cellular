using Cellular.ViewModel;
using Cellular.Views;
using CommunityToolkit.Maui.Extensions;

namespace Cellular;

public partial class CiclopesTestPage : ContentPage
{
    private readonly CiclopesTestViewModel _viewModel;

    public CiclopesTestPage()
    {
        InitializeComponent();
        _viewModel = new CiclopesTestViewModel();
        BindingContext = _viewModel;
    }

    private async void OnStartTestClicked(object sender, EventArgs e)
    {
        StartTestButton.IsEnabled = false;

        try
        {
            var response = await _viewModel.RunTestAsync();

            if (response is null)
            {
                await DisplayAlert("Ciclopes", "No response data returned.", "OK");
                return;
            }

            this.ShowPopup(new CiclopesResultPopup(response));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Ciclopes Request Failed", ex.Message, "OK");
        }
        finally
        {
            StartTestButton.IsEnabled = true;
        }
    }
}
