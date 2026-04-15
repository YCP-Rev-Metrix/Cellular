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
        private readonly EventRepository _eventRepo;
        private readonly GameRepository _gameRepo;
        private readonly BallRepository _ballRepo;
        private readonly FrameRepository _frameRepo;
        private readonly ShotRepository _shotRepo;
        private readonly EstablishmentRepository _estabRepo;

        public int UserId = Preferences.Get("UserId", 0);

        // Command to clear session selection
        public ICommand ClearSessionCommand { get; }

        // Nullable filter values used by the query engine.
        // Null = no date boundary on that side.
        private DateTime? _selectedStartDate;
        private DateTime? _selectedEndDate;
        public DateTime? SelectedStartDate
        {
            get => _selectedStartDate;
            set { if (_selectedStartDate != value) { _selectedStartDate = value; OnPropertyChanged(); } }
        }
        public DateTime? SelectedEndDate
        {
            get => _selectedEndDate;
            set { if (_selectedEndDate != value) { _selectedEndDate = value; OnPropertyChanged(); } }
        }

        // Non-nullable display properties that DatePicker.Date binds to (DatePicker.Date is DateTime, not DateTime?).
        // Changing these updates the nullable filter properties above.
        // Defaults: past year → today, so the pickers open at a sensible range.
        private DateTime _startDateDisplay = DateTime.Today.AddYears(-1);
        private DateTime _endDateDisplay   = DateTime.Today;

        public DateTime StartDateDisplay
        {
            get => _startDateDisplay;
            set
            {
                if (_startDateDisplay != value)
                {
                    _startDateDisplay = value;
                    OnPropertyChanged();
                    SelectedStartDate = value;
                }
            }
        }

        public DateTime EndDateDisplay
        {
            get => _endDateDisplay;
            set
            {
                if (_endDateDisplay != value)
                {
                    _endDateDisplay = value;
                    OnPropertyChanged();
                    SelectedEndDate = value;
                }
            }
        }

        /// <summary>Resets the date pickers to their defaults and clears the nullable filter values.</summary>
        public void ResetDates()
        {
            _selectedStartDate = null;
            _selectedEndDate   = null;
            OnPropertyChanged(nameof(SelectedStartDate));
            OnPropertyChanged(nameof(SelectedEndDate));

            // Updating the display properties will re-fire the setters which would set the nullables
            // back to a date, so set the backing fields directly and just notify the UI.
            _startDateDisplay = DateTime.Today.AddYears(-1);
            _endDateDisplay   = DateTime.Today;
            OnPropertyChanged(nameof(StartDateDisplay));
            OnPropertyChanged(nameof(EndDateDisplay));
        }

        public StatsViewModel()
        {
            var conn = new CellularDatabase().GetConnection();
            _sessionRepo = new SessionRepository(conn);
            _eventRepo = new EventRepository(conn);
            _gameRepo = new GameRepository(conn);
            _ballRepo = new BallRepository(conn);
            _frameRepo = new FrameRepository(conn);
            _shotRepo = new ShotRepository(conn);
            _estabRepo = new EstablishmentRepository(conn);

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
        public ObservableCollection<Establishment> Establishments { get; } = new();
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
        private Establishment _selectedEstablishment;
        private string _selectedStatType;

        // --- Stat results (all types) ---
        public double StrikePercent { get; set; }
        public double SparePercent { get; set; }
        public double OpenPercent { get; set; }
        public double SplitPercent { get; set; }
        public double WashoutPercent { get; set; }
        public double GameAverage { get; set; }

        // Game-type stats
        public int HighGame { get; set; }
        public int LowGame { get; set; }
        public int GamesPlayed { get; set; }

        // Strike-type stats
        public int TotalStrikes { get; set; }
        public double StrikesPerGame { get; set; }
        public double CarryPercent { get; set; }    // 
        public double PocketPercent { get; set; }   // 
        public double CountAverage { get; set; }    // avg pins knocked down on first ball
        public int StrikeAttempts { get; set; }     // total frames attempted including fill balls (up to 12)

        // Pocket position counts (first-ball shots, all frames)
        public int PocketCount { get; set; }        // position == "pocket" exactly
        public int HighPocketCount { get; set; }    // position == "high pocket" exactly
        public int LightPocketCount { get; set; }   // position == "light pocket" exactly
        public int TotalPocketCount { get; set; }   // pocket + high pocket + light pocket
        public int BNLDCount { get; set; }          // position contains "BNLD"

        // Spare-type stats
        public double SpareConversionRate { get; set; }
        public int TotalSpares { get; set; }
        public int TotalOpens { get; set; }

        // Type visibility helpers
        public bool IsGameType => _selectedStatType == "Game";
        public bool IsStrikeType => _selectedStatType == "Strike";
        public bool IsSpareType => _selectedStatType == "Spare";

        /// <summary>
        /// Human-readable summary of every active filter, built when stats are loaded.
        /// Only includes criteria that are actually set so the popup stays clean.
        /// </summary>
        public string FilterSummary { get; private set; } = string.Empty;

        private void BuildFilterSummary()
        {
            var sb = new System.Text.StringBuilder();

            // Date range
            bool hasStart = _selectedStartDate.HasValue;
            bool hasEnd   = _selectedEndDate.HasValue;
            if (hasStart && hasEnd)
                sb.AppendLine($"Dates: {_selectedStartDate!.Value:MM/dd/yy} – {_selectedEndDate!.Value:MM/dd/yy}");
            else if (hasStart)
                sb.AppendLine($"Dates: {_selectedStartDate!.Value:MM/dd/yy} – All");
            else if (hasEnd)
                sb.AppendLine($"Dates: All – {_selectedEndDate!.Value:MM/dd/yy}");
            else
                sb.AppendLine("Dates: All");

            sb.AppendLine($"Event: {_selectedEvent?.NickName ?? _selectedEvent?.LongName ?? "All"}");
            sb.AppendLine($"Session: {_selectedSession?.DisplayName ?? (_selectedSession != null ? _selectedSession.SessionNumber.ToString() : "All")}");
            sb.AppendLine($"Game: {(_selectedGame != null ? _selectedGame.GameNumber.ToString() : "All")}");
            sb.AppendLine($"Ball: {_selected_ball?.Name ?? "All"}");
            sb.AppendLine($"House: {_selectedEstablishment?.NickName ?? "All"}");
            sb.AppendLine($"Lane: {(_selectedLaneInt > 0 ? _selectedLaneInt.ToString() : "All")}");
            sb.AppendLine($"Frame: {(_selectedFrameInt > 0 ? _selectedFrameInt.ToString() : "All")}");
            sb.AppendLine($"Type: {(_selectedStatType ?? "All")}");
            sb.Append($"({GamesPlayed} game{(GamesPlayed == 1 ? "" : "s")})");

            FilterSummary = sb.ToString().TrimEnd();
            OnPropertyChanged(nameof(FilterSummary));
        }

        // Suppress flags used when repopulating collections to avoid binding recursion/null-write that clears saved ids
        private bool _suppressSelectedGameSetter = false;
        private bool _suppressSelectedSessionSetter = false;

        // Prevent restoration when ClearSessionSelection explicitly clears selection
        private bool _preventRestoreSelectedGame = false;

        // Useful ids for queries
        // -1 indicates "none selected"
        private int _selectedSessionId = -1;
        private int _selectedEventId = -1;
        private int _selectedGameId = -1;
        private int _selectedBallId = -1;
        private int _selectedEstablishmentId = -1;
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

        public int SelectedEventId
        {
            get => _selectedEventId;
            private set
            {
                if (_selectedEventId != value)
                {
                    _selectedEventId = value;
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

        public int SelectedEstablishmentId
        {
            get => _selectedEstablishmentId;
            private set
            {
                if (_selectedEstablishmentId != value)
                {
                    _selectedEstablishmentId = value;
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

                    // When suppressed (Sessions collection is being repopulated) do not update the
                    // id or trigger a reload — the load already in progress will restore us.
                    if (_suppressSelectedSessionSetter)
                    {
                        OnPropertyChanged(nameof(SelectedSession));
                        return;
                    }

                    OnPropertyChanged(nameof(SelectedSession));
                    SelectedSessionId = _selectedSession?.SessionId ?? -1;
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
                    SelectedEventId = _selectedEvent?.EventId ?? -1;
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

        public Establishment SelectedEstablishment
        {
            get => _selectedEstablishment;
            set
            {
                if (_selectedEstablishment != value)
                {
                    _selectedEstablishment = value;
                    OnPropertyChanged();
                    SelectedEstablishmentId = _selectedEstablishment?.EstaID ?? -1;
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
                    OnPropertyChanged(nameof(IsGameType));
                    OnPropertyChanged(nameof(IsStrikeType));
                    OnPropertyChanged(nameof(IsSpareType));
                    _ = LoadFilteredDataAsync();
                }
            }
        }

        // Clear the session selection and dependent state
        public void ClearSessionSelection()
        {
            // Block the guard so individual setter-triggered loads don't fire while we're
            // resetting everything — we'll do one clean load at the end.
            _isLoadingFilteredData = true;
            try
            {
                ResetDates();

                _selectedSession = null;
                _selectedSessionId = -1;
                OnPropertyChanged(nameof(SelectedSession));
                OnPropertyChanged(nameof(SelectedSessionId));

                _selectedEvent = null;
                _selectedEventId = -1;
                OnPropertyChanged(nameof(SelectedEvent));
                OnPropertyChanged(nameof(SelectedEventId));

                Games.Clear();
                _selectedGame = null;
                _selectedGameId = -1;
                OnPropertyChanged(nameof(SelectedGame));
                OnPropertyChanged(nameof(SelectedGameId));

                BowlingFrames.Clear();
                Shots.Clear();

                _selectedFrame = null;
                _selectedFrameInt = -1;
                OnPropertyChanged(nameof(SelectedFrame));
                OnPropertyChanged(nameof(SelectedFrameInt));

                _selected_ball = null;
                _selectedBallId = -1;
                OnPropertyChanged(nameof(SelectedBall));
                OnPropertyChanged(nameof(SelectedBallId));

                _selectedEstablishment = null;
                _selectedEstablishmentId = -1;
                OnPropertyChanged(nameof(SelectedEstablishment));
                OnPropertyChanged(nameof(SelectedEstablishmentId));

                _selectedLane = null;
                _selectedLaneInt = -1;
                OnPropertyChanged(nameof(SelectedLane));
                OnPropertyChanged(nameof(SelectedLaneInt));
            }
            finally
            {
                _isLoadingFilteredData = false;
            }

            // One reload with all filters cleared — also restores Sessions to the full list
            // because SelectedEventId == -1 triggers LoadSessionsAsync path.
            _ = LoadAsync();
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
            StatTypes.Add("Game");
            StatTypes.Add("Strike");
            StatTypes.Add("Spare");
        }

        // Public initialization to load DB-backed collections
        public async Task LoadAsync()
        {
            await LoadEventsAsync();          // must be first so session DisplayNames can resolve event nick-names
            await LoadSessionsAsync();        // respects SelectedStartDate/SelectedEndDate
            await LoadGamesAsync();           // loads all games for user (before session selection)
            await LoadBallsAsync();
            await LoadEstablishmentsAsync();
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
            {
                // Resolve the event nick-name for the display label.
                // Events is populated by LoadEventsAsync() which runs before LoadSessionsAsync() in LoadAsync().
                var nickName = Events.FirstOrDefault(e => e.EventId == s.EventId)?.NickName ?? string.Empty;
                s.BuildDisplayName(nickName);
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
                // Skip if we already have this exact game id or a game with the same game number
                // (prevents duplicate display entries like "1, 1, 2, 2" when multiple sessions
                // contain games with the same game number).
                if (!Games.Any(x => x.GameId == g.GameId || x.GameNumber == g.GameNumber))
                    Games.Add(g);
            }
        }

        public async Task LoadEventsAsync()
        {
            Events.Clear();
            if (UserId == 0) return;

            var eventsFromDb = await _eventRepo.GetEventsByUserIdAsync(UserId);
            foreach (var e in eventsFromDb.OrderBy(e => e.EventId))
                Events.Add(e);
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

        public async Task LoadEstablishmentsAsync()
        {
            Establishments.Clear();
            if (UserId == 0) return;

            var estabsFromDb = await _estabRepo.GetEstablishmentsByUserIdAsync(UserId);
            foreach (var e in estabsFromDb.Where(e => e.Enabled).OrderBy(e => e.NickName))
                Establishments.Add(e);
        }

        // Re-entrancy guard: prevents a second call from starting while one is already running.
        private bool _isLoadingFilteredData = false;

        // Core: load only valid games, frames and shots into the viewmodel based on active filters.
        public async Task LoadFilteredDataAsync()
        {
            if (UserId == 0) return;
            if (_isLoadingFilteredData) return;
            _isLoadingFilteredData = true;

            // Track how many sessions we considered for this load so we can log it later
            int sessionsConsideredCount = 0;

            // Capture the ids the UI currently wants selected so we can restore them
            // even if clearing/repopulating the bound collections causes the UI to write null back.
            var desiredSelectedGameId = SelectedGameId;
            var desiredSelectedEventId = SelectedEventId;
            var desiredSelectedSessionId = SelectedSessionId;

            // Suppress setter side-effects while repopulating bound collections.
            _suppressSelectedGameSetter = true;
            _suppressSelectedSessionSetter = true;
            try
            {
                // Events loaded separately (LoadAsync / LoadEventsAsync). Do not reload here to avoid
                // clearing the user's selection on every filter change.

                Games.Clear();
                BowlingFrames.Clear();
                Shots.Clear();

                // Determine sessions to consider
                List<Session> sessionsToConsider;
                // If an event is selected, prefer sessions under that event
                if (SelectedEventId != -1)
                {
                    var sessionsForEvent = await _sessionRepo.GetSessionsByUserIdAndEventAsync(UserId, SelectedEventId);

                    // apply optional date range filter
                    if (SelectedStartDate.HasValue || SelectedEndDate.HasValue)
                    {
                        sessionsForEvent = sessionsForEvent.Where(s => s.DateTime.HasValue)
                                                           .Where(s => (!SelectedStartDate.HasValue || s.DateTime.Value.Date >= SelectedStartDate.Value.Date) &&
                                                                       (!SelectedEndDate.HasValue || s.DateTime.Value.Date <= SelectedEndDate.Value.Date))
                                                           .ToList();
                    }

                    // If a specific session id is selected, prefer that one (still scoped to event)
                    if (SelectedSessionId != -1)
                    {
                        var s = sessionsForEvent.FirstOrDefault(x => x.SessionId == SelectedSessionId);
                        sessionsToConsider = s != null ? new List<Session> { s } : new List<Session>();
                    }
                    else
                    {
                        sessionsToConsider = sessionsForEvent;
                    }
                }
                else
                {
                    // No event constraint: fall back to selected-session or date-range selection
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
                }

                // Apply house (establishment) filter if selected
                if (SelectedEstablishmentId != -1)
                {
                    sessionsToConsider = sessionsToConsider
                        .Where(s => s.Establishment.HasValue && s.Establishment.Value == SelectedEstablishmentId)
                        .ToList();
                }

                // capture count for logging
                sessionsConsideredCount = sessionsToConsider?.Count ?? 0;

                // Only update the Sessions picker list when an event filter is active (so clicking an
                // event narrows the Sessions picker to that event's sessions).
                // When the user picks a session directly — with no event selected — leave the full
                // Sessions list intact: clearing it here (a) collapses the picker to a single entry
                // and (b) causes MAUI to immediately write null back through the two-way SelectedItem
                // binding, which previously caused the freeze.
                if (SelectedEventId != -1)
                {
                    Sessions.Clear();
                    foreach (var sess in sessionsToConsider.OrderByDescending(s => s.SessionNumber))
                    {
                        var nickName = Events.FirstOrDefault(e => e.EventId == sess.EventId)?.NickName ?? string.Empty;
                        sess.BuildDisplayName(nickName);
                        Sessions.Add(sess);
                    }
                }

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
                        // Prevent duplicate display entries: skip if same GameId or same GameNumber already present
                        if (!Games.Any(g => g.GameId == game.GameId || g.GameNumber == game.GameNumber))
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

                // Restore SelectedSession to the instance in the newly-populated Sessions collection
                if (desiredSelectedSessionId != -1)
                {
                    var restoredSess = Sessions.FirstOrDefault(s => s.SessionId == desiredSelectedSessionId);
                    if (restoredSess != null)
                    {
                        _selectedSession = restoredSess;
                        SelectedSessionId = restoredSess.SessionId;
                        OnPropertyChanged(nameof(SelectedSession));
                    }
                }

                // Restore SelectedGame to the instance in the newly-populated Games collection
                if (desiredSelectedGameId != -1)
                {
                    var restored = Games.FirstOrDefault(g => g.GameId == desiredSelectedGameId);
                    if (restored != null)
                    {
                        _selectedGame = restored;
                        SelectedGameId = restored.GameId;
                    }
                }

                // Restore SelectedEvent to the instance in the newly-populated Events collection
                if (desiredSelectedEventId != -1)
                {
                    var restoredEv = Events.FirstOrDefault(ev => ev.EventId == desiredSelectedEventId);
                    if (restoredEv != null)
                    {
                        _selectedEvent = restoredEv;
                        SelectedEventId = restoredEv.EventId;
                        OnPropertyChanged(nameof(SelectedEvent));
                    }
                }
            }
            finally
            {
                _isLoadingFilteredData = false;

                // Re-enable setter behavior and notify UI.
                _suppressSelectedGameSetter = false;
                _suppressSelectedSessionSetter = false;
                OnPropertyChanged(nameof(Games));
                OnPropertyChanged(nameof(Sessions));
                OnPropertyChanged(nameof(BowlingFrames));
                OnPropertyChanged(nameof(Shots));
                OnPropertyChanged(nameof(SelectedGame));
                OnPropertyChanged(nameof(SelectedSession));

                // Log counts so we can see how many items were loaded for this call.
                Debug.WriteLine($"LoadFilteredDataAsync: sessions={sessionsConsideredCount}, games={Games.Count}, frames={BowlingFrames.Count}, shots={Shots.Count}");

                // Call the StatsCalculator and pass the frames retrieved by the query.
                // The calculator method returns a double in [0,1]; you said you'll implement the math inside CalculateStrikePercentage.
                
                StrikePercent = CalculateStrikePercentage(BowlingFrames);
                SparePercent = CalculateSparePercentage(BowlingFrames);
                OpenPercent = CalculateOpenPercentage(BowlingFrames);
                SplitPercent = CalculateSplitPercentage(Shots);
                WashoutPercent = CalculateWashoutPercentage(Shots);
                GameAverage = CalculateScoreAverage(Games);

                // Game-type stats — unwrap nullable Score and ignore unscored (0 / null) games
                var scores = Games
                    .Where(g => g.Score.HasValue && g.Score.Value > 0)
                    .Select(g => g.Score.Value)
                    .ToList(); // List<int>
                GamesPlayed = scores.Count;
                HighGame    = scores.Any() ? scores.Max() : 0;
                LowGame     = scores.Any() ? scores.Min() : 0;

                // Strike-type stats
                TotalStrikes   = BowlingFrames.Count(f => f.Result == "Strike");
                StrikesPerGame = GamesPlayed > 0 ? (double)TotalStrikes / GamesPlayed : 0.0;
                StrikeAttempts = BowlingFrames.Count; // all frames including fill balls (up to 12)

                // Build a lookup of ShotId → Shot so we can inspect Shot1 positions per frame.
                var shotById = Shots.Where(s => s.ShotId > 0)
                                    .GroupBy(s => s.ShotId)
                                    .ToDictionary(g => g.Key, g => g.First());

                // Position counts — first-ball shots across all frames (including fill balls)
                PocketCount = BowlingFrames.Count(f =>
                    f.Shot1.HasValue &&
                    shotById.TryGetValue(f.Shot1.Value, out var s1) &&
                    string.Equals(s1.Position, "pocket", StringComparison.OrdinalIgnoreCase));

                HighPocketCount = BowlingFrames.Count(f =>
                    f.Shot1.HasValue &&
                    shotById.TryGetValue(f.Shot1.Value, out var s1) &&
                    string.Equals(s1.Position, "h-pocket", StringComparison.OrdinalIgnoreCase));

                LightPocketCount = BowlingFrames.Count(f =>
                    f.Shot1.HasValue &&
                    shotById.TryGetValue(f.Shot1.Value, out var s1) &&
                    string.Equals(s1.Position, "l-pocket", StringComparison.OrdinalIgnoreCase));

                TotalPocketCount = PocketCount + HighPocketCount + LightPocketCount;

                BNLDCount = BowlingFrames.Count(f =>
                    // 1. Ensure Shot 1 exists
                    f.Shot1.HasValue &&
                    shotById.TryGetValue(f.Shot1.Value, out var s1) &&
                    // 2. It must be a Strike
                    s1.Count == 10 &&
                    // 3. It must NOT be a pocket hit
                    // (This excludes "pocket", "h-pocket", and "l-pocket")
                    s1.Position != null &&
                    !s1.Position.Contains("pocket", StringComparison.OrdinalIgnoreCase));

                CountAverage = CalculateCountAverage(Shots);

                PocketPercent = (double)TotalPocketCount / StrikeAttempts;
                CarryPercent = (double)(TotalStrikes - BNLDCount) / TotalPocketCount;

                Debug.WriteLine($"Pocket: {PocketPercent}, Carry: {CarryPercent}");

                // Spare-type stats
                TotalSpares = BowlingFrames.Count(f => f.Result == "Spare");
                TotalOpens = BowlingFrames.Count(f => f.Result == "Open");
                var spareAttempts = TotalSpares + TotalOpens;
                SpareConversionRate = spareAttempts > 0
                    ? StatsCalculator.CalculatePercentage(TotalSpares, spareAttempts)
                    : 0.0;

                OnPropertyChanged(nameof(StrikePercent));
                OnPropertyChanged(nameof(SparePercent));
                OnPropertyChanged(nameof(OpenPercent));
                OnPropertyChanged(nameof(SplitPercent));
                OnPropertyChanged(nameof(WashoutPercent));
                OnPropertyChanged(nameof(GameAverage));
                OnPropertyChanged(nameof(HighGame));
                OnPropertyChanged(nameof(LowGame));
                OnPropertyChanged(nameof(GamesPlayed));
                OnPropertyChanged(nameof(TotalStrikes));
                OnPropertyChanged(nameof(StrikesPerGame));
                OnPropertyChanged(nameof(StrikeAttempts));
                OnPropertyChanged(nameof(PocketCount));
                OnPropertyChanged(nameof(HighPocketCount));
                OnPropertyChanged(nameof(LightPocketCount));
                OnPropertyChanged(nameof(TotalPocketCount));
                OnPropertyChanged(nameof(BNLDCount));
                OnPropertyChanged(nameof(CarryPercent));
                OnPropertyChanged(nameof(PocketPercent));
                OnPropertyChanged(nameof(CountAverage));
                OnPropertyChanged(nameof(TotalSpares));
                OnPropertyChanged(nameof(TotalOpens));
                OnPropertyChanged(nameof(SpareConversionRate));
                OnPropertyChanged(nameof(IsGameType));
                OnPropertyChanged(nameof(IsStrikeType));
                OnPropertyChanged(nameof(IsSpareType));

                BuildFilterSummary();
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

        public static double CalculateOpenPercentage(IEnumerable<BowlingFrame> frames)
        {
            if (frames == null) return 0.0;

            int totalFrames = frames.Count();
            int opens = frames.Count(f => f.Result == "Open");
            return StatsCalculator.CalculatePercentage(opens, totalFrames);
        }

        public static double CalculateSplitPercentage(IEnumerable<Shot> shots)
        {
            if (shots == null || !shots.Any()) return 0.0;

            // The 13th bit (Split) = 2^12 = 4096
            const short SplitMask = 4096;

            // We only count Shot 1 because a split can't happen on Shot 2
            var shot1Only = shots.Where(s => s.ShotNumber == 1).ToList();

            if (!shot1Only.Any()) return 0.0;

            int totalShot1s = shot1Only.Count;
            int splitCount = shot1Only.Count(s => (s.LeaveType & SplitMask) != 0);

            return StatsCalculator.CalculatePercentage(splitCount, totalShot1s);
        }

        public static double CalculateWashoutPercentage(IEnumerable<Shot> shots)
        {
            if (shots == null) return 0.0;

            // Filter to only include the first shot of each frame
            var firstShots = shots.Where(s => s.ShotNumber == 1).ToList();
            if (!firstShots.Any()) return 0.0;

            // Bit 14 (Washout) = 2^13 = 8192
            const short WashoutMask = 8192;

            int totalFirstShots = firstShots.Count;
            int washoutCount = firstShots.Count(s => (s.LeaveType & WashoutMask) != 0);

            return StatsCalculator.CalculatePercentage(washoutCount, totalFirstShots);
        }

        public static double CalculateScoreAverage(IEnumerable<Game> games)
        {
            if (games == null) return 0.0;
            var scores = games.Select(game => game.Score);
            return StatsCalculator.CalculateAverage(scores);
        }


        /// <summary>
        /// Count average = average pins knocked down on first-ball shots.
        /// Uses Shot.Count where ShotNumber == 1.
        /// </summary>
        public static double CalculateCountAverage(IEnumerable<Shot> shots)
        {
            if (shots == null) return 0.0;

            var counts = shots
                .Where(s => s.ShotNumber == 1 && s.Count.HasValue)
                .Select(s => (int)s.Count!)
                .ToList();

            return counts.Any() ? counts.Average() : 0.0;
        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}