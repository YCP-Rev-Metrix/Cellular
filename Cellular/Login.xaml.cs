namespace Cellular
{
    public partial class LoginPage : ContentPage
    {
        public LoginPage()
        {
            InitializeComponent();
        }
        private void OnLoginClicked(object sender, EventArgs e)
        {
            bool isLoggedIn = true; // Set based on authentication
            ((AppShell)Shell.Current).UpdateMenuForLoginStatus(isLoggedIn);

        }

    }

}
