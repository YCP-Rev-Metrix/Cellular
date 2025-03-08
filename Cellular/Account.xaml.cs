using Cellular.Data;
using Cellular.ViewModel;
using SQLite;

namespace Cellular
{
    public partial class AccountPage : ContentPage
    {
        private readonly UserRepository _userRepository;
        private readonly MainViewModel _viewModel;

        public AccountPage()
        {
            InitializeComponent();

            // Initialize UserRepository and ViewModel
            _userRepository = new UserRepository(new CellularDatabase().GetConnection());
            _viewModel = new MainViewModel();  // ViewModel instance
            BindingContext = _viewModel;

        }

        private async void OnSignoutClicked(object sender, EventArgs e)
        {
            await SecureStorage.SetAsync("IsLoggedIn", "false");

            // Update menu and navigate to Home after logging out
            ((AppShell)Shell.Current).UpdateMenuForLoginStatus(false);

            await Shell.Current.GoToAsync("//MainPage");
        }

        private async void OnEditAccountClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new EditAccountPage(_userRepository));
        }

        private void OnStatsClicked(object sender, EventArgs e)
        {
            // Implement when stats are figured out
        }
    }
}

