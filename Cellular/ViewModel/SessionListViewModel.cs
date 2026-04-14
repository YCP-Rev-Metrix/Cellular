using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cellular.Data;
using static CellularCore.DateTimeCalculator;
using SQLite;
using static System.Collections.Specialized.BitVector32;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Storage;
using Microsoft.Extensions.Logging;
using CellularCore;

namespace Cellular.ViewModel
{
    internal partial class SessionListViewModel : INotifyPropertyChanged
    {
        private readonly SessionRepository _SessionRepository;
        private readonly GameRepository _GameRepository;
        private readonly EventRepository _EventRepository;
        private readonly EstablishmentRepository _EstablishmentRepository;

        public ObservableCollection<Session> Sessions { get; set; }
        public ObservableCollection<Game> Games { get; set; }

        // New collections for binding pickers
        public ObservableCollection<Event> Events { get; set; }
        public ObservableCollection<Establishment> Establishments { get; set; }

        private int? _sessionNumber;
        private DateOnly? SessionDate { get; set; }
        private TimeOnly? SessionTime { get; set; }
        private DateTime? SessionDateTime { get; set; }

        private DateTime? SessionName { get; set; }

        private int? _gameNumber;
        public string? EventName { get; set; }

        // Selected items for pickers
        private Event _selectedEvent;
        private Establishment _selectedEstablishment;

        public SessionListViewModel()
        {
            var database = new CellularDatabase().GetConnection();
            _SessionRepository = new SessionRepository(database);
            _GameRepository = new GameRepository(database);
            _EventRepository = new EventRepository(database);
            _EstablishmentRepository = new EstablishmentRepository(database);

            Sessions = new ObservableCollection<Session>();
            Games = new ObservableCollection<Game>();
            Events = new ObservableCollection<Event>();
            Establishments = new ObservableCollection<Establishment>();
        }

        // SelectedItem properties for bindings
        public Event SelectedEvent
        {
            get => _selectedEvent;
            set
            {
                if (_selectedEvent != value)
                {
                    _selectedEvent = value;
                    OnPropertyChanged();
                    // Automatically resolve the establishment from the event's location
                    _ = ResolveEstablishmentForEventAsync(value);
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
                }
            }
        }

        // add the new properties to the SessionListViewModel

        public int SelectedEventId { get; set; }

        private string _selectedEventName = string.Empty;
        public string SelectedEventName
        {
            get => _selectedEventName;
            set { if (_selectedEventName != value) { _selectedEventName = value; OnPropertyChanged(); } }
        }

        // Loads sessions already present
        public async Task loadSessions()
        {
            var sessionsFromDb = await _SessionRepository.GetSessionsByUserIdAndEventAsync(Preferences.Get("UserId", 0), Preferences.Get("EventId", 0));
            Sessions.Clear();
            foreach (var session in sessionsFromDb)
            {
                Sessions.Add(session);
                Debug.WriteLine("This is the session ID " + session.SessionId + " For " + session.SessionNumber + " and EventId: " + session.EventId);
            }
        }

        // Loads games already present
        public async Task loadGames()
        {
            var gamesFromDb = await _GameRepository.GetGamesByUserIdAsync(Preferences.Get("UserId", 0));
            Games.Clear();
            foreach (var game in gamesFromDb)
            {
                Games.Add(game);
            }
        }

