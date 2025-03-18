using SQLite;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Maui.Storage;
using Cellular.ViewModel;

namespace Cellular.Data
{
    public class CellularDatabase
    {
        private readonly SQLiteAsyncConnection _database;

        public CellularDatabase()
        {
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "appdata.db");
            _database = new SQLiteAsyncConnection(dbPath);
        }

        public async Task InitializeAsync()
        {
            await _database.CreateTableAsync<User>();
            await ImportUsersFromCsvAsync();
            await _database.CreateTableAsync<Ball>();
            await ImportBallsFromCsvAsync();
        }

        private async Task ImportBallsFromCsvAsync()
        {
            var csvFileName = "ball.csv"; // File in Resources/Raw

            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync(csvFileName);
                using var reader = new StreamReader(stream);

                var balls = new List<Ball>();
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    var data = line?.Split(',');

                    if (data == null || data.Length < 4) continue; // Ensure valid data

                    var ball = new Ball
                    {
                        UserId = int.TryParse(data[0].Trim(), out int UserId) ? UserId : 0,
                        Name = data[1].Trim(),
                        Diameter = int.TryParse(data[2].Trim(), out int diameter) ? diameter : 0,
                        Weight = int.TryParse(data[3].Trim(), out int weight) ? weight : 0,
                        Core = data[4].Trim(),
                    };

                    var existingUser = await _database.Table<Ball>().FirstOrDefaultAsync(u => u.Name == ball.Name);
                    if (existingUser == null)
                    {
                        balls.Add(ball);
                    }
                }

                if (balls.Count > 0)
                {
                    await _database.InsertAllAsync(balls);
                    Console.WriteLine("Balls imported successfully.");
                }
                else
                {
                    Console.WriteLine("No new balls to import.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading CSV: {ex.Message}");
            }
        }
    
        private async Task ImportUsersFromCsvAsync()
        {
            var csvFileName = "users.csv"; // File in Resources/Raw

            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync(csvFileName);
                using var reader = new StreamReader(stream);

                var users = new List<User>();
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    var data = line?.Split(',');

                    if (data == null || data.Length < 6) continue; // Ensure valid data

                    var user = new User
                    {
                        UserName = data[0].Trim(),
                        PasswordHash = HashPassword(data[1].Trim()),
                        FirstName = data[2].Trim(),
                        LastName = data[3].Trim(),
                        Email = data[4].Trim(),
                        BallList = data[5].Trim(),
                        LastLogin = DateTime.TryParse(data[5].Trim(), out DateTime lastLogin) ? lastLogin : DateTime.Now,
                        PhoneNumber = data[6].Trim()
                    };

                    var existingUser = await _database.Table<User>().FirstOrDefaultAsync(u => u.UserName == user.UserName);
                    if (existingUser == null)
                    {
                        users.Add(user);
                    }
                }

                if (users.Count > 0)
                {
                    await _database.InsertAllAsync(users);
                    Console.WriteLine("Users imported successfully.");
                }
                else
                {
                    Console.WriteLine("No new users to import.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading CSV: {ex.Message}");
            }
        }

        public static string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        public SQLiteAsyncConnection GetConnection() => _database;

        public UserRepository CreateUserRepository() => new(_database);
    }
}


