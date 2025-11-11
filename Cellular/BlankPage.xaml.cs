namespace Cellular
{
    public partial class BlankPage : ContentPage
    {
        public BlankPage()
        {
            InitializeComponent();
        }

        private async void OnButtonClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MainPage");
        }
      
    }

}
