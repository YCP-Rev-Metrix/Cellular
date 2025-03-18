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
        private async void LoadBalls()
        {
            userId = Preferences.Get("UserId", 0);
            //Debug.WriteLine("This is USer ID"+ userId);
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
            // Add a new ball to the database
            Debug.WriteLine("This is NEW BALL");
            var newBall = new Ball { Name = "New Ball", UserId =  userId, Diameter = 15, Weight = 2, Core = "Foam" };
            await _ballRepository.AddAsync(newBall);
            Balls.Add(newBall);
        }
    }
}
