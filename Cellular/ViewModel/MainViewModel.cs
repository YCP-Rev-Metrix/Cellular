using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using SQLite;

namespace Cellular.ViewModel
{
    internal partial class MainViewModel : INotifyPropertyChanged
    {
        private readonly SQLiteAsyncConnection _database;
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
                if (_userName != value)
                {
                    _userName = value;
                    OnPropertyChanged();
                    LoadUserData(); // Load user info when username changes
                }
            }
        }

        public string Password
        {
            get => _password ?? "N/A";
            set
            {
                if (_password != value)
                {
                    _password = value;
                    OnPropertyChanged();
                }
            }
        }

        public string FirstName
        {
            get => _firstName ?? "N/A";
            set
            {
                if (_firstName != value)
                {
                    _firstName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LastName
        {
            get => _lastName ?? "N/A";
            set
            {
                if (_lastName != value)
                {
                    _lastName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Email
        {
            get => _email ?? "N/A";
            set
            {
                if (_email != value)
                {
                    _email = value;
                    OnPropertyChanged();
                }
            }
        }

        public MainViewModel()
        {
            // Initialize SQLite async connection
            string dbPath = Path.Combine(FileSystem.AppDataDirectory, "appdata.db");
            _database = new SQLiteAsyncConnection(dbPath);
            _database.CreateTableAsync<User>().Wait(); // Initialize the table asynchronously

            // Load stored username from Preferences
            UserName = Preferences.Get("UserName", "Guest");
        }

        public void UpdateUserName(string newName)
        {
            UserName = newName;
            Preferences.Set("UserName", newName);
            LoadUserData(); // Reload user details when the username changes
        }

        private async void LoadUserData()
        {
            // If user is "Guest", reset all fields to N/A
            if (UserName == "Guest")
            {
                Password = "N/A";
                FirstName = "N/A";
                LastName = "N/A";
                Email = "N/A";
                return;
            }

            // Retrieve user details asynchronously from the database
            var user = await _database.Table<User>().FirstOrDefaultAsync(u => u.UserName == UserName);

            if (user != null)
            {
                Password = user.Password ?? "N/A";
                FirstName = user.FirstName ?? "N/A";
                LastName = user.LastName ?? "N/A";
                Email = user.Email ?? "N/A";
            }
            else
            {
                // If no user found, reset to N/A
                Password = "N/A";
                FirstName = "N/A";
                LastName = "N/A";
                Email = "N/A";

                // Log that no user was found
                Console.WriteLine("User not found in the database.");
            }
        }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
        }
    }
}
