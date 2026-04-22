using Cellular.ViewModel;
using CommunityToolkit.Maui.Extensions;
using Microsoft.Maui.Controls;
using System;

namespace Cellular
{
    public partial class Stats : ContentPage
    {
        private readonly StatsViewModel _viewModel;
        private bool _hasLoaded = false;

        public Stats()
        {
            InitializeComponent();

            _viewModel = new StatsViewModel();
            BindingContext = _viewModel;

            LoadStatsButton.Clicked += OnLoadStatsClicked;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Only run the initial data load once per page instance.
            // Subsequent appearances (e.g. popup dismissed) must not clear
            // the collections — that would wipe all the user's selections.
            if (!_hasLoaded)
            {
                _hasLoaded = true;
                await _viewModel.LoadAsync();
            }
        }

        private async void OnLoadStatsClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_viewModel.SelectedStatType))
            {
                await DisplayAlertAsync("Stat Type Required", "Please select a Stat Type before loading.", "OK");
                return;
            }

            _viewModel.IsLoading = true;
            try
            {
                await _viewModel.LoadFilteredDataAsync();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var popup = new Cellular.Views.StatsPopup(_viewModel);
                    this.ShowPopup(popup);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadStats error: {ex.Message}");
                await DisplayAlertAsync("Error", $"Failed to load stats: {ex.Message}", "OK");
            }
            finally
            {
                _viewModel.IsLoading = false;
            }
        }
    }
}
