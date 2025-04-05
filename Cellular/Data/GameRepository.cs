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

        public async Task<List<Game>> GetGamesListBySessionAsync(int sessionId, int userId)
        {
            return await _conn.Table<Game>().Where(g => g.SessionId == sessionId).ToListAsync();
        }
        public async Task<List<Game>> GetGamesByUserIdAsync(int userId)
        {
            var games = await _conn.Table<Game>().ToListAsync();
            var sessions = await _conn.Table<Session>().ToListAsync();

            return (from game in games
                    join session in sessions on game.SessionId equals session.SessionId
                    where session.UserId == userId
                    select game).ToList();
        }

        public async Task UpdateGameAsync(Game game)
        {
            await _conn.UpdateAsync(game);
        }

        public async Task<Game?> GetGame(int sessionId, int gameNumber, int userId)
        {
            return await _conn.Table<Game>()
                                  .Where(g => g.SessionId == sessionId && g.GameNumber == gameNumber)
                                  .FirstOrDefaultAsync();
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


  
