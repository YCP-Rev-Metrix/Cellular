namespace Cellular
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
        }
        public void UpdateMenuForLoginStatus(bool isLoggedIn)
        {
            if (!isLoggedIn)
            {
                Items.Clear(); // Removes all items
                Items.Add(new ShellContent { Content = new LoginPage(), Title = "Login", Route = "login" });
            }
            else
            {
                // Re-add your menu items here
                Items.Clear();
                Items.Add(new ShellContent { Content = new MainPage(), Title = "Home", Route = "MainPage" });
                Items.Add(new ShellContent { Content = new BallArsenal(), Title = "Ball Arsenal", Route = "BallArselal" });
                Items.Add(new ShellContent { Content = new ClickerPage(), Title = "Clicker", Route = "ClickerPage" });
            }
        }

    }
}