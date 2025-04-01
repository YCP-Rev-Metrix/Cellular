using Cellular.Data;
using Cellular.ViewModel;
using Microsoft.Maui.Storage;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Cellular
{
    public partial class RegisterPage : ContentPage
    {
        private readonly UserRepository _userRepository;

        // Constructor where we initialize the UserRepository
        public RegisterPage()
        {
            InitializeComponent();
            _userRepository = new UserRepository(new CellularDatabase().GetConnection());
        }

        private async void OnRegisterClicked(object sender, EventArgs e)
        {
            string username = entryUsername.Text;
            string password = entryPassword.Text;
            string cpassword = entryConfirmPassword.Text;
            string firstName = entryFirstName.Text;
            string lastName = entryLastName.Text;
            string email = entryEmail.Text;
            string cemail = confirmEntryEmail.Text;
            string phoneNumber = entryPhone.Text;

            // Check if any field is empty
            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(cpassword) ||
                string.IsNullOrWhiteSpace(firstName) ||
                string.IsNullOrWhiteSpace(lastName) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(cemail) ||
                string.IsNullOrWhiteSpace(phoneNumber))
            {
                await DisplayAlert("Registration Error", "Please fill in all fields", "OK");
                return;
            }

            // Get the user repository from the database
            var userRepository = new UserRepository(new CellularDatabase().GetConnection());

            // Ensure the table exists before querying it
            await userRepository.InitAsync();

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
            else if (email != cemail)
            {
                await DisplayAlert("Registration Error", "Emails do not match", "OK");
            }
            else
            {
                // Create a new user
                var newUser = new User
                {
                    UserName = username,
                    PasswordHash = HashPassword(password),
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    LastLogin = DateTime.Now,
                    PhoneNumber = phoneNumber,
                    Hand = null
                };

                // Add the new user to the database
                await userRepository.AddAsync(newUser);

                // Set the user as logged in
                Preferences.Set("IsLoggedIn", false);
                Preferences.Set("UserName", newUser.FirstName);

                // Update the menu and navigate to Home
                ((AppShell)Shell.Current).UpdateMenuForLoginStatus(false);
                await Shell.Current.GoToAsync("//MainPage");
            }

        }
        public static string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }
    }
}
