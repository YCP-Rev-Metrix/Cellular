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
        private ObservableCollection<Ball> arsenal;
        private ObservableCollection<ShotPageFrame> frames;
        public event Action AlertEditFrame;
        private readonly SQLiteAsyncConnection _database;
        public string FrameDisplay => $"Gm {currentGame}-{CurrentFrame} \nShot {CurrentShot}";
        public string CurrentDate { get; set; } = Preferences.Get("Date", "Unknown");
        private string _hand = "Right";
        public short pinStates = 0;
        public short shot1PinStates = 0;
        public int _currentFrame = 1;
        public int _frameCounter = 1;
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
        public int TotalScore = 0;
        public bool EditMode = false;
        public string frameResult = null;
        private Ball _selectedStrikeBall;
        private Ball _selectedSpareBall;
        private int _strikeBallId = -1;
        private int _spareBallId = -1;
        private string _comment = "";
        public ObservableCollection<string> Players
        {
            get => players;
            set
            {
                players = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<Ball> Arsenal
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

        // Separate commands for shot 1 and shot 2 taps so VM receives the ShotPageFrame instance directly.
        public ICommand ShotOneTappedCommand { get; private set; }
        public ICommand ShotTwoTappedCommand { get; private set; }

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

            // Initialize new commands that receive the ShotPageFrame instance directly.
            ShotOneTappedCommand = new Command<ShotPageFrame>(OnShotOneTapped);
            ShotTwoTappedCommand = new Command<ShotPageFrame>(OnShotTwoTapped);
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
                    UpdateFrameBackgrounds();
                }
            }
        }

        public int FrameCounter
        {
            get => _frameCounter;
            set
            {
                if (_frameCounter != value)
                {
                    _frameCounter = value;
                    OnPropertyChanged(nameof(_frameCounter));
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

        public string Comment
        {
            get => _comment;
            set
            {
                if (_comment != value)
                {
                    _comment = value;
                    OnPropertyChanged(nameof(Comment));
                }
            }
        }

        // Selected strike ball (bound to Picker.SelectedItem)
        public Ball SelectedStrikeBall
        {
            get => _selectedStrikeBall;
            set
            {
                if (_selectedStrikeBall != value)
                {
                    _selectedStrikeBall = value;
                    StrikeBallId = _selectedStrikeBall?.BallId ?? -1;
                    OnPropertyChanged(nameof(SelectedStrikeBall));
                }
            }
        }

        // Selected spare ball (bound to Picker.SelectedItem)
        public Ball SelectedSpareBall
        {
            get => _selectedSpareBall;
            set
            {
                if (_selectedSpareBall != value)
                {
                    _selectedSpareBall = value;
                    SpareBallId = _selectedSpareBall?.BallId ?? -1;
                    OnPropertyChanged(nameof(SelectedSpareBall));
                }
            }
        }

        public int StrikeBallId
        {
            get => _strikeBallId;
            set
            {
                if (_strikeBallId != value)
                {
                    _strikeBallId = value;
                    OnPropertyChanged(nameof(StrikeBallId));
                }
            }
        }
        public int SpareBallId
        {
            get => _spareBallId;
            set
            {
                if (_spareBallId != value)
                {
                    _spareBallId = value;
                    OnPropertyChanged(nameof(SpareBallId));
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
            // kept old name in case other callers use it; if not, replace with LoadArsenal
            if (UserId == 0)
            {
                Arsenal = new ObservableCollection<Ball>();
                return;
            }

            var arsenalList = await _database.Table<Ball>()
                                             .Where(b => b.UserId == UserId)
                                             .ToListAsync();
            Arsenal = new ObservableCollection<Ball>(arsenalList);
        }

        public async void LoadEditInfo(int shotnum)
        {
            Debug.WriteLine($"Game ID: {gameId} Current frame: {CurrentFrame}");
            var frame = await _database.Table<BowlingFrame>().Where(f => f.GameId == gameId && f.FrameNumber == CurrentFrame).FirstOrDefaultAsync();
            if (frame != null)
            {
                var shot1 = await _database.Table<Shot>().Where(f => f.ShotId == frame.Shot1).FirstOrDefaultAsync();

                if (shot1 != null)
                {
                    if (shot1.LeaveType != null)
                    {
                        pinStates = (short)(shot1.LeaveType);
                        shot1PinStates = pinStates;
                        StrikeBallId = (int)shot1.Ball;
                        Comment = shot1.Comment ?? "";
                        string pins = Convert.ToString((ushort)shot1PinStates, 2).PadLeft(16, '0');
                        Debug.WriteLine($"{pins}");
                    }
                }

                if (shotnum == 2)
                {
                    var shot2 = await _database.Table<Shot>().Where(f => f.ShotId == frame.Shot2).FirstOrDefaultAsync();
                    if (shot2 != null)
                    {
                        if (shot2.LeaveType != null)
                        {
                            pinStates = (short)(shot2.LeaveType);
                            StrikeBallId = (int)shot2.Ball;
                            Comment = shot2.Comment ?? "";
                            string pins = Convert.ToString((ushort)pinStates, 2).PadLeft(16, '0');
                            Debug.WriteLine($"{pins}");
                        }
                    }
                }
            }
           
            AlertEditFrame?.Invoke();
        }

        //Method used to edit frames/shots
        private void OnFrameTapped(ShotPageFrame frame)
        {
            if (frame == null) return;
            if (frame.FrameNumber > FrameCounter) return; //Only allow editing if the frame has a shot recorded

            EditMode = true;
            CurrentFrame = frame.FrameNumber;
            OnPropertyChanged();
        }

        // New: handler for tapping shot 1 box
        private void OnShotOneTapped(ShotPageFrame frame)
        {
            if (frame == null) return;

            // require EditMode as before
            if (!EditMode) return;

            CurrentFrame = frame.FrameNumber;
            CurrentShot = 1;
            LoadEditInfo(1);

            OnPropertyChanged(nameof(CurrentFrame));
            OnPropertyChanged(nameof(CurrentShot));
            OnPropertyChanged(nameof(FrameDisplay));
        }

        // New: handler for tapping shot 2 box
        private void OnShotTwoTapped(ShotPageFrame frame)
        {
            if (frame == null || frame.ShotOneBox == "X") return;

            // require EditMode as before
            if (!EditMode) return;

            CurrentFrame = frame.FrameNumber;
            CurrentShot = 2;
            LoadEditInfo(2);

            OnPropertyChanged(nameof(CurrentFrame));
            OnPropertyChanged(nameof(CurrentShot));
            OnPropertyChanged(nameof(FrameDisplay));
        }

        // Update each ShotPageFrame.BackgroundColor according to CurrentFrame.
        private void UpdateFrameBackgrounds()
        {
            if (frames == null) return;

            foreach (var f in frames)
            {
                // selected frame gets a highlight; others are default white
                f.BackgroundColor = (f.FrameNumber == CurrentFrame)
                    ? (Color.FromArgb("#e89e99"))
                    : Colors.White;
            }
            OnPropertyChanged(nameof(FrameDisplay));
        }
    }

    public class ShotPageFrame : INotifyPropertyChanged
    {
        public int FrameNumber { get; set; }
        public int? RollingScore { get; set; }

        private Color _backgroundColor;

        private ObservableCollection<Color> _pinColors;
        private ObservableCollection<Color> _centerPinColors;
        public ObservableCollection<Color> PinColors
        {
            get => _pinColors;
            set
            {
                if (_pinColors != value)
                {
                    _pinColors = value;
                    OnPropertyChanged(nameof(PinColors));
                }
            }
        }

        public ObservableCollection<Color> CenterPinColors
        {
            get => _centerPinColors;
            set
            {
                if (_centerPinColors != value)
                {
                    _centerPinColors = value;
                    OnPropertyChanged(nameof(CenterPinColors));
                }
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
                    OnPropertyChanged(nameof(ShotOneBox));
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
                    OnPropertyChanged(nameof(ShotTwoBox));
                }
            }
        }

        public Color BackgroundColor
        {
            get => _backgroundColor;
            set
            {
                if (_backgroundColor != value)
                {
                    _backgroundColor = value;
                    OnPropertyChanged(nameof(BackgroundColor));
                }
            }
        }

        public ShotPageFrame(int frameNumber)
        {
            FrameNumber = frameNumber;
            RollingScore = null;
            ShotOneBox = "";
            ShotTwoBox = "";
            _backgroundColor = Colors.White;
            PinColors = new ObservableCollection<Color>(Enumerable.Repeat(Colors.Black, 10));
            CenterPinColors = new ObservableCollection<Color>(Enumerable.Repeat(Colors.Transparent, 10));
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
