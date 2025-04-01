using Cellular.ViewModel;
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
        }
        public async Task<List<Game>> GetGamesByUserIdAsync(int userID) => await _conn.Table<Game>().Where(u => u.UserId == userID).ToListAsync();

        public async Task<List<Game>> GetGamesBySessionAsync(int session, int userId)
        {
            return await _conn.Table<Game>().Where(g => g.Session == session && g.UserId == userId).ToListAsync();
        }

        public async Task UpdateGameAsync(Game game)
        {
            await _conn.UpdateAsync(game);
        }

        public async Task<Game?> GetGameBySessionAndGameNumberAsync(int session, int gameNumber, int userId)
        {
            return await _conn.Table<Game>()
                              .FirstOrDefaultAsync(g => g.Session == session && g.GameNumber == gameNumber && g.UserId == userId);
        }

    }
}


  


