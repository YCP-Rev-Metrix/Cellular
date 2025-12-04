using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Cellular.Data;
using SQLite;
using Microsoft.Maui.Storage;

namespace Cellular.ViewModel
{
    internal partial class StatsViewModel : INotifyPropertyChanged
    {
        private readonly SessionRepository _sessionRepo;
        private readonly GameRepository _gameRepo;
        private readonly BallRepository _ballRepo;

        public int UserId = Preferences.Get("UserId", 0);

        public StatsViewModel()
        {
            var conn = new CellularDatabase().GetConnection();
            _sessionRepo = new SessionRepository(conn);
            _gameRepo = new GameRepository(conn);
            _ballRepo = new BallRepository(conn);

            LoadStaticData();
        }

        // --- Collections --- (typed so we can bind SelectedItem to real objects)
        public ObservableCollection<string> Lanes { get; } = new();
        public ObservableCollection<string> Frames { get; } = new();

        public ObservableCollection<Session> Sessions { get; } = new();
        public ObservableCollection<Game> Games { get; } = new();
        public ObservableCollection<Event> Events { get; } = new();
        public ObservableCollection<Ball> Balls { get; } = new();
        public ObservableCollection<string> StatTypes { get; } = new();

        // --- Selected Properties (Bindings) ---
        private DateTime minDate = new DateTime(1900, 1, 1);
        private DateTime maxDate = DateTime.Now.AddYears(1);
        private DateTime selectedStartDate;
        private DateTime selectedEndDate;

        private string _selectedLane;
        private string _selectedFrame;
        private Session _selectedSession;
        private Game _selectedGame;
        private Event _selectedEvent;
        private Ball _selectedBall;
        private string _selectedStatType;

        // Public selected properties for binding
        public string SelectedLane
        {
            get => _selectedLane;
            set
            {
                if (_selectedLane != value)
                {
                    _selectedLane = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SelectedFrame
        {
            get => _selectedFrame;
            set
            {
                if (_selectedFrame != value)
                {
                    _selectedFrame = value;
                    OnPropertyChanged();
                }
            }
        }

        public Session SelectedSession
        {
            get => _selectedSession;
            set
            {
                if (_selectedSession != value)
                {
                    _selectedSession = value;
                    OnPropertyChanged();
                    // When a session is selected, load games for that session only
                    _ = LoadGamesForSessionAsync(_selectedSession?.SessionId);
                }
            }
        }

        public Game SelectedGame
        {
            get => _selectedGame;
            set
            {
                if (_selectedGame != value)
                {
                    _selectedGame = value;
                    OnPropertyChanged();
                }
            }
        }

        public Ball SelectedBall
        {
            get => _selectedBall;
            set
            {
                if (_selectedBall != value)
                {
                    _selectedBall = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SelectedStatType
        {
            get => _selectedStatType;
            set
            {
                if (_selectedStatType != value)
                {
                    _selectedStatType = value;
                    OnPropertyChanged();
                }
            }
        }

        // --- Helper Methods ---
        private void LoadStaticData()
        {
            // Populate Lanes 1 - 32
            Lanes.Clear();
            for (int i = 1; i <= 32; i++)
            {
                Lanes.Add(i.ToString());
            }

            // Populate Frames 1 - 10
            Frames.Clear();
            for (int i = 1; i <= 10; i++)
            {
                Frames.Add(i.ToString());
            }
            Frames.Add("All Frames");

            // Static Stat Types
            StatTypes.Clear();
            StatTypes.Add("Default");
        }

        // Public initialization to load DB-backed collections
        public async Task LoadAsync()
        {
            await LoadSessionsAsync();
            await LoadGamesAsync();      // loads all games for user (before any session selection)
            await LoadBallsAsync();
        }

        public async Task LoadSessionsAsync()
        {
            Sessions.Clear();
            if (UserId == 0) return;

            var sessionsFromDb = await _sessionRepo.GetSessionsByUserIdAsync(UserId);
            foreach (var s in sessionsFromDb.OrderByDescending(s => s.SessionNumber))
            {
                Sessions.Add(s);
            }
        }

        public async Task LoadGamesAsync()
        {
            Games.Clear();
            if (UserId == 0) return;

            var gamesFromDb = await _gameRepo.GetGamesByUserIdAsync(UserId);
            foreach (var g in gamesFromDb.OrderByDescending(g => g.GameNumber))
            {
                Games.Add(g);
            }
        }

        // Load games filtered by a session id (called when a session is selected)
        public async Task LoadGamesForSessionAsync(int? sessionId)
        {
            Games.Clear();
            if (sessionId == null || UserId == 0) return;

            var gamesFromDb = await _gameRepo.GetGamesListBySessionAsync(sessionId.Value, UserId);
            foreach (var g in gamesFromDb.OrderBy(g => g.GameNumber))
            {
                Games.Add(g);
            }

            // Optionally clear selection when switching session
            SelectedGame = null;
        }

        public async Task LoadBallsAsync()
        {
            Balls.Clear();
            if (UserId == 0) return;

            var ballsFromDb = await _ballRepo.GetBallsByUserIdAsync(UserId);
            foreach (var b in ballsFromDb.OrderBy(b => b.Name))
            {
                Balls.Add(b);
            }
        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}