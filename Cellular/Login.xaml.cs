using Cellular.Data;
using Cellular.ViewModel;
using Microsoft.Maui.Storage;

namespace Cellular
{
    public partial class LoginPage : ContentPage
    {
        private readonly UserRepository _userRepository;

        // Constructor where we initialize the UserRepository
        public LoginPage()
        {
            InitializeComponent();
            _userRepository = new UserRepository(new CellularDatabase().GetConnection());
        }

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            string username = entryUsername.Text;
            string password = entryPassword.Text;

            // Query the database to validate the user's credentials
            var user = await _userRepository.GetUserByCredentialsAsync(username, password);

            if (user != null)
            {
                Preferences.Set("IsLoggedIn", true);

                // Update menu and navigate to Home
                ((AppShell)Shell.Current).UpdateMenuForLoginStatus(true);

                Preferences.Set("UserName", username); // Store the user's username

                await ((AppShell)Shell.Current).OnLoginSuccess();

                await Shell.Current.GoToAsync("//MainPage");
            }
            else
            {
                await DisplayAlert("Login Failed", "Invalid username or password", "OK");
            }
        }

        private void OnRegisterTapped(object sender, EventArgs e)
        {
            Navigation.PushAsync(new RegisterPage());
        }
    }
}
