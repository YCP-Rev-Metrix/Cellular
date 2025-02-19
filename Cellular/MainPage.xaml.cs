namespace Cellular
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new LoginPage());
        }
        private void OnSignupClicked(object sender, EventArgs e)
        {
            
        }
      
    }

}