        // New: load events for the current user
        public async Task loadEvents()
        {
            try
            {
                var eventsFromDb = await _EventRepository.GetEventsByUserIdAsync(Preferences.Get("UserId", 0));
                Events.Clear();
                foreach (var ev in eventsFromDb)
                {
                    Debug.WriteLine("Event: " + ev);
                    Events.Add(ev);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading events: {ex.Message}");
            }
        }

        // New: load establishments for the current user
        public async Task loadEstablishments()
        {
            try
            {
                var estFromDb = await _EstablishmentRepository.GetEstablishmentsByUserIdAsync(Preferences.Get("UserId", 0));
                Establishments.Clear();
                foreach (var e in estFromDb)
                {
                    Debug.WriteLine("Establishment: " + e);
                    Establishments.Add(e);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading establishments: {ex.Message}");
            }
        }

        public async Task<int> getSessionNumberMaxAsync()
        {
            try
            {
                Debug.WriteLine("Attempting to retrieve games from the database.");
                var sessionsFromDb = await _SessionRepository.GetSessionsByUserIdAndEventAsync(Preferences.Get("UserId", 0), Preferences.Get("EventId", 0));
                Debug.WriteLine("Games retrieved successfully.");

                int sessionNumber = 0;
                if (sessionsFromDb.Any())
                {
                    if (sessionsFromDb.Any())
                    {
                        sessionNumber = sessionsFromDb.Max(g => (int?)g.SessionNumber) ?? 0;
                    }
                }
                sessionNumber++;
                return sessionNumber;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"An unexpected error occurred: {ex.Message}");
                return 0;
            }
        }
        public async Task<int> getGameNumberMaxAsync(int session)
        {
            try
            {
                Debug.WriteLine("Attempting to retrieve games from the database.");
                var gamesFromDb = await _GameRepository.GetGamesListBySessionAsync(session, Preferences.Get("UserId", 0));
                Debug.WriteLine("Games retrieved successfully.");

                int gameNumber = gamesFromDb.Max(g => g.GameNumber) ?? 0;         
                gameNumber++;
                return gameNumber;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"An unexpected error occurred: {ex.Message}");
                return 0;
            }
        }


        public async Task AddGame(int sessionNumber)
        {
            int gameNumber = await getGameNumberMaxAsync(sessionNumber);
            Debug.WriteLine("THIS IS THE SESSION NUMBER 2 " + sessionNumber + "------------------------------------------------------------------");
            Debug.WriteLine("THIS IS THE FUCKING GAME NUMERB "+gameNumber+"------------------------------------------------------------------");
            Game game = new Game
            {
                GameNumber = gameNumber,
                SessionId = sessionNumber
            };
            await _GameRepository.AddAsync(game);
        }
        /// <summary>
        /// Looks up the Establishment whose NickName matches the event's Location field
        /// (Event.Location is set to SelectedEstablishment.NickName when the event is created)
        /// and assigns it to SelectedEstablishment.
        /// </summary>
        private async Task ResolveEstablishmentForEventAsync(Event ev)
        {
            if (ev == null || string.IsNullOrWhiteSpace(ev.Location)) return;

            try
            {
                var all = await _EstablishmentRepository.GetEstablishmentsByUserIdAsync(Preferences.Get("UserId", 0));

                // Try NickName first (set by EventPopupViewModel), then FullName (may be used in CSV imports)
                var match = all.FirstOrDefault(e =>
                                string.Equals(e.NickName, ev.Location, StringComparison.OrdinalIgnoreCase))
                         ?? all.FirstOrDefault(e =>
                                string.Equals(e.FullName, ev.Location, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    SelectedEstablishment = match;
                    Debug.WriteLine($"ResolveEstablishment: matched '{ev.Location}' → EstaID {match.EstaID} ('{match.NickName}')");
                }
                else
                {
                    Debug.WriteLine($"ResolveEstablishment: no match for '{ev.Location}'. Available: {string.Join(", ", all.Select(e => e.NickName))}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ResolveEstablishment error: {ex.Message}");
            }
        }

        public async Task AddSession()
        {
            // Resolve establishment from the current event when it hasn't been set via the picker.
            // SessionPopup creates its own fresh ViewModel instance so SelectedEvent is never set;
            // we always fall back to loading the event directly from the EventId preference.
            if (SelectedEstablishment == null)
            {
                int eventId = Preferences.Get("EventId", 0);
                if (eventId > 0)
                {
                    var db = new CellularDatabase().GetConnection();
                    var ev = await db.Table<Event>().Where(e => e.EventId == eventId).FirstOrDefaultAsync();
                    if (ev != null)
                        await ResolveEstablishmentForEventAsync(ev);
                }
            }

            int sessionNumber = await getSessionNumberMaxAsync();
            SessionDateTime = DateTimeCalculator.CombineDateAndTime(SessionDate, SessionTime);
            Session session = new Session
            {
                UserId = Preferences.Get("UserId", 0),
                SessionNumber = sessionNumber,
                EventId = Preferences.Get("EventId", 0),
                Establishment = SelectedEstablishment?.EstaID,
                DateTime = SessionDateTime
            };
            await _SessionRepository.AddAsync(session);
            Debug.WriteLine($"AddSession: saved with Establishment={session.Establishment}");
        }

        // INotifyPropertyChanged implementation and helper
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
