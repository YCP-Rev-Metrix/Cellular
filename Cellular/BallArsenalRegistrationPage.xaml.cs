using System.Collections.ObjectModel;
using System.Diagnostics;
using Cellular.Data;
using Cellular.ViewModel;

namespace Cellular;

public partial class BallArsenalRegistrationPage : ContentPage
{
    private readonly BallRepository _BallRepository;
    public ObservableCollection<Ball> Balls { get; set; }
    public BallArsenalRegistrationPage()
    {
        InitializeComponent();
        _BallRepository = new BallRepository(new CellularDatabase().GetConnection());
        Balls = new ObservableCollection<Ball>();
    }
    private async void OnRegisterBallClicked(object sender, EventArgs e)
    {
        // Get the entered data
        string ballName = BallName.Text;
        string? ballDiameter = BallDiameter.Text;
        string? ballWeight = BallWeight.Text;
        string? ballCore = BallCoreType.Text;
        // Validation to ensure all fields are filled
        if (string.IsNullOrWhiteSpace(ballName))
        {
            await DisplayAlert("Error", "Please enter a Ball name.", "OK");
            return;
        }
        if (string.IsNullOrEmpty(ballDiameter))
        {
            await DisplayAlert("Error", "Please enter a Ball diameter.", "OK");
            return;
        }
        if (string.IsNullOrEmpty(ballWeight))
        {
            await DisplayAlert("Error", "Please enter a Ball weight.", "OK");
            return;
        }
        if (string.IsNullOrEmpty(ballCore))
        {
            await DisplayAlert("Error", "Please enter a Ball core.", "OK");
            return;
        }
        // Convert string inputs to integers where necessary
        if (!int.TryParse(ballDiameter, out int diameter))
        {
            await DisplayAlert("Error", "Ball diameter must be a valid number.", "OK");
            return;
        }
        if (!int.TryParse(ballWeight, out int weight))
        {
            await DisplayAlert("Error", "Ball weight must be a valid number.", "OK");
            return;
        }
        // You can further process the data here (e.g., save it to a database or display a success message)
        var newBall = new Ball
        {
            UserId = Preferences.Get("UserId", 0),
            Name = ballName,
            Diameter = diameter,
            Weight = weight,
            Core = ballCore
        };
        var existingBall = await _BallRepository.GetBallByNameAsync(ballName);
        //Debug.WriteLine(existingBall);
        if (existingBall == null)
        {
            await _BallRepository.AddAsync(newBall);
            Balls.Add(newBall);
        }
       
        await DisplayAlert("Ball", "The Ball was Added", "OK");
        // Optionally, clear the form
        BallName.Text = string.Empty;
        BallDiameter.Text = string.Empty;
        BallWeight.Text = string.Empty;
        BallCoreType.Text = string.Empty;
    }

}
