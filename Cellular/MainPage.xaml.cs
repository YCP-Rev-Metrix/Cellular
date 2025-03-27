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

        private BallArsenal? arsenalPage;
        private SessionList? SessionListPage;
        private Bluetooth? bluetoothPage;
        private AccountPage? accountPage;
        private RegisterPage? registerPage;
        private LoginPage? loginPage;
        private DataPage? dataPage;
        private EventPage? EventPage;
        private EstablishmentPage? EstablishmentPage;

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
            var isLoggedInValue = await SecureStorage.GetAsync("IsLoggedIn");
            isLoggedIn = isLoggedInValue == "true";
            UpdateUI();

            // Initialize other pages
            arsenalPage = new BallArsenal();
            SessionListPage = new SessionList();
            bluetoothPage = new Bluetooth();
            accountPage = new AccountPage();
            registerPage = new RegisterPage();
            loginPage = new LoginPage();
            dataPage = new DataPage();
            EventPage = new EventPage();
            EstablishmentPage = new EstablishmentPage();
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
            var user = await _userRepository.GetUserByUsernameAsync("Guest");
           
            if (user != null)
            {
                // Set preferences for guest login
                Preferences.Set("IsLoggedIn", true);
                Preferences.Set("UserId", user.UserId);
                ((AppShell)Shell.Current).UpdateMenuForLoginStatus(true);

                await ((AppShell)Shell.Current).OnLoginSuccess();
                await Shell.Current.GoToAsync("//MainPage");
            }
        }

        private async void OnArsenalClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(arsenalPage);
        }

        private async void OnSessionListClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(SessionListPage);
        }

        private async void OnBluetoothClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(bluetoothPage);
        }

        private async void OnAccountClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(accountPage);
        }

        private async void OnDataClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(dataPage);
        }

        
    }
}
