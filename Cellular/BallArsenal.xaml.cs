using Cellular.Data;
using Microsoft.Maui.Controls;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace Cellular;

public partial class BallArsenal : ContentPage
{
    private readonly CellularDatabase _database;

    public BallArsenal()
    {
        InitializeComponent();
        _database = new CellularDatabase();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadBalls();
    }
    private async Task LoadBalls()
    {
        var balls = await _database.GetBallsAsync();
        BallsListView.ItemsSource = balls;
    }

    async private void OnAddBallBtnClicked(object sender, EventArgs e)
    {
        //Navigation.PushAsync(new NewBall());
    }



}