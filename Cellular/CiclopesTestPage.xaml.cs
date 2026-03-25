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
            var (laneBallsTask, fourDBodyTask) = _viewModel.RunTestAsync();

            var laneBallsResponse = await laneBallsTask;

            if (laneBallsResponse is null)
            {
                await DisplayAlert("Ciclopes", "No lane/balls data returned.", "OK");
                return;
            }

            this.ShowPopup(new CiclopesResultPopup(laneBallsResponse, fourDBodyTask));
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
