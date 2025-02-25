using Microsoft.Maui.Storage;

namespace Cellular
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            SetLogin();
        }
        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }

        public async void SetLogin()
        {
            await SecureStorage.SetAsync("IsLoggedIn", "false");
        }
    }
}
