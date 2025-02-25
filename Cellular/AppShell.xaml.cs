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
            var storedValue = await SecureStorage.GetAsync("IsLoggedIn");
            bool isLoggedIn = storedValue == "true";
            UpdateMenuForLoginStatus(isLoggedIn);
        }

        public void UpdateMenuForLoginStatus(bool isLoggedIn)
        {
            Items.Clear(); // Clear existing navigation items

            if (!isLoggedIn)
            {
                Items.Add(new ShellContent { Content = new MainPage(), Title = "Home", Route = "MainPage" });
                Items.Add(new ShellContent { Content = new LoginPage(), Title = "Login", Route = "login" });
                signout.IsVisible = false;
            }
            else
            {
                Items.Add(new ShellContent { Content = new MainPage(), Title = "Home", Route = "MainPage" });
                Items.Add(new ShellContent { Content = new BallArsenal(), Title = "Ball Arsenal", Route = "BallArsenal" });
                Items.Add(new ShellContent { Content = new Bluetooth(), Title = "Bluetooth", Route = "Bluetooth" });
                Items.Add(new ShellContent { Content = new AccountPage(), Title = "Account", Route = "AccountPage" });
                Items.Add(new ShellContent { Content = new ClickerPage(), Title = "Clicker", Route = "ClickerPage" });
                signout.IsVisible = true;
            }
        }

        public async void OnSignoutClicked(object sender, EventArgs e)
        {
            await SecureStorage.SetAsync("IsLoggedIn", "false"); // Ensure login state is updated correctly
            UpdateMenuForLoginStatus(false);
            await Shell.Current.GoToAsync("//MainPage");
        }

        public async Task OnLoginSuccess()
        {
            await SecureStorage.SetAsync("IsLoggedIn", "true");
            UpdateMenuForLoginStatus(true);
            await Shell.Current.GoToAsync("//MainPage");
        }
    }
}
