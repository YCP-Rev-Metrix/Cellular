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

        public int currentSessionId = 0;

        public SessionListViewModel()
        {
            _SessionRepository = new SessionRepository(new CellularDatabase().GetConnection());
            Sessions = new ObservableCollection<Session>();

            _GameRepository = new GameRepository(new CellularDatabase().GetConnection());
            Games = new ObservableCollection<Game>();

            loadSessions();
            loadGames();
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

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
