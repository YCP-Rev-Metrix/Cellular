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
        }
        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }

        public static async void SetLogin()
        {
            Preferences.Set("IsLoggedIn", false);
        }

        public static string LastSavePath
        {
            get => Preferences.Get(LastSavePathKey, string.Empty);
            set => Preferences.Set(LastSavePathKey, value);
        }
    }
}
