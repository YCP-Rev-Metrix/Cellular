using Cellular.Cloud_API.Endpoints;
using Cellular.ViewModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System;
using System.Diagnostics;

namespace Cellular
{
    public partial class BallArsenal : ContentPage
    {
        private readonly BallArsenalViewModel _viewModel;

        public BallArsenal()
        {
            InitializeComponent();

            _viewModel = new BallArsenalViewModel();
            BindingContext = _viewModel;

            BallsListView.BindingContext = _viewModel;
            BallsListView.ItemsSource = _viewModel.Balls;

            // forward UI selection into the VM (if XAML doesn't bind SelectedItem)
            BallsListView.SelectionChanged += OnBallsListViewSelectionChanged;

            // react when the VM reports SelectedIndex change
            _viewModel.SelectedIndexChanged += OnViewModelSelectedIndexChanged;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // refresh via VM
            await _viewModel.LoadBallsAsync();

            // Ensure the ItemsSource is set (defensive)
            BallsListView.ItemsSource = _viewModel.Balls;
        }

        async private void OnAddBallBtnClicked(object sender, EventArgs e)
        {
            // Navigation stays in the view
            await Navigation.PushAsync(new BallArsenalRegistrationPage());
        }

        // forward selected item from the ListView to the VM
        private void OnBallsListViewSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection != null && e.CurrentSelection.Count > 0 && e.CurrentSelection[0] is Ball selected)
            {
                _viewModel.SelectedBall = selected;
            }
            else
            {
                _viewModel.SelectedBall = null;
            }
        }

        // react to VM selection changes
        private void OnViewModelSelectedIndexChanged(object? sender, EventArgs e)
        {
            
        }
    }
}
