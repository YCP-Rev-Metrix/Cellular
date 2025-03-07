using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Storage;

namespace Cellular.ViewModel
{
    internal partial class GameInterfaceViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<object> players;

        public ObservableCollection<object> Players
        {
            get
            {
                return players;
            }
            set
            {
                players = value;
            }
        }

        public GameInterfaceViewModel()
        {
            players = new ObservableCollection<object>();
            players.Add("Zac");
            players.Add("Carson");
            players.Add("Thomas");
            players.Add("Josh");
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
