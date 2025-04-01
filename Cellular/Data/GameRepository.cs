using Cellular.ViewModel;
using Microsoft.Maui.Controls;
using SQLite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Cellular.Data
{
    public class GameRepository(SQLiteAsyncConnection conn)
    {
        private readonly SQLiteAsyncConnection _conn = conn ?? throw new ArgumentNullException(nameof(conn));


        // Initialize the database and create the table asynchronously
        public async Task InitAsync()
        {
            await _conn.CreateTableAsync<Game>();  // Create the table if it doesn't exist
        }
        public async Task AddAsync(Game e)
        {
            await _conn.InsertAsync(e);
            Console.WriteLine($"Game ID: {e.GameId} Game Number: {e.GameNumber}");
        }
        public async Task<List<Game>> GetGamesByUserIdAsync(int userID) => await _conn.Table<Game>().Where(u => u.UserId == userID).ToListAsync();

        public async Task<Game> GetGamesBySessionAsync(int session, int userId)
        {
            return await _conn.Table<Game>().FirstOrDefaultAsync(u => u.Session == session && u.UserId == userId);
        }

        public async Task<List<Game>> GetGamesListBySessionAsync(int session, int userId)
        {
            return await _conn.Table<Game>().Where(g => g.Session == session && g.UserId == userId).ToListAsync();
        }

        public async Task UpdateGameAsync(Game game)
        {
            await _conn.UpdateAsync(game);
        }

        public async Task<Game?> GetGame(int session, int gameNumber, int userId)
        {
            return await _conn.Table<Game>()
                                  .Where(g => g.Session == session && g.GameNumber == gameNumber && g.UserId == userId)
                                  .FirstOrDefaultAsync();
        }

        public async Task<List<int>> GetFrameIdsByGameAsync(int sessionNumber, int gameNumber, int userId)
        {
            var game = await GetGame(sessionNumber, gameNumber, userId);
            if (game == null || string.IsNullOrEmpty(game.Frames))
            {
                return new List<int>();
            }
            return game.Frames.Split('_')
                              .Where(frame => !string.IsNullOrEmpty(frame))
                              .Select(int.Parse)
                              .ToList();

        }

        public async Task<List<int>> GetFrameIdsByGameIdAsync(int gameId)
        {
            // Query to get the Frames string from the Game table
            var query = "SELECT Frames FROM Game WHERE GameId = @GameId";
            var framesString = await _conn.ExecuteScalarAsync<string>(query, new { GameId = gameId });

            // If no result is found or the Frames string is empty, return an empty list
            if (string.IsNullOrEmpty(framesString))
            {
                return new List<int>();
            }

            // Split the Frames string by "_" and convert to a list of integers
            return framesString.Split('_')
                               .Where(frame => int.TryParse(frame, out _))  // Ensure valid integers
                               .Select(int.Parse)  // Convert to integers
                               .ToList();
        }
    }
}


  
