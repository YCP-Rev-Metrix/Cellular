using System.Diagnostics;
using System.Text.RegularExpressions;
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
                _viewModel.PhoneNumber = user.PhoneNumber ?? "N/A";
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

            var user = await _userRepository.GetUserByIdAsync(_viewModel.UserID.Value);

            if (user == null)
            {
                await DisplayAlert("Error", "User not found.", "OK");
                return;
            }

            // Validate passwords
            if (!string.IsNullOrEmpty(newPasswordEntry.Text) || !string.IsNullOrEmpty(confirmPasswordEntry.Text))
            {
                if (newPasswordEntry.Text != confirmPasswordEntry.Text)
                {
                    await DisplayAlert("Error", "New passwords do not match.", "OK");
                    return;
                }
            }

            // Validate emails
            if (!string.IsNullOrEmpty(entryEmail.Text) || !string.IsNullOrEmpty(confirmEntryEmail.Text))
            {
                if (entryEmail.Text != confirmEntryEmail.Text)
                {
                    await DisplayAlert("Error", "Emails do not match.", "OK");
                    return;
                }
            }
            // Update user details
            user.UserName = entryUsername.Text;
            user.FirstName = entryFirstName.Text;
            user.LastName = entryLastName.Text;
            user.Email = entryEmail.Text;
            user.PhoneNumber = entryPhone.Text;

            if (!string.IsNullOrEmpty(newPasswordEntry.Text))
            {
                user.PasswordHash = HashPassword(newPasswordEntry.Text);
            }

            await _userRepository.UpdateUserAsync(user);

            // Update ViewModel
            _viewModel.UserName = user.UserName;
            _viewModel.FirstName = user.FirstName;
            _viewModel.LastName = user.LastName;
            _viewModel.Email = user.Email;
            _viewModel.PhoneNumber = user.PhoneNumber;

            Preferences.Set("UserName", user.UserName);
            _viewModel.NotifyUserDetailsChanged();

            await DisplayAlert("Success", "Your account has been updated.", "OK");
            await Shell.Current.GoToAsync("..");
        }

        public static string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }
    }
}
