using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Storage;
using SQLite;
using System.IO;

namespace Cellular.ViewModel
{
    internal partial class MainViewModel : INotifyPropertyChanged
    {
        private readonly SQLiteConnection _database;
        private string? _userName;
        private string? _password;
        private string? _firstName;
        private string? _lastName;
        private string? _email;

        public string UserName
        {
            get => _userName ?? "Guest";
            set
            {
                _userName = value;
                OnPropertyChanged();
                LoadUserData(); // Load user info when username changes
            }
        }

        public string Password
        {
            get => _password ?? "N/A";
            set
            {
                _password = value;
                OnPropertyChanged();
            }
        }

        public string FirstName
        {
            get => _firstName ?? "N/A";
            set
            {
                _firstName = value;
                OnPropertyChanged();
            }
        }

        public string LastName
        {
            get => _lastName ?? "N/A";
            set
            {
                _lastName = value;
                OnPropertyChanged();
            }
        }

        public string Email
        {
            get => _email ?? "N/A";
            set
            {
                _email = value;
                OnPropertyChanged();
            }
        }

        public MainViewModel()
        {
            // Initialize SQLite connection
            string dbPath = Path.Combine(FileSystem.AppDataDirectory, "appdata.db");
            _database = new SQLiteConnection(dbPath);
            _database.CreateTable<User>();

            // Load stored username
            UserName = Preferences.Get("UserName", "Guest");
        }

        public void UpdateUserName(string newName)
        {
            UserName = newName;
            Preferences.Set("UserName", newName);
            LoadUserData(); // Reload user details
        }

        private void LoadUserData()
        {
            if (UserName == "Guest")
            {
                // If Guest, reset all fields to N/A
                Password = "N/A";
                FirstName = "N/A";
                LastName = "N/A";
                Email = "N/A";
                return;
            }

            var user = _database.Table<User>().FirstOrDefault(u => u.UserName == UserName);

            if (user != null)
            {
                Password = user.Password ?? "N/A";
                FirstName = user.FirstName ?? "N/A";
                LastName = user.LastName ?? "N/A";
                Email = user.Email ?? "N/A";
            }
            else
            {
                Password = "N/A";
                FirstName = "N/A";
                LastName = "N/A";
                Email = "N/A";

                // Log that no user was found
                Console.WriteLine("User not found in the database.");
            }
        }


        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
        }
    }
}
