using System.Diagnostics;
using Cellular.Data;
using Cellular.Services;
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
            string username = entryUsername.Text?.Trim() ?? string.Empty;
            string password = entryPassword.Text ?? string.Empty;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                await DisplayAlert("Login Failed", "Please enter a username and password.", "OK");
                return;
            }

            var user = await _userRepository.GetUserByUsernameAsync(username);

            if (user != null && VerifyPassword(password, user.PasswordHash))
            {
                await CloudSyncCredentialStore.StoreAsync(username, password);
                Preferences.Set("UserId", user.UserId); // Store UserId properly
                Preferences.Set("IsLoggedIn", true);
                Debug.WriteLine("This is the login page "+Preferences.Get("UserId", 0));
                ((AppShell)Shell.Current).UpdateMenuForLoginStatus(true);
                await Shell.Current.GoToAsync("//MainPage");
            }
            else
            {
                await DisplayAlertAsync("Login Failed", "Invalid username or password", "OK");
            }
        }

        public static bool VerifyPassword(string enteredPassword, string storedHash)
        {
            return BCrypt.Net.BCrypt.Verify(enteredPassword, storedHash);
        }
        private void OnRegisterTapped(object sender, EventArgs e)
        {
            Navigation.PushAsync(new RegisterPage());
        }
    }
}
