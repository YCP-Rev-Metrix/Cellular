using Microsoft.Maui.Storage;

namespace Cellular
{
    public partial class RegisterPage : ContentPage
    {
        public RegisterPage()
        {
            InitializeComponent();
        }

        private void OnRegisterClicked(object sender, EventArgs e)
        {
            string username = entryUsername.Text;
            string password = entryPassword.Text;
            string cpassword = entryConfirmPassword.Text;

            // checks for username existing
            if (username == "string")
            {
                DisplayAlert("Registration Error", "User already exists", "OK");
            }
            else if (password != cpassword)
            {
                DisplayAlert("Registration Error", "Passwords do not match", "OK");
            }
            else
            {
                Preferences.Set("IsLoggedIn", true);

                // Update menu and navigate to Home
                ((AppShell)Shell.Current).UpdateMenuForLoginStatus(true);
                Shell.Current.GoToAsync("//MainPage");
            }
        }
    }
}

