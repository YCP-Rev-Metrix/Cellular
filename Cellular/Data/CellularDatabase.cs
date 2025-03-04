using SQLite;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using Cellular.ViewModel;

namespace Cellular.Data
{
    public class CellularDatabase
    {
        private readonly SQLiteAsyncConnection _database;

        // Constructor initializes the SQLiteAsyncConnection
        public CellularDatabase()
        {
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "appdata.db");
            _database = new SQLiteAsyncConnection(dbPath);
        }

        // Initialize the database and create table asynchronously
        public async Task InitializeAsync()
        {
            // Create the User table if it doesn't exist
            await _database.CreateTableAsync<User>();

            // Optionally, check if the default user exists and create one if not
            var existingUser = await _database.Table<User>().FirstOrDefaultAsync(u => u.UserName == "string");

            // Hardcoded "string" user is used for testing or initial setup
            if (existingUser == null)
            {
                var defaultUser = new User
                {
                    UserName = "string", // Example username
                    Password = "string", // Example password
                    FirstName = "Default",
                    LastName = "User",
                    Email = "defaultuser@example.com",
                    BallList = null,
                    LastLogin = DateTime.Now
                };

                await _database.InsertAsync(defaultUser);
                Console.WriteLine("Default user added.");
            }
            else
            {
                Console.WriteLine("User with username 'string' already exists.");
            }
        }

        // Return the database connection for external usage
        public SQLiteAsyncConnection GetConnection()
        {
            return _database;
        }

        // Create and return a new UserRepository with the database connection
        public UserRepository CreateUserRepository()
        {
            return new UserRepository(_database);
        }
    }
}
