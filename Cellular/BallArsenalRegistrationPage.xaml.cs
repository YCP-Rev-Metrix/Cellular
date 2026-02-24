using System;
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

        // ensure initial visibility matches any pre-selected value (usually none)
        UpdateHexVisibility();
    }

    private void OnBallColorSelectedIndexChanged(object sender, EventArgs e)
    {
        UpdateHexVisibility();
    }

    private void UpdateHexVisibility()
    {
        var selected = BallColor.SelectedItem?.ToString();
        BallHexColor.IsVisible = string.Equals(selected, "Custom", StringComparison.OrdinalIgnoreCase);
    }

    private async void OnRegisterBallClicked(object sender, EventArgs e)
    {
        // Get the entered data
        string ballName = BallName.Text;
        string? ballWeight = BallWeight.Text;
        string? ballCore = BallCoreType.Text;
        string? ballColor = BallColor.SelectedItem?.ToString();
        // Validation to ensure all fields are filled
        if (string.IsNullOrWhiteSpace(ballName))
        {
            await DisplayAlert("Error", "Please enter a Ball name.", "OK");
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
        // If custom was selected, ensure a hex color is provided
        if (string.Equals(ballColor, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            var hex = BallHexColor.Text;
            if (string.IsNullOrWhiteSpace(hex))
            {
                await DisplayAlert("Error", "Please enter a hex color value for custom color.", "OK");
                return;
            }

            // optional: basic hex validation
            if (!System.Text.RegularExpressions.Regex.IsMatch(hex.Trim(), "^#?[0-9A-Fa-f]{6}$"))
            {
                await DisplayAlert("Error", "Please enter a valid 6-digit hex color (e.g. #FFAABB).", "OK");
                return;
            }

            // normalize to include leading '#'
            if (!hex.StartsWith("#")) hex = "#" + hex.Trim();
            ballColor = hex;
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
            //SerialNumber = serial,
            Weight = weight,
            Core = ballCore,
            ColorString = ballColor ?? string.Empty
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
        BallWeight.Text = string.Empty;
        BallCoreType.Text = string.Empty;
        BallColor.SelectedIndex = -1;
        BallHexColor.Text = string.Empty;
        BallHexColor.IsVisible = false;
    }

}
