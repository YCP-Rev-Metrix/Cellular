using Microsoft.Maui.Storage; // Required for Preferences

namespace Cellular
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            // Retrieve login status
            bool isLoggedIn = SecureStorage.GetAsync("IsLoggedIn").Result == "true";
            UpdateMenuForLoginStatus(isLoggedIn);
        }

        public void UpdateMenuForLoginStatus(bool isLoggedIn)
        {
            Items.Clear(); // Clear existing items

            if (!isLoggedIn)
            {
                Items.Add(new ShellContent { Content = new MainPage(), Title = "Home", Route = "MainPage" });
                Items.Add(new ShellContent { Content = new LoginPage(), Title = "Login", Route = "login" });
            }
            else
            {
                Items.Add(new ShellContent { Content = new MainPage(), Title = "Home", Route = "MainPage" });
                Items.Add(new ShellContent { Content = new BallArsenal(), Title = "Ball Arsenal", Route = "BallArsenal" });
                Items.Add(new ShellContent { Content = new Bluetooth(), Title = "Bluetooth", Route = "Bluetooth" });
                Items.Add(new ShellContent { Content = new ClickerPage(), Title = "Clicker", Route = "ClickerPage" });
            }
        }
    }
}
