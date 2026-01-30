using Camera.MAUI;
using Cellular.Data;
using Cellular.ViewModel;
using CommunityToolkit.Maui.Extensions;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

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

            // Wire up Load Stats button to populate Games/Frames/Shots according to current filters
            LoadStatsButton.Clicked += OnLoadStatsClicked;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            // Load DB-backed collections (sessions, games, balls)
            await _viewModel.LoadAsync();
        }  

        private void OnClearSessionClicked(object sender, EventArgs e)
        {
            // clear in VM (keeps state consistent)
            _viewModel.ClearSessionSelection();
        }

        // Click handler for the "Load Stats" button — calls the VM to populate Games/Frames/Shots
        private async void OnLoadStatsClicked(object sender, EventArgs e)
        {
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
                // Surface unexpected errors for debugging — consider replacing with user-friendly UI alert
                System.Diagnostics.Debug.WriteLine($"LoadStats error: {ex.Message}");
                await DisplayAlert("Error", $"Failed to load stats: {ex.Message}", "OK");
            }
        }
    }
}