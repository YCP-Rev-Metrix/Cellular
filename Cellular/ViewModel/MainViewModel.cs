using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using BCrypt.Net;
using SQLite;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Cellular.ViewModel
{
    internal partial class MainViewModel : INotifyPropertyChanged
    {
        private readonly SQLiteAsyncConnection _database;
        private int? _userID;
        private string? _userName;
        private string? _password;
        private string? _firstName;
        private string? _lastName;
        private string? _email;
        public string? _newUserName;
        public string? _phoneNumber;
        public string? _hand;
        public ObservableCollection<string> HandOptions { get; } = ["Right", "Left"];



        public int? UserID
        {
            get => _userID;
            set
            {
                if (_userID != value)
                {
                    _userID = value;
                    OnPropertyChanged();
                    _ = LoadUserData();
                }
            }
        }


        public string UserName
        {
            get => _userName ?? "Guest";
            set
            {
                if (_userName != value)
                {
                    _userName = value;
                    OnPropertyChanged();
                    _ = LoadUserData();
                }
            }
        }

        public string NewUserName
        {
            get => _newUserName ?? UserName;
            set
            {
                if (_newUserName != value)
                {
                    _newUserName = value;
                    OnPropertyChanged();
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

        public string PhoneNumber
        {
            get => _phoneNumber ?? "N/A";
            set
            {
                if (_phoneNumber != value)
                {
                    _phoneNumber = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Hand
        {
            get => _hand;
            set
            {
                if (_hand != value)
                {
                    _hand = value;
                    OnPropertyChanged();
                    _ = SaveHandPreferenceToDatabase();  // Save the updated hand preference
                }
            }
        }

        public MainViewModel()
        {
            string dbPath = Path.Combine(FileSystem.AppDataDirectory, "appdata.db");
            _database = new SQLiteAsyncConnection(dbPath);
            _database.CreateTableAsync<User>().Wait();

            // Retrieve UserId from Preferences
            int storedUserId = Preferences.Get("UserId", -1); // Default to -1 if not set
            UserID = storedUserId != -1 ? storedUserId : null;

            if (UserID != null)
            {
                _ = LoadUserData();
            }
        }

        public void UpdateUserName(string newName)
        {
            UserName = newName;
            Preferences.Set("UserName", newName);
            _ = LoadUserData(); // Reload user details when the username changes
        }

        public async Task LoadUserData()
        {
            if (UserID == null)
            {
                UserName = "Guest";
                Password = "N/A";
                FirstName = "N/A";
                LastName = "N/A";
                Email = "N/A";
                PhoneNumber = "N/A";
                Hand = "Right";
                return;
            }

            var user = await _database.Table<User>().FirstOrDefaultAsync(u => u.UserId == UserID);

            if (user != null)
            {
                UserID = user.UserId;
                UserName = user.UserName ?? "Guest";
                Password = user.PasswordHash ?? "N/A";
                FirstName = user.FirstName ?? "N/A";
                LastName = user.LastName ?? "N/A";
                Email = user.Email ?? "N/A";
                PhoneNumber = user.PhoneNumber ?? "N/A";
                Hand = user.Hand ?? "Right";
            }
            else
            {
                UserID = null;
                UserName = "Guest";
                Password = "N/A";
                FirstName = "N/A";
                LastName = "N/A";
                Email = "N/A";
                PhoneNumber = "N/A";
                Hand = "Right";
            }
        }

        private async Task SaveHandPreferenceToDatabase()
        {
            if (UserID != null)
            {
                var user = await _database.Table<User>().FirstOrDefaultAsync(u => u.UserId == UserID);
                if (user != null)
                {
                    user.Hand = Hand;
                    await _database.UpdateAsync(user);
                }
            }
        }

        public static bool VerifyPassword(string enteredPassword, string storedHash)
        {
            return BCrypt.Net.BCrypt.Verify(enteredPassword, storedHash);
        }

        public void NotifyUserDetailsChanged()
        {
            OnPropertyChanged(nameof(UserName));
            OnPropertyChanged(nameof(FirstName));
            OnPropertyChanged(nameof(LastName));
            OnPropertyChanged(nameof(Email));
            OnPropertyChanged(nameof(PhoneNumber));
        }
        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
        }
    }
}
