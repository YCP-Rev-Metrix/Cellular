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

            // Check login status
            var isLoggedInValue = Preferences.Get("IsLoggedIn", false);
            isLoggedIn = isLoggedInValue == true;
            UpdateUI();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

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
            await Shell.Current.GoToAsync("//LoginPage");
        }

        private async void OnRegisterClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//registerPage");
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

                var mainViewModel = new MainViewModel();
                mainViewModel.UserID = user.UserId; // Set UserId instead of UserName

                await ((AppShell)Shell.Current).OnLoginSuccess();
                await Shell.Current.GoToAsync("//MainPage");
            }
        }

        private async void OnArsenalClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//ballArsenal");
        }

        private async void OnSessionListClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//sessionList");
        }

        private async void OnBluetoothClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//bluetooth");
        }

        private async void OnAccountClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//account");
        }

        private async void OnDataClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//dataPage");
        }

        private async void OnAPIClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//apiPage");
        }


    }
}
