using System;
using Microsoft.Maui.Storage;
using System.Threading.Tasks;
using Cellular.ViewModel;

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
                ballArsenal.IsVisible = false;
                sessionList.IsVisible = false;
                bluetooth.IsVisible = false;
                dataPage.IsVisible = false;
                databaseVisualizer.IsVisible = false;
                apiPage.IsVisible = false;
                accountPage.IsVisible = false;
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
                databaseVisualizer.IsVisible = true;
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
            await Task.Delay(100);
            Preferences.Default.Clear();

            if (Shell.Current.CurrentPage is MainPage mainPage)
            {
                await mainPage.SoftRefreshAsync();
            }
        }
    }
}
