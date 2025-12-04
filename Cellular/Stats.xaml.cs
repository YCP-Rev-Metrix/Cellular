using Camera.MAUI;
using Cellular.Data;
using Cellular.ViewModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System;
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
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            // Load DB-backed collections (sessions, games, balls)
            await _viewModel.LoadAsync();
        }   
    }
}