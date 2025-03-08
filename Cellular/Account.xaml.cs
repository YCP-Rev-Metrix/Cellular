using Cellular.ViewModel;

namespace Cellular
{
    public partial class AccountPage : ContentPage
    {
        public AccountPage()
        {
            InitializeComponent();
            BindingContext = new MainViewModel();
        }

        private async void OnSignoutClicked(object sender, EventArgs e)
        {
            await SecureStorage.SetAsync("IsLoggedIn", "false");

            // Update menu and navigate to Home after logging out
            ((AppShell)Shell.Current).UpdateMenuForLoginStatus(false);

             await Shell.Current.GoToAsync("//MainPage");
        }

        private void OnStatsClicked(object sender, EventArgs e)
        {
            // Implement when stats are figured out
        }
    }
}

