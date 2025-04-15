using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SQLite;
using Cellular.ViewModel;
using System.Data.Common;

namespace Cellular.Data
{
    public class ShotRepository
    {
        private readonly SQLiteAsyncConnection _conn;

        // Constructor ensures the connection is initialized
        public ShotRepository(SQLiteAsyncConnection conn)
        {
            _conn = conn ?? throw new ArgumentNullException(nameof(conn));
        }

        // Initialize the database table
        public async Task InitAsync()
        {
            await _conn.CreateTableAsync<Shot>();
        }

        // Adds a shot to the database
        public async Task<int> AddAsync(Shot shot)
        {
            try
            {
                await _conn.InsertAsync(shot);
                Console.WriteLine($"Shot added: Id: {shot.ShotId} Frame {shot.Frame}, Shot {shot.ShotNumber}, Pins Down {shot.Count}");
                return shot.ShotId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding shot: {ex.Message}");
                return -1;
            }
        }

        // Updates a shot
        public async Task UpdateShotAsync(Shot shot)
        {
            try
            {
                await _conn.UpdateAsync(shot);
                Console.WriteLine($"Shot updated: Frame {shot.Frame}, Shot {shot.ShotNumber}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating shot: {ex.Message}");
            }
        }

        // Retrieves all shots for a specific game
        public async Task<Shot?> GetShotById(int shotId)
        {
            return await _conn.Table<Shot>()
                                  .Where(s => s.ShotId == shotId)
                                  .FirstOrDefaultAsync();
        }

    }
}
