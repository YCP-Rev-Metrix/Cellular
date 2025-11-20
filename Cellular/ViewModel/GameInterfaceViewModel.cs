using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Linq;
using System.Runtime.CompilerServices;
using Cellular.Data;
using SQLite;
using System.Diagnostics;

namespace Cellular.ViewModel
{
    internal partial class GameInterfaceViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<string> players;
        private ObservableCollection<string> arsenal;
        private ObservableCollection<ShotPageFrame> frames;
        public event Action AlertEditFrame;
        private readonly SQLiteAsyncConnection _database;
        public string FrameDisplay => $"Gm {currentGame}-{CurrentFrame} \nShot {CurrentShot}";
        public string CurrentDate { get; set; } = Preferences.Get("Date", "Unknown");
        private string _hand = "Right";
        public short pinStates = 0;
        public short shot1PinStates = 0;
        public int _currentFrame = 1;
        public int _currentShot = 1;
        public int currentSession { get; set; } = Preferences.Get("SessionNumber", 0);
        public int currentGame { get; set; } = Preferences.Get("GameNumber", 0);
        public int gameId { get; set; } = Preferences.Get("GameID", 0);
        public int firstShotId = -1;
        public int secondShotId = -1;
        public int currentFrameId = -1;
        public int lastFrameId = -1;
        public int UserId = Preferences.Get("UserId", 0);
        public bool GameCompleted = false;
        public bool EditMode = false;
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

        public ICommand FrameTappedCommand { get; private set; }

        public GameInterfaceViewModel()
        {
            string dbPath = Path.Combine(FileSystem.AppDataDirectory, "appdata.db");
            _database = new SQLiteAsyncConnection(dbPath);

            frames =
            [
                new (1),
                new (2),
                new (3),
                new (4),
                new (5),
                new (6),
                new (7),
                new (8),
                new (9),
                new (10)
            ];

            LoadUsers();
            LoadArsenal();
            FrameTappedCommand = new Command<ShotPageFrame>(OnFrameTapped);
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

        public int CurrentFrame
        {
            get => _currentFrame;
            set
            {
                if (_currentFrame != value)
                {
                    _currentFrame = value;
                    OnPropertyChanged(nameof(CurrentFrame));
                }
            }
        }

        public int CurrentShot
        {
            get => _currentShot;
            set
            {
                if (_currentShot != value)
                {
                    _currentShot = value;
                    OnPropertyChanged(nameof(CurrentShot));
                    OnPropertyChanged(nameof(FrameDisplay));
                }
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

        public async void LoadEditInfo()
        {
            Debug.WriteLine($"Game ID: {gameId} Current frame: {CurrentFrame}");
            var frame = await _database.Table<BowlingFrame>().Where(f => f.GameId == gameId && f.FrameNumber == CurrentFrame).FirstOrDefaultAsync();
            if (frame != null)
            {
                currentFrameId = frame.FrameId;
                if(frame.Shot1 != null)
                {
                    firstShotId = (int)(frame.Shot1);
                }
            }
            var shot = await _database.Table<Shot>().Where(f => f.ShotId == firstShotId).FirstOrDefaultAsync();

            if (shot != null)
            {
                if (shot.LeaveType != null)
                {
                    pinStates = (short)(shot.LeaveType);
                    shot1PinStates = pinStates;
                    string result = Convert.ToString((ushort)pinStates, 2).PadLeft(16, '0');
                    Debug.WriteLine($"{result}");
                }
            }
            AlertEditFrame?.Invoke();
        }

        //Method used to edit frames/shots
        private void OnFrameTapped(ShotPageFrame frame)
        {
            if (frame == null) return;

            CurrentFrame = frame.FrameNumber;
            CurrentShot = 1;
            LoadEditInfo();
            OnPropertyChanged();
        }
    }

    public class ShotPageFrame : INotifyPropertyChanged
    {
        public int FrameNumber { get; set; }
        public int? RollingScore { get; set; }

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

        private string _shotOneBox;
        public string ShotOneBox
        {
            get => _shotOneBox;
            set
            {
                if (_shotOneBox != value)
                {
                    _shotOneBox = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _shotTwoBox;
        public string ShotTwoBox
        {
            get => _shotTwoBox;
            set
            {
                if (_shotTwoBox != value)
                {
                    _shotTwoBox = value;
                    OnPropertyChanged();
                }
            }
        }

        public ShotPageFrame(int frameNumber)
        {
            FrameNumber = frameNumber;
            RollingScore = null;
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

        public void UpdateCenterPinColor(int pinIndex, Color newColor)
        {
            if (_centerPinColors[pinIndex] != newColor)
            {
                _centerPinColors[pinIndex] = newColor;
                OnPropertyChanged(nameof(CenterPinColors));
            }
        }

        public void UpdatePinColor(int pinIndex, Color newColor)
        {
            if (_pinColors[pinIndex] != newColor)
            {
                _pinColors[pinIndex] = newColor;
                OnPropertyChanged(nameof(_pinColors));
            }
        }

        public void UpdateShotBox(int box, string value)
        {
            if (box == 1)
            {
                if (ShotOneBox == value)
                {
                    ShotOneBox = "";
                }
                else
                {
                    ShotOneBox = value;
                }
            }
            else if (box == 2)
            {
                if (ShotTwoBox == value)
                {
                    ShotTwoBox = "";
                }
                else
                {
                    ShotTwoBox = value;
                }
            }
        }
    }

}
