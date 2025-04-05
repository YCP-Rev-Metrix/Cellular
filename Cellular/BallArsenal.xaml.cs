using Cellular.Data;
using Cellular.ViewModel;
using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Cellular
{
    public partial class BallArsenal : ContentPage
    {
        private readonly BallRepository _ballRepository;
        public ObservableCollection<Ball> Balls { get; set; }
        private int userId;

        public BallArsenal()
        {
            InitializeComponent();
            _ballRepository = new BallRepository(new CellularDatabase().GetConnection());
            Balls = new ObservableCollection<Ball>();
            BallsListView.BindingContext = this;
            LoadBalls();
            BallsListView.ItemsSource = Balls;
            
        }
        protected override void OnAppearing()
        {
            base.OnAppearing();

            // Prevent duplicate entries by resetting the list
            BallsListView.ItemsSource = null;

            // Load the event list again
            LoadBalls();
            BallsListView.ItemsSource = Balls;
        }
        private async void LoadBalls()
        {
            userId = Preferences.Get("UserId", 0);
            Debug.WriteLine("This is USer ID"+ userId);
            //Debug.WriteLine("This is strighat form the pref" + Preferences.Get("UserId", 0));
            if (userId == 0)
            {
                return;
            }
            var ballsFromDb = await _ballRepository.GetBallsByUserIdAsync(userId);
            Balls.Clear();
            foreach (var ball in ballsFromDb)
            {
                Debug.WriteLine("This is ball name" + ball.Name);
                Balls.Add(ball);
            }
        }

        async private void OnAddBallBtnClicked(object sender, EventArgs e)
        {
            // Navigate to the event registration page
            await Navigation.PushAsync(new BallArsenalRegistrationPage());
        }
    }
}
