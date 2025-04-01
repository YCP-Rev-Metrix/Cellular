using Microsoft.Maui.Storage;
using SQLite;

namespace Cellular
{
    public partial class App : Application
    {
        public static string LoggedInUserName { get; set; } = "Guest"; // Default name

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

    }
}
