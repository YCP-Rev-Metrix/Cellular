using Microsoft.Maui.Storage;

namespace Cellular
{
    public partial class App : Application
    {
        public static string LoggedInUserName { get; set; } = "Guest"; // Default name

        public App()
        {
            InitializeComponent();
            App.SetLogin();
        }
        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }

        public static async void SetLogin()
        {
            await SecureStorage.SetAsync("IsLoggedIn", "false");
        }

    }
}
