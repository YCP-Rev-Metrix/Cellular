using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cellular.ViewModel
{
    internal partial class SessionListViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<object> sessions;
        private ObservableCollection<object> games;

        public ObservableCollection<object> Sessions
        {
            get
            {
                return sessions;
            }
            set
            {
                sessions = value;
            }
        }

        public ObservableCollection<object> Games
        {
            get
            {
                return games;
            }
            set
            {
                games = value;
            }
        }

        public SessionListViewModel()
        {
            sessions = new ObservableCollection<object>();
            sessions.Add("First Session");
            sessions.Add("Second Session");
            sessions.Add("Third Session");
            sessions.Add("Fourth Session");

            games = new ObservableCollection<object>();
            (string session, string gamename) game1 = ("First Session", "Game 1");
            games.Add(game1);
            (string session, string gamename) game2 = ("First Session", "Game 2");
            games.Add(game2);
            (string session, string gamename) game3 = ("Second Session", "Game 1");
            games.Add(game3);
            (string session, string gamename) game4 = ("Third Session", "Game 1");
            games.Add(game4);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
