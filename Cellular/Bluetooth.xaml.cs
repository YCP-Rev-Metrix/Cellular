namespace Cellular
{
    public partial class Bluetooth : ContentPage
    {
        public Bluetooth()
        {
            InitializeComponent();
        }

        private async void OnAddClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new MainPage()); 
        }
      
    }

}
