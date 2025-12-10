using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Cellular.Data;
using Microsoft.Maui.Storage;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using System.Collections.Generic;
using System.Diagnostics;
using CellularCore; // added to call StatsCalculator

namespace Cellular.ViewModel
{
    internal partial class StatsViewModel : INotifyPropertyChanged
    {
        private readonly SessionRepository _sessionRepo;
        private readonly GameRepository _gameRepo;
        private readonly BallRepository _ballRepo;
        private readonly FrameRepository _frameRepo;
        private readonly ShotRepository _shotRepo;

        public int UserId = Preferences.Get("UserId", 0);

        // Command to clear session selection
        public ICommand ClearSessionCommand { get; }

        // Date range filters (bind from UI)
        private DateTime? _selectedStartDate;
        private DateTime? _selectedEndDate;
        public DateTime? SelectedStartDate
        {
            get => _selectedStartDate;
            set
            {
                if (_selectedStartDate != value)
                {
                    _selectedStartDate = value;
                    OnPropertyChanged();
                    _ = LoadFilteredDataAsync();
                }
            }
        }
        public DateTime? SelectedEndDate
        {
            get => _selectedEndDate;
            set
            {
                if (_selectedEndDate != value)
                {
                    _selectedEndDate = value;
                    OnPropertyChanged();
                    _ = LoadFilteredDataAsync();
                }
            }
        }

        public StatsViewModel()
        {
            var conn = new CellularDatabase().GetConnection();
            _sessionRepo = new SessionRepository(conn);
            _gameRepo = new GameRepository(conn);
            _ballRepo = new BallRepository(conn);
            _frameRepo = new FrameRepository(conn);
            _shotRepo = new ShotRepository(conn);

            // initialize commands
            ClearSessionCommand = new Command(ClearSessionSelection);

            LoadStaticData();
        }

        // --- Collections ---
        public ObservableCollection<string> Lanes { get; } = new();
        public ObservableCollection<string> Frames { get; } = new();

        public ObservableCollection<Session> Sessions { get; } = new();
        public ObservableCollection<Game> Games { get; } = new();
        public ObservableCollection<Event> Events { get; } = new();
        public ObservableCollection<Ball> Balls { get; } = new();
        public ObservableCollection<string> StatTypes { get; } = new();

        // Bowling frames and shots (DB-backed) for stats
        public ObservableCollection<BowlingFrame> BowlingFrames { get; } = new();
        public ObservableCollection<Shot> Shots { get; } = new();

        // --- Selected backing fields ---
        private string _selectedLane;
        private string _selectedFrame;
        private Session _selectedSession;
        private Game _selectedGame;
        private Event _selectedEvent;
        private Ball _selected_ball;
        private string _selectedStatType;

        // Suppress flag used when repopulating Games to avoid binding recursion/null-write that clears saved id
        private bool _suppressSelectedGameSetter = false;

        // Prevent restoration when ClearSessionSelection explicitly clears selection
        private bool _preventRestoreSelectedGame = false;

        // Useful ids for queries
        // -1 indicates "none selected"
        private int _selectedSessionId = -1;
        private int _selectedGameId = -1;
        private int _selectedBallId = -1;
        private int _selectedLaneInt = -1;
        private int _selectedFrameInt = -1;

        public int SelectedSessionId
        {
            get => _selectedSessionId;
            private set
            {
                if (_selectedSessionId != value)
                {
                    _selectedSessionId = value;
                    OnPropertyChanged();
                }
            }
        }

        public int SelectedGameId
        {
            get => _selectedGameId;
            private set
            {
                if (_selectedGameId != value)
                {
                    _selectedGameId = value;
                    OnPropertyChanged();
                }
            }
        }

        public int SelectedBallId
        {
            get => _selectedBallId;
            private set
            {
                if (_selectedBallId != value)
                {
                    _selectedBallId = value;
                    OnPropertyChanged();
                }
            }
        }

        public int SelectedLaneInt
        {
            get => _selectedLaneInt;
            private set
            {
                if (_selectedLaneInt != value)
                {
                    _selectedLaneInt = value;
                    OnPropertyChanged();
                }
            }
        }

