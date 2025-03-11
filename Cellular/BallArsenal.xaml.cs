using Cellular.Data;
using Cellular.ViewModel;
using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Cellular
{
    public partial class BallArsenal : ContentPage
    {
        private readonly BallRepository _ballRepository;
        public ObservableCollection<Ball> Balls { get; set; }

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
            var ballsFromDb = await _ballRepository.GetAllBallsAsync();
            Balls.Clear();
            foreach (var ball in ballsFromDb)
            {
                Balls.Add(ball);
            }
        }

        async private void OnAddBallBtnClicked(object sender, EventArgs e)
        {
            // Add a new ball to the database
            var newBall = new Ball { Name = "New Ball", Diameter = 15, Weight = 2, Core = "Foam" };
            await _ballRepository.AddAsync(newBall);
            Balls.Add(newBall);
        }
    }
}
