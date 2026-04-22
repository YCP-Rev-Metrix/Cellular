using Cellular.ViewModel;
using Cellular.Views;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;

namespace Cellular;

public partial class CiclopesTestPage : ContentPage
{
    private readonly CiclopesTestViewModel _viewModel;
    private readonly List<int> _selectedShots = [];

    public CiclopesTestPage()
    {
        InitializeComponent();
        _viewModel = new CiclopesTestViewModel();
        BindingContext = _viewModel;
    }

    private async void OnStartTestClicked(object sender, EventArgs e)
    {
        StartTestButton.IsEnabled = false;
        LoadingOverlay.IsVisible = true;

        try
        {
            var (laneBallsTask, fourDBodyTask) = _viewModel.RunTestAsync();

            var laneBallsResponse = await laneBallsTask;

            if (laneBallsResponse is null)
            {
                await DisplayAlertAsync("Ciclopes", "No lane/balls data returned.", "OK");
                return;
            }

            var popup = new CiclopesResultPopup(laneBallsResponse, fourDBodyTask);
            await this.ShowPopupAsync(popup, CiclopesResultPopup.CreatePopupOptions());
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Ciclopes Request Failed", $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}", "OK");
        }
        finally
        {
            LoadingOverlay.IsVisible = false;
            StartTestButton.IsEnabled = true;
        }
    }

    private void OnQueryTestClicked(object sender, EventArgs e)
    {
        ShotDropdown.IsVisible = !ShotDropdown.IsVisible;
    }

    private void OnShotCheckChanged(object sender, CheckedChangedEventArgs e)
    {
        _selectedShots.Clear();
        if (Shot1Check.IsChecked) _selectedShots.Add(1);
        if (Shot2Check.IsChecked) _selectedShots.Add(2);
        if (Shot3Check.IsChecked) _selectedShots.Add(3);

        SelectedShotsLabel.Text = _selectedShots.Count == 0
            ? "Selected: none"
            : $"Selected: {string.Join(", ", _selectedShots)}";
        RunQueryButton.IsEnabled = _selectedShots.Count > 0;
    }

    private async void OnRunQueryClicked(object sender, EventArgs e)
    {
        if (_selectedShots.Count == 0) return;

        QueryTestButton.IsEnabled = false;
        RunQueryButton.IsEnabled = false;
        LoadingOverlay.IsVisible = true;

        try
        {
            var (laneTask, poseTask) = _viewModel.QueryTestAsync([.. _selectedShots]);
            var laneResponse = await laneTask;

            if (laneResponse is null || laneResponse.Shots.Count == 0)
            {
                await DisplayAlertAsync("Ciclopes", "No shots returned.", "OK");
                return;
            }

            var popup = new CiclopesResultPopup(laneResponse, poseTask);
            await this.ShowPopupAsync(popup, CiclopesResultPopup.CreatePopupOptions());
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Query Failed", $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}", "OK");
        }
        finally
        {
            LoadingOverlay.IsVisible = false;
            QueryTestButton.IsEnabled = true;
            RunQueryButton.IsEnabled = _selectedShots.Count > 0;
        }
    }

    private async void OnExperimentalVideoClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(CiclopesExperimentalVideoPage));
    }
}
