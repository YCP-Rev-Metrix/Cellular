﻿using System;
using Microsoft.Maui.Storage;
using Cellular.ViewModel;

namespace Cellular
{
    public partial class MainPage : ContentPage
    {
        private bool isLoggedIn;
        private MainViewModel viewModel;
        public MainPage()
        {
            InitializeComponent();
            LoadLoginState();
            UpdateUI();
            viewModel = new MainViewModel();
            BindingContext = viewModel; // Set ViewModel for data binding
        }

        private async void LoadLoginState()
        {
            isLoggedIn = await SecureStorage.GetAsync("IsLoggedIn") == "true";
            UpdateUI();
        }

        private void UpdateUI()//Sets which buttons show up when loggen in or out
        {
            login.IsVisible = !isLoggedIn;
            register.IsVisible = !isLoggedIn;

            welcome.IsVisible = isLoggedIn;
            user.IsVisible = isLoggedIn;
            arsenal.IsVisible = isLoggedIn;
            bluetooth.IsVisible = isLoggedIn;
            account.IsVisible = isLoggedIn;
            gamelist.IsVisible = isLoggedIn;
        }

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new LoginPage());
        }

        private async void OnRegisterClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new RegisterPage());
        }

        private async void OnArsenalClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new BallArsenal());
        }
        private async void OnGameListClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new GameList());
        }

        private async void OnBluetoothClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new Bluetooth());
        }

        private async void OnAccountClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new AccountPage());
        }

    }
}