        public int SelectedFrameInt
        {
            get => _selectedFrameInt;
            private set
            {
                if (_selectedFrameInt != value)
                {
                    _selectedFrameInt = value;
                    OnPropertyChanged();
                }
            }
        }

        // --- Selected properties (bindings) ---
        public string SelectedLane
        {
            get => _selectedLane;
            set
            {
                if (_selectedLane != value)
                {
                    _selectedLane = value;
                    OnPropertyChanged();
                    // parse to integer or set -1
                    if (int.TryParse(_selectedLane, out var lane))
                        SelectedLaneInt = lane;
                    else
                        SelectedLaneInt = -1;

                    _ = LoadFilteredDataAsync();
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
                    if (int.TryParse(_selectedFrame, out var frame))
                        SelectedFrameInt = frame;
                    else
                        SelectedFrameInt = -1;

                    _ = LoadFilteredDataAsync();
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
                    SelectedSessionId = _selectedSession?.SessionId ?? -1;
                    // refresh everything using the active filters
                    _ = LoadFilteredDataAsync();
                }
            }
        }

        public Game SelectedGame
        {
            get => _selectedGame;
            set
            {
                // Always update backing field so UI reflects current state
                if (_selectedGame != value)
                {
                    _selectedGame = value;

                    // When suppressed we must not change SelectedGameId nor trigger reloads.
                    if (_suppressSelectedGameSetter)
                    {
                        OnPropertyChanged(nameof(SelectedGame));
                        return;
                    }

                    OnPropertyChanged(nameof(SelectedGame));
                    SelectedGameId = _selectedGame?.GameId ?? -1;

                    // When a specific game is selected we still want frames/shots limited to current filters.
                    _ = LoadFilteredDataAsync();
                }
            }
        }

        public Event SelectedEvent
        {
            get => _selectedEvent;
            set
            {
                if (_selectedEvent != value)
                {
                    _selectedEvent = value;
                    OnPropertyChanged();
                    _ = LoadFilteredDataAsync();
                }
            }
        }

        public Ball SelectedBall
        {
            get => _selected_ball;
            set
            {
                if (_selected_ball != value)
                {
                    _selected_ball = value;
                    OnPropertyChanged();
                    SelectedBallId = _selected_ball?.BallId ?? -1;
                    _ = LoadFilteredDataAsync();
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
                    _ = LoadFilteredDataAsync();
                }
            }
        }

        // Clear the session selection and dependent state
        public void ClearSessionSelection()
        {
            SelectedSession = null;
            SelectedSessionId = -1;

            Games.Clear();
            SelectedGame = null;
            SelectedGameId = -1;

            BowlingFrames.Clear();
            Shots.Clear();
            SelectedFrame = null;
            SelectedFrameInt = -1;

            // Reset ball and lane selections so the clear button clears them as well
            SelectedBall = null;
            SelectedBallId = -1;

            SelectedLane = null;
            SelectedLaneInt = -1;

            // Refresh filtered data to ensure UI reflects cleared filters
            _ = LoadFilteredDataAsync();
        }

        // --- Helper Methods ---
        private void LoadStaticData()
        {
            Lanes.Clear();
            for (int i = 1; i <= 32; i++) Lanes.Add(i.ToString());

            Frames.Clear();
            for (int i = 1; i <= 10; i++) Frames.Add(i.ToString());
            Frames.Add("All Frames");

            StatTypes.Clear();
            StatTypes.Add("Default");
        }

        // Public initialization to load DB-backed collections
        public async Task LoadAsync()
        {
            await LoadSessionsAsync(); // respects SelectedStartDate/SelectedEndDate
            await LoadGamesAsync(); // loads all games for user (before session selection)
            await LoadBallsAsync();
        }

