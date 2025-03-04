using Cellular.Data;
using Cellular.ViewModel;
using SQLite;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

public class CellularDatabase
{
    private readonly SQLiteAsyncConnection _database;

    // Constructor initializes the SQLiteAsyncConnection
    public CellularDatabase()
    {
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "appdata.db");
        _database = new SQLiteAsyncConnection(dbPath);
    }

    public SQLiteAsyncConnection GetConnection()
    {
        return _database;
    }

    // Initialize the database and create table asynchronously
    public async Task InitializeAsync()
    {
        await _database.CreateTableAsync<User>();  // Ensure the table exists

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

    // Create and return a new UserRepository with the database connection
    public UserRepository CreateUserRepository()
    {
        return new UserRepository(_database);
    }
}
