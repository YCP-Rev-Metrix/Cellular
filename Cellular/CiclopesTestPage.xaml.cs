using Cellular.ViewModel;
using Cellular.Views;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;

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

            var popup = new CiclopesResultPopup(laneBallsResponse, fourDBodyTask);
            await this.ShowPopupAsync(popup, CiclopesResultPopup.CreatePopupOptions());
        }
        catch (Exception ex)
        {
            await DisplayAlert("Ciclopes Request Failed", $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}", "OK");
        }
        finally
        {
            StartTestButton.IsEnabled = true;
        }
    }
}
