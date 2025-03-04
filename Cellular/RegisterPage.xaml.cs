﻿using Cellular.Data;
using Cellular.ViewModel;
using Microsoft.Maui.Storage;
using SQLite;

namespace Cellular
{
    public partial class RegisterPage : ContentPage
    {
        public RegisterPage()
        {
            InitializeComponent();
        }

        private async void OnRegisterClicked(object sender, EventArgs e)
        {
            string username = entryUsername.Text;
            string password = entryPassword.Text;
            string cpassword = entryConfirmPassword.Text;
            string firstName = entryFirstName.Text;
            string lastName = entryLastName.Text;
            string email = entryEmail.Text;

            // Check if any field is empty
            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(cpassword) ||
                string.IsNullOrWhiteSpace(firstName) ||
                string.IsNullOrWhiteSpace(lastName) ||
                string.IsNullOrWhiteSpace(email))
            {
                await DisplayAlert("Registration Error", "Please fill in all fields", "OK");
                return;
            }

            // Get the user repository from the database
            var userRepository = new UserRepository(DependencyService.Get<SQLiteAsyncConnection>());

            // Check if a user with the given username or email already exists
            var existingUserByUsername = await userRepository.GetUserByUsernameAsync(username);
            var existingUserByEmail = await userRepository.GetUserByEmailAsync(email);

            // Check if the username or email already exists in the database
            if (existingUserByUsername != null)
            {
                await DisplayAlert("Registration Error", "Username already exists", "OK");
            }
            else if (existingUserByEmail != null)
            {
                await DisplayAlert("Registration Error", "Email already exists", "OK");
            }
            else if (password != cpassword)
            {
                await DisplayAlert("Registration Error", "Passwords do not match", "OK");
            }
            else
            {
                // Create a new user
                var newUser = new User
                {
                    UserName = username,
                    Password = password,
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    LastLogin = DateTime.Now
                };

                // Add the new user to the database
                await userRepository.AddAsync(newUser);

                // Set the user as logged in
                Preferences.Set("IsLoggedIn", true);
                Preferences.Set("UserName", username);

                // Update the menu and navigate to Home
                ((AppShell)Shell.Current).UpdateMenuForLoginStatus(true);
                await Shell.Current.GoToAsync("//MainPage");
            }
        }

    }
}

