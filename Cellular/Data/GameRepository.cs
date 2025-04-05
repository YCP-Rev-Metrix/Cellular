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
        /*public async Task<List<Game>> GetGamesByUserIdAsync(int userID) => await _conn.Table<Game>().Where(u => u.UserId == userID).ToListAsync();

        public async Task<Game> GetGamesBySessionAsync(int session, int userId)
        {
            return await _conn.Table<Game>().FirstOrDefaultAsync(u => u.Session == session && u.UserId == userId);
        }

        public async Task<List<Game>> GetGamesListBySessionAsync(int session, int userId)
        {
            return await _conn.Table<Game>().Where(g => g.Session == session && g.UserId == userId).ToListAsync();
        }*/

        public async Task UpdateGameAsync(Game game)
        {
            await _conn.UpdateAsync(game);
        }

        public async Task<Game?> GetGame(int session, int gameNumber)
        {
            return await _conn.Table<Game>()
                                  .Where(g => g.Session == session && g.GameNumber == gameNumber)
                                  .FirstOrDefaultAsync();
        }

        public async Task<List<int>> GetFrameIdsByGameIdAsync(int gameId)
        {
            var query = "SELECT Id FROM Frame WHERE GameId = @GameId";
            var frameIds = await _conn.QueryAsync<int>(query, new { GameId = gameId });
            return frameIds.ToList();
        }
    }
}


  
