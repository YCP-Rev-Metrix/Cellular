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

            // Initialize repository and use the shared ViewModel
            _userRepository = new UserRepository(new CellularDatabase().GetConnection());
            _viewModel = new MainViewModel();
            BindingContext = _viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Reload user details whenever the page appears
            await _viewModel.LoadUserData();
        }

        private async void OnSignoutClicked(object sender, EventArgs e)
        {
            Preferences.Set("IsLoggedIn", false);

            // Update menu and navigate to Home after logging out
            ((AppShell)Shell.Current).UpdateMenuForLoginStatus(false);
            Preferences.Default.Clear();

            await Shell.Current.GoToAsync("//MainPage");
        }

        private async void OnEditAccountClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new EditAccountPage(_userRepository));
        }

        private async void OnStatsClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new Stats());
        }
    }
}
