namespace Cellular;

public partial class DataPage : ContentPage
{
    private EventPage? EventPage;
    private EstablishmentPage? EstablishmentPage;
    public DataPage()
	{
		InitializeComponent();
        EventPage = new EventPage();
        EstablishmentPage = new EstablishmentPage();
    }
    private async void NavigateToEstablishmentCommand(object sender, EventArgs e)
	{
        await Navigation.PushAsync(EventPage);
    }
    private async void NavigateToEventCommand(object sender, EventArgs e)
    {
        await Navigation.PushAsync(EstablishmentPage);
    }
}