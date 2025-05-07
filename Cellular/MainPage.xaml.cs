using System;
using Microsoft.Maui.Storage;
using Cellular.ViewModel;
using Cellular.Data;
using System.Diagnostics;

namespace Cellular
{
    public partial class MainPage : ContentPage
    {
        private bool isLoggedIn;
        private MainViewModel? viewModel;
        private UserRepository? _userRepository;

        public MainPage()
        {
            InitializeComponent();
            InitializePageAsync();
        }

        private async void InitializePageAsync()
        {
            _userRepository = new UserRepository(new CellularDatabase().GetConnection());
            viewModel = new MainViewModel();
            BindingContext = viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Check login status
            var isLoggedInValue = Preferences.Get("IsLoggedIn", false);
            Debug.WriteLine($"Is the user logged in: {isLoggedIn}");
            isLoggedIn = isLoggedInValue == true;
            UpdateUI();

            // Refresh the username or other necessary details
            if (viewModel != null)
            {
                await viewModel.LoadUserData();
                user.Text = viewModel.FirstName;
                Debug.WriteLine("User's Hand: " + viewModel.Hand);
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
            SessionList.IsVisible = isLoggedIn;
            data.IsVisible = isLoggedIn;
            API.IsVisible = isLoggedIn;

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
            var user = await _userRepository.GetUserByUsernameAsync("Guest");
           
            if (user != null)
            {
                // Set preferences for guest login
                Preferences.Set("IsLoggedIn", true);
                Preferences.Set("UserId", user.UserId);
                ((AppShell)Shell.Current).UpdateMenuForLoginStatus(true);
                await SoftRefreshAsync();
            }
        }

        private async void OnArsenalClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new BallArsenal());
        }

        private async void OnSessionListClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new SessionList());
        }

        private async void OnBluetoothClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new Bluetooth());
        }

        private async void OnAccountClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new AccountPage());
        }

        private async void OnDataClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new DataPage());
        }

        private async void OnAPIClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new APItestPage());
        }

        public async Task SoftRefreshAsync()
        {
            isLoggedIn = Preferences.Get("IsLoggedIn", false);
            UpdateUI();

            if (viewModel != null)
            {
                await viewModel.LoadUserData();
                user.Text = viewModel.FirstName;
                Debug.WriteLine("User's Hand: " + viewModel.Hand);
            }
        }

    }
}
