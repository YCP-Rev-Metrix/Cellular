using System.Diagnostics;
using Cellular.Data;
using Cellular.ViewModel;

namespace Cellular
{
    public partial class EditAccountPage : ContentPage
    {
        private readonly UserRepository _userRepository;
        private readonly MainViewModel _viewModel;

        // Inject UserRepository via the constructor and set BindingContext
        public EditAccountPage(UserRepository userRepository)
        {
            InitializeComponent();

            _userRepository = userRepository;  // Store the injected repository
            _viewModel = new MainViewModel();  // Create the ViewModel instance
            BindingContext = _viewModel;      // Set the BindingContext to the ViewModel
        }

        // Save changes when Save Changes button is clicked
        private async void OnSaveChangesClicked(object sender, EventArgs e)
        {
            // Debug the username
            Debug.WriteLine($"Searching for user with username: {_viewModel.UserName}");

            // Fetch the user by username from the database
            var user = await _userRepository.GetUserByUsernameAsync(_viewModel.UserName);

            // Check if user is found
            if (user == null)
            {
                Debug.WriteLine("User not found.");
                await DisplayAlert("Error", "User not found.", "OK");
                return;
            }

            Debug.WriteLine($"Fetched user: {user.UserName}, {user.Password}");

            // Check if the old password entered is correct
            if (user.Password != oldPasswordEntry.Text)
            {
                await DisplayAlert("Error", "The old password is incorrect.", "OK");
                return;
            }

            // Validate if new passwords match
            if (newPasswordEntry.Text != confirmPasswordEntry.Text)
            {
                await DisplayAlert("Error", "New passwords do not match.", "OK");
                return;
            }

            // Update the user's information if necessary
            user.UserName = entryUsername.Text;
            user.FirstName = entryFirstName.Text;
            user.LastName = entryLastName.Text;
            user.Email = entryEmail.Text;

            // Update password if it's changed
            if (!string.IsNullOrEmpty(newPasswordEntry.Text))
            {
                user.Password = newPasswordEntry.Text;
            }

            // Save the updated user in the database
            await _userRepository.UpdateUserAsync(user);

            await DisplayAlert("Success", "Your account has been updated.", "OK");
            await Shell.Current.GoToAsync("//AccountPage"); // Go back to the account page
        }
    }
}
