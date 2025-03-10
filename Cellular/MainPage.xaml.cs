using System;
using Microsoft.Maui.Storage;
using Cellular.ViewModel;
using Cellular.Data;

namespace Cellular
{
    public partial class MainPage : ContentPage
    {
        private bool isLoggedIn;
        private MainViewModel? viewModel;

        private BallArsenal? arsenalPage;
        private GameList? gameListPage;
        private Bluetooth? bluetoothPage;
        private AccountPage? accountPage;
        private RegisterPage? registerPage;
        private LoginPage? loginPage;

        public MainPage()
        {
            InitializeComponent();
            InitializePageAsync();
        }

        private async void InitializePageAsync()
        {
            viewModel = new MainViewModel();
            BindingContext = viewModel;

            // Check login status
            var isLoggedInValue = await SecureStorage.GetAsync("IsLoggedIn");
            isLoggedIn = isLoggedInValue == "true";
            UpdateUI();

            // Initialize other pages
            arsenalPage = new BallArsenal();
            gameListPage = new GameList();
            bluetoothPage = new Bluetooth();
            accountPage = new AccountPage();
            registerPage = new RegisterPage();
            loginPage = new LoginPage();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Refresh the username or other necessary details
            if (viewModel != null)
            {
                await viewModel.LoadUserData();
                user.Text = viewModel.FirstName;
            }
        }
        private void UpdateUI()
        {
            // Set which buttons show up when logged in or out
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
            await Navigation.PushAsync(loginPage);
        }

        private async void OnRegisterClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(registerPage);
        }


        private async void OnGuestClicked(object sender, EventArgs e)
        {
            // Set preferences for guest login
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
