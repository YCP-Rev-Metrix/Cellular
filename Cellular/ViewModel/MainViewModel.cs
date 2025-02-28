using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Storage;

namespace Cellular.ViewModel
{
    internal partial class MainViewModel : INotifyPropertyChanged
    {
        private string? _userName; // Make _userName nullable

        public string UserName
        {
            get => _userName ?? "Guest"; // Return "Guest" if _userName is null
            set
            {
                _userName = value;
                OnPropertyChanged();
            }
        }

        public MainViewModel()
        {
            // Load stored username or set default
            UserName = Preferences.Get("UserName", "Guest");
        }

        public void UpdateUserName(string newName)
        {
            UserName = newName;
            Preferences.Set("UserName", newName); // Save username persistently
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            // Check if propertyName is null before invoking PropertyChanged
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
        }
    }
}
