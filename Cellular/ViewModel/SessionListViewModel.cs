using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cellular.Data;
using SQLite;

namespace Cellular.ViewModel
{
    internal partial class SessionListViewModel : INotifyPropertyChanged
    {
        private readonly SessionRepository _SessionRepository;
        private readonly GameRepository _GameRepository;
        public ObservableCollection<Session> Sessions { get; set; }
        public ObservableCollection<Game> Games { get; set; }
        private int? _sessionNumber;
        private int? _gameNumber;
        public SessionListViewModel()
        {
            var database = new CellularDatabase().GetConnection();
            _SessionRepository = new SessionRepository(database);
            _GameRepository = new GameRepository(database);
            Sessions = new ObservableCollection<Session>();
            Games = new ObservableCollection<Game>();
        } 
        public async Task loadSessions()
        {
            var sessionsFromDb = await _SessionRepository.GetSessionsByUserIdAsync(Preferences.Get("UserId", 0));
            Sessions.Clear();
            foreach (var session in sessionsFromDb)
            {
                Sessions.Add(session);
                Debug.WriteLine("This is the session ID " + session.SessionId + " For " + session.SessionNumber);
            }
        }

        public async Task loadGames()
        {
            var gamesFromDb = await _GameRepository.GetGamesByUserIdAsync(Preferences.Get("UserId", 0));
            Games.Clear();
            foreach (var game in gamesFromDb)
            {
                Games.Add(game);
                Debug.WriteLine("This is the Game ID " + game.GameId + " and this is the Session ID" + game.Session);

            }
        }
        
        public async Task<int> getSessionNumberMaxAsync()
        {
            try
            {
                Debug.WriteLine("Attempting to retrieve games from the database.");
                var sessionsFromDb = await _SessionRepository.GetSessionsByUserIdAsync(Preferences.Get("UserId", 0));
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

                int gameNumber = gamesFromDb.Max(g => g.GameNumber) ?? 0; ;         
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
            Game game = new Game
            {
                UserId = Preferences.Get("UserId", 0),
                GameNumber = gameNumber,
                Session = sessionNumber
            };
            await _GameRepository.AddAsync(game);
        }
        public async Task AddSession()
        {
            int sessionNumber = await getSessionNumberMaxAsync();
            Session session = new Session
            {
                UserId = Preferences.Get("UserId", 0),
                SessionNumber = sessionNumber
            };
            await _SessionRepository.AddAsync(session);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
