using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Storage;

namespace Cellular.ViewModel
{
    internal class MainViewModel : INotifyPropertyChanged
    {
        private string _userName;
        public string UserName
        {
            get => _userName;
            set
            {
                _userName = value;
                OnPropertyChanged();
            }
        }

        public MainViewModel()
        {
            // Load the latest stored username
            UserName = Preferences.Get("UserName", "Guest");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
