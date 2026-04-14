using Cellular.Data;
using Cellular.Services;
using Cellular.ViewModel;
using Microsoft.Maui.Storage;
using SQLite;

namespace Cellular
{
    public partial class AccountPage : ContentPage
    {
        private readonly UserRepository _userRepository;
        private readonly MainViewModel _viewModel;

        public AccountPage()
        {
            InitializeComponent();

            // Initialize repository and use the shared ViewModel
            _userRepository = new UserRepository(new CellularDatabase().GetConnection());
            _viewModel = new MainViewModel();
            BindingContext = _viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Reload user details whenever the page appears
            await _viewModel.LoadUserData();
            RefreshSyncUi();
        }

        private void RefreshSyncUi()
        {
            _viewModel.RefreshSyncLastCheckedTime();
            lastCheckedLabel.Text = _viewModel.SyncLastCheckedTime != null ? "Last checked: " + _viewModel.SyncLastCheckedTime : "";
            syncErrorLabel.Text = _viewModel.SyncError ?? "";
            syncErrorLabel.IsVisible = !string.IsNullOrEmpty(_viewModel.SyncError);
            syncButton.IsEnabled = !_viewModel.IsSyncBusy;
        }

        private async void OnSyncClicked(object sender, EventArgs e)
        {
            int userId = Preferences.Get("UserId", -1);
            if (userId < 0)
            {
                await DisplayAlert("Sync", "Please sign in to sync.", "OK");
                return;
            }

            _viewModel.IsSyncBusy = true;
            _viewModel.SyncError = null;
            syncButton.IsEnabled = false;

            try
            {
                var syncService = new CloudSyncService();
                var result = await syncService.CheckForNewDataAsync(userId);

                if (result.Result == SyncCheckResult.Error)
                {
                    _viewModel.SyncError = result.ErrorMessage ?? "Sync failed.";
                    await DisplayAlert("Sync Error", _viewModel.SyncError, "OK");
                    RefreshSyncUi();
                    return;
                }

                if (result.Result == SyncCheckResult.HasConflict)
                {
                    // Hide blocking overlay so the system action sheet is usable on all platforms.
                    _viewModel.IsSyncBusy = false;
                    const string overwriteLocalWithCloud = "Overwrite Local with Cloud Data";
                    const string overwriteCloudWithLocal = "Overwrite Cloud with Local Data";

                    string action = await DisplayActionSheet(
                        "The Cloud User and the Local User Differ",
                        "Cancel",
                        null,
                        overwriteLocalWithCloud,
                        overwriteCloudWithLocal);

                    if (action == "Cancel")
                    {
                        RefreshSyncUi();
                        return;
                    }

                    _viewModel.IsSyncBusy = true;

                    if (action == overwriteLocalWithCloud)
                    {
                        var cloud = await syncService.FetchCloudDataAsync(syncUserId: userId);
                        if (cloud.Error != null)
                        {
                            _viewModel.SyncError = cloud.Error;
                            await DisplayAlert("Sync Error", cloud.Error, "OK");
                            RefreshSyncUi();
                            return;
                        }
                        var overwriteErr = await syncService.OverwriteLocalWithCloudAsync(
                            cloud.Balls, cloud.Establishments, cloud.Events,
                            cloud.Sessions, cloud.Games, cloud.Frames, cloud.Shots,
                            userId);
                        if (overwriteErr != null)
                        {
                            _viewModel.SyncError = overwriteErr;
                            await DisplayAlert("Sync Error", overwriteErr, "OK");
                            RefreshSyncUi();
                            return;
                        }
                        var addErr = await syncService.AddNewDataAsync(userId);
                        if (addErr != null)
                        {
                            _viewModel.SyncError = addErr;
                            await DisplayAlert("Sync Error", addErr, "OK");
                            RefreshSyncUi();
                            return;
                        }
                    }
                    else if (action == overwriteCloudWithLocal)
                    {
                        // Make cloud match local: delete cloud data, then push local.
                        var replaceErr = await syncService.ReplaceCloudWithLocalAsync(userId);
                        if (replaceErr != null)
                        {
                            _viewModel.SyncError = replaceErr;
                            await DisplayAlert("Sync Error", replaceErr, "OK");
                            RefreshSyncUi();
                            return;
                        }
                    }

                    if (action != "Cancel")
                        await DisplayAlert("Sync", "Sync completed successfully.", "OK");
                }
                else
                {
                    var addErr = await syncService.AddNewDataAsync(userId);
                    if (addErr != null)
                    {
                        _viewModel.SyncError = addErr;
                        await DisplayAlert("Sync Error", addErr, "OK");
                        RefreshSyncUi();
                        return;
                    }
                    await DisplayAlert("Sync", "Sync completed successfully.", "OK");
                }

                _viewModel.SyncError = null;
            }
            finally
            {
                _viewModel.IsSyncBusy = false;
                RefreshSyncUi();
            }
        }

        private async void OnSignoutClicked(object sender, EventArgs e)
        {
            Preferences.Set("IsLoggedIn", false);

            // Update menu and navigate to Home after logging out
            ((AppShell)Shell.Current).UpdateMenuForLoginStatus(false);
            await CloudSyncCredentialStore.ClearAsync();
            Preferences.Remove("UserId");
            Preferences.Remove("IsLoggedIn");
            Preferences.Remove(CloudSyncService.SyncLastCheckedUtcKey);

            await Shell.Current.GoToAsync("//MainPage");
        }

        private async void OnEditAccountClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new EditAccountPage(_userRepository));
        }

        private async void OnStatsClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new Stats());
        }
    }
}
