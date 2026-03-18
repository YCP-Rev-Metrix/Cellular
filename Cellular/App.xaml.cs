using Cellular.Data;
using Microsoft.Maui.Storage;
using SQLite;

namespace Cellular
{
    public partial class App : Application
    {
        public static string LoggedInUserName { get; set; } = "Guest"; // Default name
        private const string LastSavePathKey = "last_save_path"; // Video save path

        public App()
        {
            InitializeComponent();
            App.SetLogin();
            // Register SQLite dependency
            DependencyService.RegisterSingleton<SQLiteAsyncConnection>(new SQLiteAsyncConnection(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "appdata.db")));

            // Reset IsConnected to false on app startup (in case app was killed without cleanup)
            ResetIsConnectedAsync();
        }
        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }

        public static async void SetLogin()
        {
            Preferences.Set("IsLoggedIn", false);
        }

        /// <summary>
        /// Resets IsConnected to false for all users on app startup.
        /// This handles the case where the app was closed/killed without proper cleanup.
        /// </summary>
        private static async void ResetIsConnectedAsync()
        {
            try
            {
                var db = new CellularDatabase().GetConnection();
                var userRepo = new UserRepository(db);
                var allUsers = await userRepo.GetAllUsersAsync();
                foreach (var user in allUsers)
                {
                    if (user.IsConnected)
                    {
                        await userRepo.UpdateIsConnectedAsync(user.UserId, false);
                        System.Diagnostics.Debug.WriteLine($"[App] Reset IsConnected to false for user {user.UserId}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] Error resetting IsConnected: {ex.Message}");
            }
        }

        public static string LastSavePath
        {
            get => Preferences.Get(LastSavePathKey, string.Empty);
            set => Preferences.Set(LastSavePathKey, value);
        }
    }
}
