using Cellular.ViewModel;
using CommunityToolkit.Maui.Extensions;
using Microsoft.Maui.Controls;
using System;

namespace Cellular
{
    public partial class Stats : ContentPage
    {
        private readonly StatsViewModel _viewModel;

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
            await _viewModel.LoadAsync();
        }

        private async void OnLoadStatsClicked(object sender, EventArgs e)
        {
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
