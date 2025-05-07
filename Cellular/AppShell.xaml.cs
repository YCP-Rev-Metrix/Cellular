using System;
using Microsoft.Maui.Storage;
using System.Threading.Tasks;

namespace Cellular
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            LoadLoginState(); // Load login state when the app starts
        }

        private async void LoadLoginState()
        {
            var storedValue = Preferences.Get("IsLoggedIn", false);
            bool isLoggedIn = storedValue == true;
            UpdateMenuForLoginStatus(isLoggedIn);
        }

        public void UpdateMenuForLoginStatus(bool isLoggedIn)
        {
            if (!isLoggedIn)
            {
                login.IsVisible = true;
                register.IsVisible = true;
                signout.IsVisible = false;
            }
            else
            {
                login.IsVisible = false;
                register.IsVisible = false;
                ballArsenal.IsVisible = true;
                sessionList.IsVisible = true;
                bluetooth.IsVisible = true;
                dataPage.IsVisible = true;
                apiPage.IsVisible = true;
                accountPage.IsVisible = true;
                signout.IsVisible = true;
            }
        }

        public async void OnSignoutClicked(object sender, EventArgs e)
        {
            Preferences.Set("IsLoggedIn", false); // Ensure login state is updated correctly
            UpdateMenuForLoginStatus(false);
            await Shell.Current.GoToAsync("//MainPage");
        }

        public async Task OnLoginSuccess()
        {
            Preferences.Set("IsLoggedIn", true);
            UpdateMenuForLoginStatus(true);
            await Shell.Current.GoToAsync("//MainPage");
        }
    }
}
