namespace Cellular
{
    public partial class GameList : ContentPage
    {
        public GameList()
        {
            InitializeComponent();
        }

        private async void OnGameClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new GameInterface());
        }
    
    }

}
