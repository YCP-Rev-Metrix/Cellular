using System;
using Microsoft.Maui.Storage;

namespace Cellular
{
    public partial class MainPage : ContentPage
    {
        private bool isLoggedIn;
        public MainPage()
        {
            InitializeComponent();
            LoadLoginState();
            UpdateUI();
        }

        private void LoadLoginState()
        {
            isLoggedIn = SecureStorage.GetAsync("IsLoggedIn").Result == "true";
            Console.WriteLine(isLoggedIn);
            UpdateUI();
        }

        private void UpdateUI()
        {
            login.IsVisible = !isLoggedIn;
            register.IsVisible = !isLoggedIn;

            arsenal.IsVisible = isLoggedIn;
            bluetooth.IsVisible = isLoggedIn;
            account.IsVisible = isLoggedIn;
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
