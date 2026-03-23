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
    private Ball? _editingBall;
    public BallArsenalRegistrationPage()
    {
        InitializeComponent();
        _BallRepository = new BallRepository(new CellularDatabase().GetConnection());
        Balls = new ObservableCollection<Ball>();

        // ensure initial visibility matches any pre-selected value (usually none)
        UpdateHexVisibility();
    }

    public BallArsenalRegistrationPage(Ball ballToEdit) : this()
    {
        _editingBall = ballToEdit;
        // Populate fields from the existing ball
        BallName.Text = _editingBall.Name;
        BallMFG.Text = _editingBall.BallMFG;
        BallMFGName.Text = _editingBall.BallMFGName;
        SerialNumber.Text = _editingBall.SerialNumber;
        BallWeight.Text = _editingBall.Weight > 0 ? _editingBall.Weight.ToString() : string.Empty;
        BallCoreType.Text = _editingBall.Core;
        Coverstock.Text = _editingBall.Coverstock;
        Comment.Text = _editingBall.Comment;
        if (!string.IsNullOrEmpty(_editingBall.ColorString))
        {
            // If the color matches one of the picker items, select it; otherwise select Custom and fill hex
            var color = _editingBall.ColorString;
            int idx = BallColor.Items.IndexOf(color);
            if (idx >= 0)
            {
                BallColor.SelectedIndex = idx;
            }
            else
            {
                BallColor.SelectedItem = "Custom";
                BallHexColor.Text = color;
                BallHexColor.IsVisible = true;
            }
        }
        RegisterBallButton.Text = "Save";
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
        string ballName = BallName.Text?.Trim() ?? string.Empty;
        string ballMFG = BallMFG.Text?.Trim() ?? string.Empty;
        string ballMFGName = BallMFGName.Text?.Trim() ?? string.Empty;
        string? ballSerial = SerialNumber.Text?.Trim();
        string? ballWeight = BallWeight.Text?.Trim();
        string? ballCore = BallCoreType.Text?.Trim();
        string? ballCoverstock = Coverstock.Text?.Trim();
        string? ballColor = BallColor.SelectedItem?.ToString();
        string? comment = Comment.Text?.Trim();
        // Validation to ensure required fields are filled
        if (string.IsNullOrWhiteSpace(ballName))
        {
            await DisplayAlertAsync("Error", "Please enter a Ball name.", "OK");
            return;
        }
        var existingBall = await _BallRepository.GetBallByNameAndUserAsync(ballName, Preferences.Get("UserId", 0));

        // If creating a new ball and a ball with this name exists, block. If editing, allow the same
        // name if it belongs to the ball being edited.
        if (existingBall != null && (_editingBall == null || existingBall.BallId != _editingBall.BallId))
        {
            await DisplayAlertAsync("Duplicate Name",
                $"You already have a ball named '{ballName}' in your arsenal.", "OK");
            return;
        }
        if (string.IsNullOrEmpty(ballMFG))
        {
            await DisplayAlertAsync("Error", "Please enter a Ball MFG.", "OK");
            return;
        }
        if (string.IsNullOrEmpty(ballMFGName))
        {
            await DisplayAlertAsync("Error", "Please enter a Ball MFG Name.", "OK");
            return;
        }
            // If custom was selected, ensure a hex color is provided
            if (string.Equals(ballColor, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            var hex = BallHexColor.Text;
            if (string.IsNullOrWhiteSpace(hex))
            {
                await DisplayAlertAsync("Error", "Please enter a hex color value for custom color.", "OK");
                return;
            }

            // optional: basic hex validation
            if (!System.Text.RegularExpressions.Regex.IsMatch(hex.Trim(), "^#?[0-9A-Fa-f]{6}$"))
            {
                await DisplayAlertAsync("Error", "Please enter a valid 6-digit hex color (e.g. #FFAABB).", "OK");
                return;
            }

            // normalize to include leading '#'
            if (!hex.StartsWith("#")) hex = "#" + hex.Trim();
            ballColor = hex;
        }

        int weight = 0;
        if (!string.IsNullOrWhiteSpace(ballWeight))
        {
            if (!int.TryParse(ballWeight, out weight))
            {
                await DisplayAlertAsync("Error", "Ball weight must be a valid number.", "OK");
                return;
            }
        }

        // You can further process the data here (e.g., save it to a database or display a success message)
        var newBall = new Ball
        {
            UserId = Preferences.Get("UserId", 0),
            Name = ballName,
            BallMFG = ballMFG,
            BallMFGName = ballMFGName,
            SerialNumber = ballSerial,
            Weight = weight,
            Core = ballCore,
            Coverstock = ballCoverstock,
            Comment = comment,
            ColorString = ballColor ?? null
        };
        if (_editingBall != null)
        {
            // Update existing
            _editingBall.Name = newBall.Name;
            _editingBall.BallMFG = newBall.BallMFG;
            _editingBall.BallMFGName = newBall.BallMFGName;
            _editingBall.SerialNumber = newBall.SerialNumber;
            _editingBall.Weight = newBall.Weight;
            _editingBall.Core = newBall.Core;
            _editingBall.Coverstock = newBall.Coverstock;
            _editingBall.Comment = newBall.Comment;
            _editingBall.ColorString = newBall.ColorString;

            await _BallRepository.UpdateBallAsync(_editingBall);
            await DisplayAlertAsync("Ball", "The Ball was Updated", "OK");
            await Navigation.PopAsync();
        }
        else
        {
            await _BallRepository.AddAsync(newBall); // Saves to SQLite
            Balls.Add(newBall);
            await DisplayAlertAsync("Ball", "The Ball was Added", "OK");
            await Navigation.PopAsync();
        }
        // Optionally, clear the form
        BallName.Text = string.Empty;
        BallMFG.Text = string.Empty;
        BallMFGName.Text = string.Empty;
        SerialNumber.Text = string.Empty;
        BallWeight.Text = string.Empty;
        BallCoreType.Text = string.Empty;
        Coverstock.Text = string.Empty;
        Comment.Text = string.Empty;
        BallColor.SelectedIndex = -1;
        BallHexColor.Text = string.Empty;
        BallHexColor.IsVisible = false;
        _editingBall = null;
    }

}
