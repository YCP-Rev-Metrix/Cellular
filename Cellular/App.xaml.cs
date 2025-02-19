using Microsoft.Maui.Storage;

namespace Cellular
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            SecureStorage.SetAsync("IsLoggedIn", "false");
        }
        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }
    }
}
