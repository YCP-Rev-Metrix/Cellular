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
            //ShellItem footer = Items.Last();
            Items.Clear(); // Clear existing items
            //Items.Add(footer);

            if (!isLoggedIn)
            {
                Items.Add(new ShellContent { Content = new MainPage(), Title = "Home", Route = "MainPage" });
                Items.Add(new ShellContent { Content = new LoginPage(), Title = "Login", Route = "login" });

            }
            else
            {
                Items.Add(new ShellContent { Content = new MainPage(), Title = "Home", Route = "MainPage" });
                Items.Add(new ShellContent { Content = new BallArsenal(), Title = "Ball Arsenal", Route = "BallArsenal" });
                Items.Add(new ShellContent { Content = new AccountPage(), Title = "Account", Route = "AccountPage" });
                Items.Add(new ShellContent { Content = new ClickerPage(), Title = "Clicker", Route = "ClickerPage" });
                

            }
        }

        public void OnSignoutClicked(object sender, EventArgs e)
        {
            Preferences.Set("IsLoggedIn", false);

            // Update menu and navigate to Home after logging out
            ((AppShell)Shell.Current).UpdateMenuForLoginStatus(false);
            Shell.Current.GoToAsync("//MainPage");

        }
    }
}
