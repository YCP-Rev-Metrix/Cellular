﻿using System.Diagnostics;
using Cellular.Data;
using Cellular.ViewModel;

namespace Cellular
{
    public partial class EditAccountPage : ContentPage
    {
        private readonly UserRepository _userRepository;
        private readonly MainViewModel _viewModel;

        public EditAccountPage(UserRepository userRepository)
        {
            InitializeComponent();

            _userRepository = userRepository;
            _viewModel = new MainViewModel();
            BindingContext = _viewModel;
            LoadUserDetails();
        }

        private async void LoadUserDetails()
        {
            if (_viewModel.UserID == null) return;

            var user = await _userRepository.GetUserByIdAsync(_viewModel.UserID.Value);
            Debug.WriteLine(_viewModel.UserID);

            if (user != null)
            {
                _viewModel.UserName = user.UserName ?? "Guest";
                _viewModel.NewUserName = user.UserName ?? "Guest";
                _viewModel.FirstName = user.FirstName ?? "N/A";
                _viewModel.LastName = user.LastName ?? "N/A";
                _viewModel.Email = user.Email ?? "N/A";
            }
            else
            {
                Debug.WriteLine("User not found.");
            }
        }


        private async void OnSaveChangesClicked(object sender, EventArgs e)
        {
            if (_viewModel.UserID == null || _viewModel.UserID == 0)
            {
                Debug.WriteLine("UserID is null or invalid.");
                await DisplayAlert("Error", "Invalid user ID.", "OK");
                return;
            }

            // Fetch the user using UserID instead of UserName
            var user = await _userRepository.GetUserByIdAsync(_viewModel.UserID.Value);

            if (user == null)
            {
                await DisplayAlert("Error", "User not found.", "OK");
                return;
            }

            // Update the user's details
            user.UserName = entryUsername.Text;
            user.FirstName = entryFirstName.Text;
            user.LastName = entryLastName.Text;
            user.Email = entryEmail.Text;

            if (!string.IsNullOrEmpty(newPasswordEntry.Text))
            {
                user.Password = newPasswordEntry.Text;
            }

            // Save changes in the database
            await _userRepository.UpdateUserAsync(user);

            // Update ViewModel with new values
            _viewModel.UserName = user.UserName;
            _viewModel.FirstName = user.FirstName;
            _viewModel.LastName = user.LastName;
            _viewModel.Email = user.Email;

            // Ensure UserID is still stored
            _viewModel.UserID = user.UserId;

            // Update Preferences for new username
            Preferences.Set("UserName", user.UserName);

            // Notify UI of changes
            _viewModel.NotifyUserDetailsChanged();

            await DisplayAlert("Success", "Your account has been updated.", "OK");
            await Shell.Current.GoToAsync("..");
        }
    }
}