        public async Task LoadSessionsAsync()
        {
            Sessions.Clear();
            if (UserId == 0) return;

            // If date filters are present use the date-range repository method, otherwise fall back
            List<Session> sessionsFromDb;
            if (SelectedStartDate.HasValue || SelectedEndDate.HasValue)
            {
                sessionsFromDb = await _sessionRepo.GetSessionsByUserIdAndDateRangeAsync(UserId, SelectedStartDate, SelectedEndDate);
            }
            else
            {
                sessionsFromDb = await _sessionRepo.GetSessionsByUserIdAsync(UserId);
            }

            foreach (var s in sessionsFromDb.OrderByDescending(s => s.SessionNumber))
                Sessions.Add(s);
        }

        public async Task LoadGamesAsync()
        {
            Games.Clear();
            if (UserId == 0) return;

            var gamesFromDb = await _gameRepo.GetGamesByUserIdAsync(UserId);
            foreach (var g in gamesFromDb.OrderByDescending(g => g.GameNumber))
                Games.Add(g);
        }

        public async Task LoadBallsAsync()
        {
            Balls.Clear();
            if (UserId == 0)
            {
                return;
            }

            var ballsFromDb = await _ballRepo.GetBallsByUserIdAsync(UserId);
            foreach (var b in ballsFromDb.OrderBy(b => b.Name))
                Balls.Add(b);
        }

        // Core: load only valid games, frames and shots into the viewmodel based on active filters.
        public async Task LoadFilteredDataAsync()
        {
            if (UserId == 0) return;

            // Track how many sessions we considered for this load so we can log it later
            int sessionsConsideredCount = 0;

            // Capture the game id the UI currently wants selected so we can restore it even if the UI clears SelectedItem during collection updates
            var desiredSelectedGameId = SelectedGameId;

            // Suppress SelectedGame setter side-effects while we clear/repopulate Games to avoid the UI writing null back into the VM and clearing our saved id.
            _suppressSelectedGameSetter = true;
            try
            {
                Games.Clear();
                BowlingFrames.Clear();
                Shots.Clear();

                // Determine sessions to consider
                List<Session> sessionsToConsider;
                if (SelectedSessionId != -1)
                {
                    // prefer already-loaded Sessions collection
                    var s = Sessions.FirstOrDefault(x => x.SessionId == SelectedSessionId);
                    if (s != null)
                        sessionsToConsider = new List<Session> { s };
                    else
                        sessionsToConsider = (await _sessionRepo.GetSessionsByUserIdAndDateRangeAsync(UserId, SelectedStartDate, SelectedEndDate))
                                              .Where(x => x.SessionId == SelectedSessionId).ToList();
                }
                else
                {
                    sessionsToConsider = await _sessionRepo.GetSessionsByUserIdAndDateRangeAsync(UserId, SelectedStartDate, SelectedEndDate);
                }

                // capture count for logging
                sessionsConsideredCount = sessionsToConsider?.Count ?? 0;

                // iterate sessions -> games -> frames -> shots, applying filters at each step
                await _frameRepo.InitAsync();
                await _shotRepo.InitAsync();

                foreach (var session in sessionsToConsider)
                {
                    // Get games for session (respect frame filter if set)
                    List<Game> gamesForSession;
                    if (SelectedGameId != -1)
                    {
                        // explicit single game selected
                        var single = (await _gameRepo.GetGamesListBySessionAsync(session.SessionId, UserId)).FirstOrDefault(g => g.GameId == SelectedGameId);
                        gamesForSession = single != null ? new List<Game> { single } : new List<Game>();
                    }
                    else if (SelectedFrameInt > 0)
                    {
                        gamesForSession = await _gameRepo.GetGamesBySessionAndFrameNumberAsync(session.SessionId, SelectedFrameInt);
                    }
                    else
                    {
                        gamesForSession = await _gameRepo.GetGamesListBySessionAsync(session.SessionId, UserId);
                    }

                    foreach (var game in gamesForSession)
                    {
                        if (!GameMatchesLaneFilter(game, SelectedLaneInt))
                            continue;

                        // Add game to viewmodel (only valid games after lane/frame filters)
                        Games.Add(game);

                        // load frames for game
                        var frameIds = await _frameRepo.GetFrameIdsByGameIdAsync(game.GameId);
                        foreach (var fid in frameIds)
                        {
                            var frame = await _frameRepo.GetFrameById(fid);
                            if (frame == null) continue;

                            if (SelectedFrameInt > 0 && (frame.FrameNumber ?? -1) != SelectedFrameInt)
                                continue;

                            BowlingFrames.Add(frame);

                            // load shots for this frame and apply ball and other filters
                            var shotIds = await _frameRepo.GetShotIdsByFrameIdAsync(frame.FrameId);
                            foreach (var sid in shotIds)
                            {
                                var shot = await _shotRepo.GetShotById(sid);
                                if (shot == null) continue;

                                if (SelectedBallId != -1 && (shot.Ball ?? -1) != SelectedBallId)
                                    continue;

                                // stat type or event filters could be applied here as needed
                                Shots.Add(shot);
                            }
                        }
                    }
                }

                // Restore SelectedGame to the instance in the newly-populated Games collection using the captured id
                if (desiredSelectedGameId != -1)
                {
                    var restored = Games.FirstOrDefault(g => g.GameId == desiredSelectedGameId);
                    if (restored != null)
                    {
                        _selectedGame = restored;
                        // keep SelectedGameId in sync with restored instance
                        SelectedGameId = restored.GameId;
                    }
                }
            }
            finally
            {
                // Re-enable SelectedGame setter behavior and notify UI.
                _suppressSelectedGameSetter = false;
                OnPropertyChanged(nameof(Games));
                OnPropertyChanged(nameof(BowlingFrames));
                OnPropertyChanged(nameof(Shots));
                OnPropertyChanged(nameof(SelectedGame));

                // Log counts so we can see how many items were loaded for this call.
                Debug.WriteLine($"LoadFilteredDataAsync: sessions={sessionsConsideredCount}, games={Games.Count}, frames={BowlingFrames.Count}, shots={Shots.Count}");

                // Call the StatsCalculator and pass the frames retrieved by the query.
                // The calculator method returns a double in [0,1]; you said you'll implement the math inside CalculateStrikePercentage.
                
                var strikePercentage = CalculateStrikePercentage(BowlingFrames);
                var sparePercentage = CalculateSparePercentage(BowlingFrames);
                Debug.WriteLine($"Calculated strike percentage: {strikePercentage:P2}");
                Debug.WriteLine($"Calculated spare percentage: {sparePercentage:P2}");
            }
        }

