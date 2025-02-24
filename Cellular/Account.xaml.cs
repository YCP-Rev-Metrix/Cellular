using Microsoft.Maui.Storage;

namespace Cellular
{
    public partial class AccountPage : ContentPage
    {
        public AccountPage()
        {
            InitializeComponent();
        }

        private void OnSignoutClicked(object sender, EventArgs e)
        {
             Preferences.Set("IsLoggedIn", false);

             // Update menu and navigate to Home after logging out
             ((AppShell)Shell.Current).UpdateMenuForLoginStatus(false);
             Shell.Current.GoToAsync("//MainPage");
        }

        private void OnStatsClicked(object sender, EventArgs e)
        {
            // Implement when stats are figured out
        }
    }
}

