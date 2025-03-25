using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq; // For LINQ extension methods
using System.Runtime.CompilerServices;
using Cellular.Data;
using SQLite;

namespace Cellular.ViewModel
{
    internal partial class GameInterfaceViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<string> players;
        private ObservableCollection<string> arsenal;
        private ObservableCollection<ShotPageFrame> frames;
        private readonly SQLiteAsyncConnection _database;
        private string _hand = "Left";
        public short pinStates = 0;
        public int currentFrame = 1;
        public int currentShot = 1;


        public ObservableCollection<string> Players
        {
            get => players;
            set
            {
                players = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<string> Arsenal
        {
            get => arsenal;
            set
            {
                arsenal = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<ShotPageFrame> Frames
        {
            get => frames;
            set
            {
                frames = value;
                OnPropertyChanged();
            }
        }

        // Property to return the first 12 frames
        public ObservableCollection<ShotPageFrame> First12Frames
        {
            get
            {
                // Returns the first 12 frames, or fewer if there are not enough frames
                return [.. frames.Take(10)];
            }
        }

        public GameInterfaceViewModel()
        {
            string dbPath = Path.Combine(FileSystem.AppDataDirectory, "appdata.db");
            _database = new SQLiteAsyncConnection(dbPath);

            frames =
            [
                new (1, 10),
                new (2, 20),
                new (3, 30),
                new (4, 40),
                new (5, 50),
                new (6, 60),
                new (7, 70),
                new (8, 80),
                new (9, 90),
                new (10, 100)
            ];

            LoadUsers();
            LoadArsenal();
        }

        public string Hand
        {
            get => _hand;
            set
            {
                if (_hand != value)
                {
                    _hand = value;
                    OnPropertyChanged();
                }
            }
        }

        public async Task LoadUserHand()
        {
            var mainViewModel = new MainViewModel();
            await mainViewModel.LoadUserData();
            _hand = mainViewModel.Hand;
        }

        private string _shotOneBox;
        public string ShotOneBox
        {
            get => _shotOneBox;
            set
            {
                _shotOneBox = value;
                OnPropertyChanged(nameof(ShotOneBox));
            }
        }

        private string _shotTwoBox;
        public string ShotTwoBox
        {
            get => _shotTwoBox;
            set
            {
                _shotTwoBox = value;
                OnPropertyChanged(nameof(ShotTwoBox));
            }
        }

        // Notify property changed
        public event PropertyChangedEventHandler? PropertyChanged;

        public void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public async void LoadUsers()
        {
            var playerList = await _database.Table<User>().ToListAsync();
            Players = [.. playerList.Select(p => p.FirstName)];
        }

        public async void LoadArsenal()
        {
            var arsenalList = await _database.Table<Ball>().ToListAsync();
            Arsenal = [.. arsenalList.Select(a => a.Name)];
        }

    }

    public class ShotPageFrame : INotifyPropertyChanged
    {
        public int FrameNumber { get; set; }
        public int RollingScore { get; set; }
        public string ShotOneBox { get; set; }
        public string ShotTwoBox { get; set; }

        private ObservableCollection<Color> _pinColors;
        private ObservableCollection<Color> _centerPinColors;
        public ObservableCollection<Color> PinColors
        {
            get => _pinColors;
            set
            {
                _pinColors = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<Color> CenterPinColors
        {
            get => _centerPinColors;
            set
            {
                _centerPinColors = value;
                OnPropertyChanged();
            }
        }

        public ShotPageFrame(int frameNumber, int rollingScore)
        {
            FrameNumber = frameNumber;
            RollingScore = rollingScore;
            ShotOneBox = "";
            ShotTwoBox = "";
            PinColors = [.. Enumerable.Repeat(Colors.Black, 10)];
            CenterPinColors = [.. Enumerable.Repeat(Colors.Transparent, 10)];
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void UpdatePinColor(int pinIndex, Color newColor)
{
    if (_centerPinColors[pinIndex] != newColor)
    {
        _centerPinColors[pinIndex] = newColor;
        OnPropertyChanged(nameof(CenterPinColors));
    }
}

    }

}
