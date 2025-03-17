using System.Diagnostics;
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

            var user = await _userRepository.GetUserByUsernameAsync(username);

            if (user != null && VerifyPassword(password, user.PasswordHash))
            {
                Preferences.Set("IsLoggedIn", true);
                Preferences.Set("UserId", user.UserId); // Store UserId properly

                Debug.WriteLine("This is the login page "+Preferences.Get("UserId", 0));
                ((AppShell)Shell.Current).UpdateMenuForLoginStatus(true);

                var mainViewModel = new MainViewModel();
                mainViewModel.UserID = user.UserId; // Set UserId instead of UserName

                await ((AppShell)Shell.Current).OnLoginSuccess();
                await Shell.Current.GoToAsync("//MainPage");
            }
            else
            {
                await DisplayAlert("Login Failed", "Invalid username or password", "OK");
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
