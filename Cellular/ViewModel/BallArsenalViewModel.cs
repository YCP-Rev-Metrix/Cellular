using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Cellular.Data;
using CellularCore;

namespace Cellular.ViewModel
{
    public class BallArsenalViewModel : INotifyPropertyChanged
    {
        private readonly BallRepository _ballRepo;
        private int _userId;

        public ObservableCollection<Ball> Balls { get; } = new();

        // raised when SelectedIndex changes
        public event EventHandler? SelectedIndexChanged;

        private Ball? _selectedBall;
        public Ball? SelectedBall
        {
            get => _selectedBall;
            set
            {
                if (_selectedBall != value)
                {
                    _selectedBall = value;
                    OnPropertyChanged();
                    // update index and notify listeners
                    SelectedIndex = _selectedBall is null ? -1 : Balls.IndexOf(_selectedBall);
                }
            }
        }

        private int _selectedIndex = -1;
        public int SelectedIndex
        {
            get => _selectedIndex;
            private set
            {
                if (_selectedIndex != value)
                {
                    _selectedIndex = value;
                    OnPropertyChanged();
                    SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public BallArsenalViewModel()
        {
            _ballRepo = new BallRepository(new CellularDatabase().GetConnection());
        }

        public async Task LoadBallsAsync()
        {
            try
            {
                _userId = Preferences.Get("UserId", 0);
                Debug.WriteLine("BallArsenalViewModel: userId=" + _userId);

                if (_userId == 0)
                {
                    Balls.Clear();
                    SelectedBall = null;
                    return;
                }

                var ballsFromDb = await _ballRepo.GetBallsByUserIdAsync(_userId);
                Balls.Clear();
                foreach (var ball in ballsFromDb)
                {
                    Balls.Add(ball);
                    Debug.WriteLine($"Ball color: {ball.ColorString}");
                }

                // ensure selected index is consistent after load
                if (SelectedBall != null)
                {
                    SelectedIndex = Balls.IndexOf(SelectedBall);
                }
                else
                {
                    SelectedIndex = -1;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadBallsAsync failed: {ex.Message}");
            }
        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