        // Helper: check if a game matches the lane filter.
        // Accepts game.StartingLane or parses game.Lanes string for a matching number.
        private bool GameMatchesLaneFilter(Game game, int laneFilter)
        {
            if (laneFilter <= 0) return true;

            if (game.StartingLane.HasValue && game.StartingLane.Value == laneFilter) return true;

            if (!string.IsNullOrWhiteSpace(game.Lanes))
            {
                // try simple parsing: split on common delimiters and compare numbers
                var tokens = game.Lanes.Split(new[] { ',', ';', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var t in tokens)
                {
                    if (int.TryParse(t, out var n) && n == laneFilter) return true;
                }

                // try range like "1-3"
                foreach (var t in tokens)
                {
                    var parts = t.Split('-', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 && int.TryParse(parts[0], out var a) && int.TryParse(parts[1], out var b))
                    {
                        if (laneFilter >= Math.Min(a, b) && laneFilter <= Math.Max(a, b)) return true;
                    }
                }
            }

            return false;
        }

        //Stat calculations
        public static double CalculateStrikePercentage(IEnumerable<BowlingFrame> frames)
        {
            if (frames == null) return 0.0;

            int totalFrames = frames.Count();
            int strikes = frames.Count(f => f.Result == "Strike");
            Debug.WriteLine($"Strikes: {strikes}");
            return StatsCalculator.CalculatePercentage(strikes, totalFrames);
        }

        public static double CalculateSparePercentage(IEnumerable<BowlingFrame> frames)
        {
            if (frames == null) return 0.0;

            int totalFrames = frames.Count();
            int spares = frames.Count(f => f.Result == "Spare");
            return StatsCalculator.CalculatePercentage(spares, totalFrames);
        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}