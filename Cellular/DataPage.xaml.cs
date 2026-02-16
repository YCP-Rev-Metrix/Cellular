namespace Cellular;

public partial class DataPage : ContentPage
{
    private EstablishmentPage? EstablishmentPage;
    public DataPage()
	{
		InitializeComponent();
        EstablishmentPage = new EstablishmentPage();
    }
    private async void NavigateToEstablishmentCommand(object sender, EventArgs e)
	{
        await Navigation.PushAsync(EstablishmentPage);
    }
}