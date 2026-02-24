using System;
using Microsoft.Maui.Storage;
using Cellular.ViewModel;
using Cellular.Data;
using System.Diagnostics;
using Cellular.Services;

namespace Cellular
{
    public partial class MainPage : ContentPage
    {
        private bool isLoggedIn;
        private int userId;
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

            await SoftRefreshAsync();
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
            video.IsVisible = isLoggedIn;
            account.IsVisible = isLoggedIn;
            EventList.IsVisible = isLoggedIn;
            establishment.IsVisible = isLoggedIn;
            API.IsVisible = isLoggedIn;
            BlankPage.IsVisible = isLoggedIn;
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
                Preferences.Set("UserId", user.UserId); // Store UserId properly
                Preferences.Set("IsLoggedIn", true);
                Debug.WriteLine("This is the login page " + Preferences.Get("UserId", 0));
                ((AppShell)Shell.Current).UpdateMenuForLoginStatus(true);
                await SoftRefreshAsync();
            }
        }

        private async void OnArsenalClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new BallArsenal());
        }

        private async void OnEventListClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new EventList());
        }

        private async void OnBluetoothClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new Bluetooth());
        }

        private async void OnVideoClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new Video());
        }

        private async void OnAccountClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new AccountPage());
        }

        private async void OnEstablishmentClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new EstablishmentPage());
        }

        private async void OnAPIClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new APItestPage());
        }
        private async void OnBlankPageClicked(object sender, EventArgs e)
        {
            var svc = IPlatformApplication.Current.Services.GetService<IWatchBleService>();
            await Navigation.PushAsync(new BlankPage(svc));
        }

        public async Task SoftRefreshAsync()
        {
            // Check login status
            var isLoggedInValue = Preferences.Get("IsLoggedIn", false);
            Debug.WriteLine($"Is the user logged in: {isLoggedIn}");
            isLoggedIn = isLoggedInValue == true;
            viewModel.UserID = Preferences.Get("UserId", -1);
            UpdateUI();

            if (viewModel.UserID != -1)
            {
                await viewModel.LoadUserData();
                user.Text = viewModel.FirstName;
                Debug.WriteLine("User's Hand: " + viewModel.Hand);
            }
        }

    }
}
