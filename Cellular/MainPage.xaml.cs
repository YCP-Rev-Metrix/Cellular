using System;
using Microsoft.Maui.Storage;
using Cellular.ViewModel;

namespace Cellular
{
    public partial class MainPage : ContentPage
    {
        private readonly bool isLoggedIn;
        private readonly MainViewModel viewModel;

        // Readonly page instances (initialized in constructor)
        private readonly BallArsenal arsenalPage;
        private readonly GameList gameListPage;
        private readonly Bluetooth bluetoothPage;
        private readonly AccountPage accountPage;

        public MainPage()
        {
            InitializeComponent();
            isLoggedIn = SecureStorage.GetAsync("IsLoggedIn").Result == "true"; // Use .Result to avoid async in constructor
            UpdateUI();
            viewModel = new MainViewModel();
            BindingContext = viewModel;

            // Initialize readonly pages
            arsenalPage = new BallArsenal();
            gameListPage = new GameList();
            bluetoothPage = new Bluetooth();
            accountPage = new AccountPage();
        }

        private void UpdateUI() // Sets which buttons show up when logged in or out
        {
            login.IsVisible = !isLoggedIn;
            register.IsVisible = !isLoggedIn;
            guest.IsVisible = !isLoggedIn;

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

        private async void OnGuestClicked(object sender, EventArgs e)
        {
            Preferences.Set("IsLoggedIn", true);
            ((AppShell)Shell.Current).UpdateMenuForLoginStatus(true);
            Preferences.Set("UserName", "Guest"); // Stores the user's username

            await ((AppShell)Shell.Current).OnLoginSuccess();
            await Shell.Current.GoToAsync("//MainPage");
        }

        private async void OnArsenalClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(arsenalPage);
        }

        private async void OnGameListClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(gameListPage);
        }

        private async void OnBluetoothClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(bluetoothPage);
        }

        private async void OnAccountClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(accountPage);
        }
    }
}
