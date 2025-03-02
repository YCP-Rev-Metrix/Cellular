﻿using Microsoft.Maui.Storage;

namespace Cellular
{
    public partial class LoginPage : ContentPage
    {
        public LoginPage()
        {
            InitializeComponent();
        }

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            string username = entryUsername.Text;
            string password = entryPassword.Text;

            // Hardcoded credentials
            if (username == "string" && password == "string")
            {
                Preferences.Set("IsLoggedIn", true);

                // Update menu and navigate to Home
                ((AppShell)Shell.Current).UpdateMenuForLoginStatus(true);

                Preferences.Set("UserName", username); //stores the users username

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

