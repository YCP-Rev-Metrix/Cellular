using Cellular.Cloud_API;
using Cellular.ViewModel;
using Cellular.Views;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;

namespace Cellular;

public partial class CiclopesTestPage : ContentPage
{
    private readonly CiclopesTestViewModel _viewModel;
    private readonly List<string> _availableQueryNames = [];
    private readonly HashSet<string> _selectedQueryNames = new(StringComparer.Ordinal);
    private bool _queryNamesLoaded;
    private bool _queryNamesLoading;

    public CiclopesTestPage()
    {
        InitializeComponent();
        _viewModel = new CiclopesTestViewModel();
        BindingContext = _viewModel;
        UpdateApiUrlLabel();
        BuildQueryNameOptions();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_queryNamesLoaded && !_queryNamesLoading)
        {
            await LoadQueryNamesAsync(showOverlay: false);
        }
    }

    private void UpdateApiUrlLabel()
    {
        CurrentApiUrlLabel.Text = $"Current: {CiclopesSettings.ApiBase ?? "(not set)"}";
    }

    private async void OnUpdateApiUrlClicked(object sender, EventArgs e)
    {
        var result = await DisplayPromptAsync(
            "Update Ciclopes-API URL",
            "Enter new base URL (leave blank to reset to settings.json default):",
            accept: "Save",
            cancel: "Cancel",
            placeholder: "https://example.trycloudflare.com",
            initialValue: CiclopesSettings.ApiBase ?? string.Empty);

        if (result is null) return;

        CiclopesSettings.SetApiBaseOverride(result);
        UpdateApiUrlLabel();
        ResetQueryNames();
        await DisplayAlertAsync("Ciclopes", $"API URL set to:\n{CiclopesSettings.ApiBase ?? "(default)"}", "OK");
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

    private async void OnQueryTestClicked(object sender, EventArgs e)
    {
        if (!_queryNamesLoaded && !_queryNamesLoading)
        {
            await LoadQueryNamesAsync(showOverlay: true);
        }

        ShotDropdown.IsVisible = !ShotDropdown.IsVisible;
    }

    private async void OnRefreshQueryNamesClicked(object sender, EventArgs e)
    {
        await LoadQueryNamesAsync(showOverlay: true);
    }

    private void ResetQueryNames()
    {
        _availableQueryNames.Clear();
        _selectedQueryNames.Clear();
        _queryNamesLoaded = false;
        BuildQueryNameOptions();
    }

    private async Task LoadQueryNamesAsync(bool showOverlay)
    {
        _queryNamesLoading = true;
        QueryTestButton.IsEnabled = false;
        RefreshQueryNamesButton.IsEnabled = false;
        if (showOverlay)
        {
            LoadingOverlay.IsVisible = true;
        }

        BuildQueryNameOptions("Loading saved runs...");

        try
        {
            var response = await _viewModel.GetQueryNamesAsync();
            List<string> names = response?.Names is null
                ? new List<string>()
                : response.Names
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            _availableQueryNames.Clear();
            _availableQueryNames.AddRange(names);

            _selectedQueryNames.RemoveWhere(n => !_availableQueryNames.Contains(n, StringComparer.Ordinal));
            _queryNamesLoaded = true;
            BuildQueryNameOptions();
        }
        catch (Exception ex)
        {
            _queryNamesLoaded = false;
            BuildQueryNameOptions($"Could not load saved runs: {ex.Message}");
        }
        finally
        {
            _queryNamesLoading = false;
            QueryTestButton.IsEnabled = true;
            RefreshQueryNamesButton.IsEnabled = true;
            if (showOverlay)
            {
                LoadingOverlay.IsVisible = false;
            }
        }
    }

    private void BuildQueryNameOptions(string? statusText = null)
    {
        QueryNameList.Children.Clear();

        if (!string.IsNullOrWhiteSpace(statusText))
        {
            QueryNameList.Children.Add(CreateQueryNameStatusLabel(statusText));
            UpdateSelectedNamesLabel();
            return;
        }

        if (_availableQueryNames.Count == 0)
        {
            QueryNameList.Children.Add(CreateQueryNameStatusLabel(
                _queryNamesLoaded ? "No saved runs found." : "Saved runs have not loaded yet."));
            UpdateSelectedNamesLabel();
            return;
        }

        foreach (var name in _availableQueryNames)
        {
            var row = new HorizontalStackLayout { Spacing = 8 };
            var checkBox = new CheckBox
            {
                IsChecked = _selectedQueryNames.Contains(name),
                Color = (Color)Application.Current!.Resources["Secondary"]
            };
            var label = new Label
            {
                Text = name,
                VerticalTextAlignment = TextAlignment.Center,
                TextColor = Application.Current!.RequestedTheme == AppTheme.Dark
                    ? Colors.White
                    : (Color)Application.Current.Resources["Gray950"]
            };

            var capturedName = name;
            checkBox.CheckedChanged += (_, args) =>
            {
                if (args.Value)
                {
                    _selectedQueryNames.Add(capturedName);
                }
                else
                {
                    _selectedQueryNames.Remove(capturedName);
                }

                UpdateSelectedNamesLabel();
            };

            row.Children.Add(checkBox);
            row.Children.Add(label);
            QueryNameList.Children.Add(row);
        }

        UpdateSelectedNamesLabel();
    }

    private static Label CreateQueryNameStatusLabel(string text)
    {
        return new Label
        {
            Text = text,
            FontSize = 12,
            TextColor = (Color)Application.Current!.Resources["Gray400"]
        };
    }

    private void UpdateSelectedNamesLabel()
    {
        SelectedNamesLabel.Text = _selectedQueryNames.Count == 0
            ? "Selected: none"
            : $"Selected: {string.Join(", ", _selectedQueryNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}";
        RunQueryButton.IsEnabled = _selectedQueryNames.Count > 0 && !_queryNamesLoading;
    }

    private async void OnRunQueryClicked(object sender, EventArgs e)
    {
        if (_selectedQueryNames.Count == 0) return;

        QueryTestButton.IsEnabled = false;
        RunQueryButton.IsEnabled = false;
        LoadingOverlay.IsVisible = true;

        try
        {
            var selectedNames = _selectedQueryNames
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var (laneTask, poseTask) = _viewModel.QueryTestAsync(selectedNames);
            var laneResponse = await laneTask;

            if (laneResponse is null || laneResponse.Shots.Count == 0)
            {
                await DisplayAlertAsync("Ciclopes", "No runs returned.", "OK");
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
            RunQueryButton.IsEnabled = _selectedQueryNames.Count > 0;
        }
    }

    private async void OnExperimentalVideoClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(CiclopesExperimentalVideoPage));
    }
}
